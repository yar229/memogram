using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Memogram.Clients.Memos;
using Memogram.Clients.Memos.Models;
using Memogram.Store;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Memogram;

public partial class Service
{
    private readonly TelegramBotClient _bot;
    private readonly MemosClient _client;
    private readonly MemogramConfig _memogramConfig;
    private readonly TelegramConfig _telegramConfig;
    private readonly UserStore _store;
    private readonly HttpClient _httpClient;

    private readonly ConcurrentDictionary<string, Memo> _mediaGroupCache = new();
    private readonly object _mediaGroupMutex = new();

    private InstanceProfile? _instanceProfile;
    private readonly HashSet<string> _allowedUsernames;

    public Service(MemogramConfig memogramConfig, TelegramConfig telegramConfig)
    {
        _memogramConfig = memogramConfig;
        _telegramConfig = telegramConfig;

        var baseUrl = _memogramConfig.ServerAddr;
        baseUrl = baseUrl.Replace("dns:", "", StringComparison.Ordinal);
        if (!baseUrl.StartsWith("http://", StringComparison.Ordinal) && !baseUrl.StartsWith("https://", StringComparison.Ordinal))
        {
            baseUrl = "http://" + baseUrl;
        }

        _client = new MemosClient(baseUrl);
        _store = new UserStore(_memogramConfig.Data);
        _store.Init();

        _allowedUsernames = ParseAllowedUsernames(_telegramConfig.AllowedUsernames);

        var handler = CreateHttpClientHandler(_telegramConfig.Proxy);
        var telegramHttpClient = new HttpClient(handler);
        _httpClient = new HttpClient(handler);

        _bot = !string.IsNullOrEmpty(_telegramConfig.BotProxyAddr)
            ? new TelegramBotClient(new TelegramBotClientOptions(_telegramConfig.BotToken, _telegramConfig.BotProxyAddr), telegramHttpClient)
            : new TelegramBotClient(_telegramConfig.BotToken, telegramHttpClient);
    }

    private static HttpClientHandler CreateHttpClientHandler(string proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
            return new HttpClientHandler();

        var proxy = new WebProxy(proxyUrl);
        return new HttpClientHandler { Proxy = proxy, UseProxy = true };
    }

    public async Task Start(CancellationToken ct = default)
    {
        Console.WriteLine("Memogram started");

        try
        {
            _instanceProfile = await _client.GetInstanceProfileAsync(ct);
            Console.WriteLine($"Instance profile: {_instanceProfile.InstanceUrl}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to get instance profile: {ex.Message}");
        }

        _bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions
            {
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            },
            cancellationToken: ct
        );

