using Memogram.Clients.Memos.Models;
using Memogram.Configs;
using Memogram.Store;
using Microsoft.Extensions.Logging;
using MimeDetective;
using System.Net.Mime;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram;

public partial class Service
{
    //private readonly TelegramBotClient _bot;
    //private BotCommandHandler[] _botCommands;

    //private readonly MemosClient _memosClient;
    //private readonly MemogramConfig _memogramConfig;
    //private readonly TelegramConfig _telegramConfig;
    private readonly UserStore _store;
    //private readonly HttpClient _tgHttpClient;
    private readonly ILogger<Service> _logger;
    private readonly TelegramService _tgService;
    private readonly MemogramService _memoService;

    //private readonly ConcurrentDictionary<string, Memo> _mediaGroupCache = new();
    //private readonly object _mediaGroupMutex = new();

    //private InstanceProfile? _instanceProfile;
    //private readonly HashSet<string> _allowedUsernames;

    private readonly IContentInspector _contentInspector;

    public Service(LocalStorageConfig localStorageConfig, /*TelegramConfig telegramConfig,*/ ILogger<Service> logger, ILoggerFactory loggerFactory,
        TelegramService tgService, MemogramService memoService)
    {
        //_memogramConfig = memogramConfig;
        //_telegramConfig = telegramConfig;
        _logger = logger;

        _tgService = tgService;
        _memoService = memoService;


        //var baseUrl = _memogramConfig.ServerAddr;
        //baseUrl = baseUrl.Replace("dns:", "", StringComparison.Ordinal);
        //if (!baseUrl.StartsWith("http://", StringComparison.Ordinal) && !baseUrl.StartsWith("https://", StringComparison.Ordinal))
        //{
        //    baseUrl = "http://" + baseUrl;
        //}
        //_memosClient = new MemosClient(baseUrl, logger: loggerFactory.CreateLogger<MemosClient>());

        _store = new UserStore(localStorageConfig.Filename);
        _store.Init();

        //_allowedUsernames = ParseAllowedUsernames(_telegramConfig.AllowedUsernames);

        //var handler = CreateHttpClientHandler(_telegramConfig.Proxy);
        //var telegramHttpClient = new HttpClient(handler);
        //_tgHttpClient = new HttpClient(handler);

        //_bot = !string.IsNullOrEmpty(_telegramConfig.BotProxyAddr)
        //    ? new TelegramBotClient(new TelegramBotClientOptions(_telegramConfig.BotToken, _telegramConfig.BotProxyAddr), telegramHttpClient)
        //    : new TelegramBotClient(_telegramConfig.BotToken, telegramHttpClient);

        _contentInspector = new ContentInspectorBuilder()
        {
            Definitions = MimeDetective.Definitions.DefaultDefinitions.All()
        }.Build();
    }

    //private static HttpClientHandler CreateHttpClientHandler(string proxyUrl)
    //{
    //    if (string.IsNullOrWhiteSpace(proxyUrl))
    //        return new HttpClientHandler();

    //    var proxy = new WebProxy(proxyUrl);
    //    return new HttpClientHandler { Proxy = proxy, UseProxy = true };
    //}

    public async Task Start(CancellationToken ct = default)
    {
        _logger.LogInformation("Memogram starting...");

        //try
        //{
            //_instanceProfile = await _memoService.GetInstanceProfileAsync(ct);
            _logger.LogInformation("Instance profile: {Profile}", JsonSerializer.Serialize(_memoService.InstanceProfile));
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogWarning(ex, "Failed to get instance profile");
        //}

        //_botCommands = [
        //    new BotCommandHandler{Command = new BotCommand("/start", "Usage: /start <memos_user_access_token>"), Handler = StartHandler },
        //    new BotCommandHandler{Command = new BotCommand("/search", "Usage: /search <what_to_search>"), Handler = SearchHandler } ];
        //await _bot.SetMyCommands(_botCommands.Select(bc => bc.Command));

        //_bot.StartReceiving(
        //    HandleUpdateAsync,
        //    HandleErrorAsync,
        //    new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] },
        //    cancellationToken: ct );

        _ = _tgService.Start(StartHandler, SearchHandler, HandleMessageAsync, HandleCallbackQueryAsync, ct);

        await Task.Delay(Timeout.Infinite, ct);
    }



    private async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var from = message.From!;

        //if (!IsUserAllowed(from.Username))
        //{
        //    if (string.IsNullOrEmpty(from.Username))
        //    {
        //        await SendError(bot, chatId, new InvalidOperationException("Your account must have a username to use this bot"), ct);
        //        return;
        //    }
        //    await SendError(bot, chatId, new InvalidOperationException($"Your account {from.Username} is not allowed to use this bot"), ct);
        //    return;
        //}