        Console.WriteLine("Bot is listening...");
        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(bot, callbackQuery, ct);
                return;
            }

            if (update.Message is not { } message || message.From is not { } from)
                return;

            if (string.IsNullOrEmpty(message.Text) && message.Document is null && message.Photo?.Length == 0 && message.Voice is null && message.Video is null && string.IsNullOrEmpty(message.Caption))
                return;

            await HandleMessageAsync(bot, message, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling update: {ex}");
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var from = message.From!;

        if (!IsUserAllowed(from.Username))
        {
            if (string.IsNullOrEmpty(from.Username))
            {
                await SendError(bot, chatId, new InvalidOperationException("Your account must have a username to use this bot"), ct);
                return;
            }
            await SendError(bot, chatId, new InvalidOperationException($"Your account {from.Username} is not allowed to use this bot"), ct);
            return;
        }

        var text = message.Text ?? string.Empty;

        if (text.StartsWith("/start", StringComparison.Ordinal))
        {
            await StartHandler(bot, message, ct);
            return;
        }
        if (text.StartsWith("/search", StringComparison.Ordinal))
        {
            await SearchHandler(bot, message, ct);
            return;
        }

        if (!_store.TryGetUserAccessToken(from.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Please start the bot with /start <access_token>", cancellationToken: ct);
            return;
        }

        var content = text;
        var entities = message.Entities ?? Array.Empty<MessageEntity>();
        if (!string.IsNullOrEmpty(message.Caption))
        {
            content = message.Caption;
            entities = message.CaptionEntities ?? Array.Empty<MessageEntity>();
        }
        if (entities.Length > 0)
        {
            content = FormatContent(content, entities);
        }

        if (message.ForwardOrigin is not null)
        {
            content = PrependForwardedFrom(message.ForwardOrigin, content);
        }

        bool hasAttachment = message.Document is not null
            || message.Photo?.Length > 0
            || message.Voice is not null
            || message.Video is not null;

        if (string.IsNullOrEmpty(content) && !hasAttachment)
        {
            await bot.SendMessage(chatId, "Please input memo content", cancellationToken: ct);
            return;
        }

        var authClient = _client.WithAuthentication(accessToken!);
        Memo memo;
        try
        {
            memo = await HandleMemoCreation(authClient, message.MediaGroupId, content, ct);
        }
        catch (Exception)
        {
            await bot.SendMessage(chatId, "Failed to create memo", cancellationToken: ct);
            return;
        }

        if (message.Document is not null)
            await ProcessFileMessage(authClient, bot, chatId, message.Document.FileId, memo, ct);
        if (message.Voice is not null)
            await ProcessFileMessage(authClient, bot, chatId, message.Voice.FileId, memo, ct);
        if (message.Video is not null)
            await ProcessFileMessage(authClient, bot, chatId, message.Video.FileId, memo, ct);
        if (message.Photo?.Length > 0)
        {
            var photo = message.Photo[^1];
            await ProcessFileMessage(authClient, bot, chatId, photo.FileId, memo, ct);
        }

        var memoUid = Util.ExtractMemoUidFromName(memo.Name);
        var baseUrl = _memogramConfig.ServerAddr;
        if (_instanceProfile?.InstanceUrl is { Length: > 0 })
        {
            baseUrl = _instanceProfile.InstanceUrl;
        }

        if (!string.IsNullOrEmpty(_memogramConfig.OnlyLikeSavedMessageWith))
        {
            var inlineKeyboard = BuildKeyboard(memo);
            await bot.SendMessage(
                chatId,
                $"Content saved as {memo.Visibility} with [{memo.Name}]({baseUrl}/memos/{memoUid})",
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }

        var likeReaction = new ReactionTypeEmoji { Emoji = "✍️" };
        await bot.SetMessageReaction(chatId, message.Id, [likeReaction]);
    }

    private async Task StartHandler(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var userId = message.From!.Id;
        var accessToken = (message.Text ?? "").Replace("/start", "", StringComparison.Ordinal).Trim();

        if (string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Usage: /start <access_token>", cancellationToken: ct);
            return;
        }

        var authClient = _client.WithAuthentication(accessToken);
        try
        {
            var user = await authClient.GetCurrentUserAsync(ct);
            _store.SetUserAccessToken(userId, accessToken);
            await bot.SendMessage(chatId, $"Hello {user.DisplayName}!", cancellationToken: ct);
        }
        catch
        {
            await bot.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
        }
    }

    private async Task SearchHandler(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var userId = message.From!.Id;
        var searchString = (message.Text ?? "").Replace("/search", "", StringComparison.Ordinal).Trim();

        if (string.IsNullOrEmpty(searchString))
        {
            await bot.SendMessage(chatId, "Usage: /search <words>", cancellationToken: ct);
            return;
        }

        if (!_store.TryGetUserAccessToken(userId, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Please start the bot with /start <access_token>", cancellationToken: ct);
            return;
        }

        var authClient = _client.WithAuthentication(accessToken!);
        Clients.Memos.Models.User? user;
        try
        {
            user = await authClient.GetCurrentUserAsync(ct);
        }
        catch
        {
            await bot.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
            return;
        }

        var filter = BuildMemoSearchFilter(searchString, user);
        var memos = await authClient.ListMemosAsync(pageSize: 10, filter: filter, ct);

        if (memos.Count == 0)
        {
            await bot.SendMessage(chatId, "No memos found for the specified search criteria.", cancellationToken: ct);
        }
        else
        {
            foreach (var memo in memos)
            {
                var tgMessage = memo.Name + "\n" + memo.Content;
                await bot.SendMessage(chatId, tgMessage, cancellationToken: ct);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var data = callbackQuery.Data ?? "";
        var userId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var messageId = callbackQuery.Message?.MessageId ?? 0;

        if (!_store.TryGetUserAccessToken(userId, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, "Please start the bot with /start <access_token>", showAlert: true, cancellationToken: ct);
            return;
        }

        var authClient = _client.WithAuthentication(accessToken!);

        var parts = data.Split(' ');
        if (parts.Length != 2)
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, "Invalid command", showAlert: true, cancellationToken: ct);
            return;
        }

        var action = parts[0];
        var memoName = parts[1];

        Memo memo;
        try
        {
            memo = await authClient.GetMemoAsync(memoName, ct);
        }
        catch
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, $"Memo {memoName} not found", showAlert: true, cancellationToken: ct);
            return;
        }

        switch (action)
        {
            case "public":
                memo.Visibility = "PUBLIC";
                break;
            case "protected":
                memo.Visibility = "PROTECTED";
                break;
            case "private":
                memo.Visibility = "PRIVATE";
                break;
            case "pin":
                memo.Pinned = !memo.Pinned;
                break;
            default:
                await bot.AnswerCallbackQuery(callbackQuery.Id, "Unknown action", showAlert: true, cancellationToken: ct);
                return;
        }

        try
        {
            memo = await authClient.UpdateMemoAsync(memo, ct);
        }
        catch
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, "Failed to update memo", showAlert: true, cancellationToken: ct);
            return;
        }

        var pinnedMarker = memo.Pinned ? "📌" : "";
        var memoUid = Util.ExtractMemoUidFromName(memo.Name);
        var baseUrl = _memogramConfig.ServerAddr;
        if (_instanceProfile?.InstanceUrl is { Length: > 0 })
        {
            baseUrl = _instanceProfile.InstanceUrl;
        }

        var inlineKeyboard = BuildKeyboard(memo);
        await bot.EditMessageText(
            chatId,
            messageId,
            $"Memo updated as {memo.Visibility} with [{memo.Name}]({baseUrl}/memos/{memoUid}) {pinnedMarker}",
            parseMode: ParseMode.Markdown,
            replyMarkup: inlineKeyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQuery(callbackQuery.Id, "Memo updated", cancellationToken: ct);
    }

    private async Task<Memo> HandleMemoCreation(MemosClient memoClient, string? mediaGroupId, string content, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(mediaGroupId))
        {
            lock (_mediaGroupMutex)
            {
                if (_mediaGroupCache.TryGetValue(mediaGroupId, out var cached))
                {
                    return cached;
                }
            }

            var memo = await memoClient.CreateMemoAsync(content, tags: _memogramConfig.TagsToAdd, ct: ct);
            _mediaGroupCache[mediaGroupId] = memo;
            return memo;
        }

        return await memoClient.CreateMemoAsync(content, tags: _memogramConfig.TagsToAdd, ct: ct);
    }

    private async Task ProcessFileMessage(MemosClient client, ITelegramBotClient bot, long chatId, string fileId, Memo memo, CancellationToken ct)
    {
        try
        {
            var file = await bot.GetFile(fileId, cancellationToken: ct);
            var fileLink = $"https://api.telegram.org/file/bot{_telegramConfig.BotToken}/{file.FilePath}";

            var response = await _httpClient.GetAsync(fileLink, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            await client.CreateAttachmentAsync(
                filename: Path.GetFileName(file.FilePath),
                contentType: contentType,
                content: bytes,
                memoName: memo.Name,
                ct: ct
            );
        }
        catch (Exception ex)
        {
            await SendError(bot, chatId, new InvalidOperationException($"Failed to save attachment: {ex.Message}"), ct);
        }
    }

    private static InlineKeyboardMarkup BuildKeyboard(Memo memo)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Public", $"public {memo.Name}"),
                InlineKeyboardButton.WithCallbackData("Private", $"private {memo.Name}"),
                InlineKeyboardButton.WithCallbackData("Pin", $"pin {memo.Name}"),
            }
        });
    }

    internal static string BuildMemoSearchFilter(string searchString, Clients.Memos.Models.User? user)
    {
        var filter = $"content.contains(\"{searchString}\")";
        if (user is null)
            return filter;

        var creator = user.Name;
        if (string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(user.Username))
        {
            creator = "users/" + user.Username;
        }
        if (string.IsNullOrEmpty(creator))
            return filter;

        return $"{filter} && creator == \"{creator}\"";
    }

    private static string PrependForwardedFrom(MessageOrigin origin, string content)
    {
        string originName = string.Empty;
        string? originUsername = null;

        switch (origin)
        {
            case MessageOriginUser userOrigin:
                var user = userOrigin.SenderUser;
                originName = string.IsNullOrEmpty(user.LastName)
                    ? user.FirstName
                    : $"{user.FirstName} {user.LastName}";
                originUsername = user.Username;
                break;
            case MessageOriginHiddenUser hiddenOrigin:
                originName = string.IsNullOrEmpty(hiddenOrigin.SenderUserName) ? "Hidden User" : hiddenOrigin.SenderUserName;
                break;
            case MessageOriginChat chatOrigin:
                originName = chatOrigin.SenderChat.Title ?? string.Empty;
                originUsername = chatOrigin.SenderChat.Username;
                break;
            case MessageOriginChannel channelOrigin:
                originName = channelOrigin.Chat.Title ?? string.Empty;
                originUsername = channelOrigin.Chat.Username;
                break;
        }

        if (!string.IsNullOrEmpty(originUsername))
        {
            return $"⏩[{originName}](https://t.me/{originUsername})\n>{content}";
        }
        return $"⏩{originName}\n>{content}";
    }

    internal static string FormatContent(string content, MessageEntity[] entities)
    {
        var sorted = entities.OrderBy(e => e.Offset).ThenBy(e => e.Length).ToList();

        var sb = new StringBuilder();
        int cursor = 0;

        foreach (var entity in sorted)
        {
            if (!IsSupportedEntity(entity.Type))
                continue;

            int start = entity.Offset;
            int end = entity.Offset + entity.Length;

            if (start < cursor)
                continue;
            if (start >= content.Length)
                break;
            if (end > content.Length)
                end = content.Length;

            sb.Append(content[cursor..start]);
            var segment = content[start..end];
            sb.Append(ApplyEntityFormatting(segment, entity));
            cursor = end;
        }

        sb.Append(content[cursor..]);
        return sb.ToString();
    }

    private static bool IsSupportedEntity(MessageEntityType? entityType)
    {
        return entityType is MessageEntityType.Url
            or MessageEntityType.TextLink
            or MessageEntityType.Bold
            or MessageEntityType.Italic;
    }

    private static string ApplyEntityFormatting(string segment, MessageEntity entity)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return segment;

        var match = EntityRegex().Match(segment);
        if (!match.Success)
            return segment;

        var prefix = match.Groups[1].Value;
        var core = match.Groups[2].Value;
        var suffix = match.Groups[3].Value;

        return entity.Type switch
        {
            MessageEntityType.Url => $"{prefix}[{core}]({core}){suffix}",
            MessageEntityType.TextLink => $"{prefix}[{core}]({entity.Url}){suffix}",
            MessageEntityType.Bold => $"{prefix}**{core}**{suffix}",
            MessageEntityType.Italic => $"{prefix}*{core}*{suffix}",
            _ => segment,
        };
    }

    private async Task SendError(ITelegramBotClient bot, long chatId, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"Error: {ex.Message}");
        try
        {
            await bot.SendMessage(chatId, $"Error: {ex.Message}", cancellationToken: ct);
        }
        catch
        {
            // Ignore send errors
        }
    }

    private static HashSet<string> ParseAllowedUsernames(string raw)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw.Split(','))
        {
            var trimmed = entry.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(trimmed))
            {
                allowed.Add(trimmed);
            }
        }
        return allowed;
    }

    private bool IsUserAllowed(string? username)
    {
        if (_allowedUsernames.Count == 0)
            return true;
        if (string.IsNullOrEmpty(username))
            return false;
        return _allowedUsernames.Contains(username.Trim().ToLowerInvariant());
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };
        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^(\s*)(.*?)(\s*)$")]
    private static partial Regex EntityRegex();
}