        //var processed = await ProcessBotCommand(bot, message, ct);
        //if (processed)
        //    return;

        if (!_store.TryGetUserAccessToken(from.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Please start the bot with /start <access_token>", cancellationToken: ct);
            return;
        }

        string content = _memoService.PrepareMessageContent(message);

        bool hasAttachment = message.Document is not null
            || message.Photo?.Length > 0
            || message.Voice is not null
            || message.Video is not null;

        if (string.IsNullOrEmpty(content) && !hasAttachment)
        {
            await bot.SendMessage(chatId, "Please input memo content", cancellationToken: ct);
            return;
        }

        //var authClient = _memosClient.WithAuthentication(accessToken!);
        Memo memo;
        try
        {
            memo = await _memoService.HandleMemoCreation(accessToken!, message.MediaGroupId, content, ct);
        }
        catch (Exception)
        {
            await bot.SendMessage(chatId, "Failed to create memo", cancellationToken: ct);
            return;
        }

        if (message.Document is not null)
            await ProcessFileMessage(accessToken!, bot, chatId, message.Document.FileId, memo, ct);
        if (message.Voice is not null)
            await ProcessFileMessage(accessToken!, bot, chatId, message.Voice.FileId, memo, ct);
        if (message.Video is not null)
            await ProcessFileMessage(accessToken!, bot, chatId, message.Video.FileId, memo, ct);
        if (message.Photo?.Length > 0)
        {
            var photo = message.Photo[^1];
            await ProcessFileMessage(accessToken!, bot, chatId, photo.FileId, memo, ct);
        }

        var memoUid = _memoService.ExtractMemoUidFromName(memo.Name);
        //var baseUrl = _memogramConfig.ServerAddr;  //TODO:!!!!
        //if (_instanceProfile?.InstanceUrl is { Length: > 0 })
        //    baseUrl = _instanceProfile.InstanceUrl;
        string msg = $"Content saved as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid})";
        await _tgService.SendMessageSaved(bot, message, chatId, memo, msg, ct);
    }



    public async Task StartHandler(ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var userId = message.From!.Id;
        var accessToken = args.Trim();

        if (string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Usage: /start <access_token>", cancellationToken: ct);
            return;
        }

        //var authClient = _memosClient.WithAuthentication(accessToken);
        try
        {
            var user = await _memoService.GetCurrentUserAsync(accessToken, ct);
            _store.SetUserAccessToken(userId, accessToken);
            await bot.SendMessage(chatId, $"Hello {user.DisplayName}!", cancellationToken: ct);
        }
        catch
        {
            await bot.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
        }
    }

    private async Task SearchHandler(ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var userId = message.From!.Id;
        var searchString = args;

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

        //var authClient = _memosClient.WithAuthentication(accessToken!);
        Clients.Memos.Models.User? user;
        try
        {
            user = await _memoService.GetCurrentUserAsync(accessToken!, ct);
        }
        catch
        {
            await bot.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
            return;
        }

        var filter = _memoService.BuildMemoSearchFilter(searchString, user);
        var memos = await _memoService.ListMemosAsync(accessToken!, pageSize: 10, filter: filter, ct);

        if (memos.Count == 0)
        {
            await bot.SendMessage(chatId, "No memos found for the specified search criteria.", cancellationToken: ct);
        }
        else
        {
            foreach (var memo in memos)
            {
                string trimmedContent = memo.Content.Length > 200
                    ? $"{memo.Content[..200]}..."
                    : memo.Content;
                string tgMessage = $"[🔗]({_memoService.BaseUrl}/{memo.Name}) {trimmedContent.TrimEnd()}";

                await bot.SendMessage(chatId, tgMessage, 
                    parseMode: ParseMode.Markdown, 
                    disableNotification: true,
                    linkPreviewOptions: LinkPreviewOptions.Disabled,
                    cancellationToken: ct);
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

        //var authClient = _memosClient.WithAuthentication(accessToken!);

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
            memo = await _memoService.GetMemoAsync(accessToken!, memoName, ct);
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
            memo = await _memoService.UpdateMemoAsync(accessToken!, memo, ct);
        }
        catch
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, "Failed to update memo", showAlert: true, cancellationToken: ct);
            return;
        }

        var pinnedMarker = memo.Pinned ? "📌" : "";
        var memoUid = _memoService.ExtractMemoUidFromName(memo.Name);
        var inlineKeyboard = _tgService.BuildKeyboard(memo);
        await bot.EditMessageText(
            chatId,
            messageId,
            $"Memo updated as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid}) {pinnedMarker}",
            parseMode: ParseMode.Markdown,
            replyMarkup: inlineKeyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQuery(callbackQuery.Id, "Memo updated", cancellationToken: ct);
    }

    //private async Task<Memo> HandleMemoCreation(MemosClient memoClient, string? mediaGroupId, string content, CancellationToken ct)
    //{
    //    if (!string.IsNullOrEmpty(mediaGroupId))
    //    {
    //        lock (_mediaGroupMutex)
    //        {
    //            if (_mediaGroupCache.TryGetValue(mediaGroupId, out var cached))
    //            {
    //                return cached;
    //            }
    //        }

    //        var memo = await memoClient.CreateMemoAsync(content, tags: _memogramConfig.TagsToAdd, ct: ct);
    //        _mediaGroupCache[mediaGroupId] = memo;
    //        return memo;
    //    }

    //    return await memoClient.CreateMemoAsync(content, tags: _memogramConfig.TagsToAdd, ct: ct);
    //}

    private async Task ProcessFileMessage(string accessToken, ITelegramBotClient bot, long chatId, string fileId, Memo memo, CancellationToken ct)
    {
        try
        {
            var file = await _tgService.GetFile(bot, fileId, ct);

            if (string.IsNullOrEmpty(file.ContentType) || MediaTypeNames.Application.Octet.Equals(file.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                var bestMatch = _contentInspector.Inspect(file.Content).ByMimeType().FirstOrDefault();
                if (null != bestMatch && !string.IsNullOrEmpty(bestMatch.MimeType))
                    file.ContentType = bestMatch.MimeType;
            }

            await _memoService.ProcessFileMessage(accessToken, 
                new MemogramService.FileInfo { FilePath = file.FilePath, Content = file.Content, ContentType = file.ContentType},
                chatId, fileId, memo, ct);

            //await memosClient.CreateAttachmentAsync(
            //    filename: Path.GetFileName(file.FilePath),
            //    contentType: file.ContentType,
            //    content: file.Content,
            //    memoName: memo.Name,
            //    ct: ct
            //);
        }
        catch (Exception ex)
        {
            await _tgService.SendError(chatId, new InvalidOperationException($"Failed to save attachment: {ex.Message}"), ct);
        }
    }

    //private static async Task<(string FilePath, byte[] Content, string ContentType)> GetFile(ITelegramBotClient bot, string fileId, CancellationToken ct)
    //{
    //    var file = await bot.GetFile(fileId, cancellationToken: ct);
    //    var fileLink = $"https://api.telegram.org/file/bot{_telegramConfig.BotToken}/{file.FilePath}";

    //    var response = await _tgHttpClient.GetAsync(fileLink, ct);
    //    response.EnsureSuccessStatusCode();

    //    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    //    var contentType = response.Content.Headers.ContentType?.MediaType ?? MediaTypeNames.Application.Octet;
    //    return (file.FilePath, bytes, contentType);
    //}

    //private static InlineKeyboardMarkup BuildKeyboard(Memo memo)
    //{
    //    return new InlineKeyboardMarkup(new[]
    //    {
    //        new[]
    //        {
    //            InlineKeyboardButton.WithCallbackData("Public", $"public {memo.Name}"),
    //            InlineKeyboardButton.WithCallbackData("Private", $"private {memo.Name}"),
    //            InlineKeyboardButton.WithCallbackData("Pin", $"pin {memo.Name}"),
    //        }
    //    });
    //}

    //private async Task SendError(ITelegramBotClient bot, long chatId, Exception ex, CancellationToken ct)
    //{
    //    _logger.LogError(ex, "{Message}", ex.Message);
    //    try
    //    {
    //        await bot.SendMessage(chatId, $"Error: {ex.Message}", cancellationToken: ct);
    //    }
    //    catch
    //    {
    //        _logger.LogError(ex, "Failed to send error to telegram: {Message}", ex.Message);
    //    }
    //}

    //private static HashSet<string> ParseAllowedUsernames(string raw)
    //{
    //    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    //    foreach (var entry in raw.Split(','))
    //    {
    //        var trimmed = entry.Trim().ToLowerInvariant();
    //        if (!string.IsNullOrEmpty(trimmed))
    //        {
    //            allowed.Add(trimmed);
    //        }
    //    }
    //    return allowed;
    //}

    //private bool IsUserAllowed(string? username)
    //{
    //    if (_allowedUsernames.Count == 0)
    //        return true;
    //    if (string.IsNullOrEmpty(username))
    //        return false;
    //    return _allowedUsernames.Contains(username.Trim().ToLowerInvariant());
    //}



}
