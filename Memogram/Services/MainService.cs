using Memogram.Clients.Memos.Models;
using Memogram.Services.Telegram;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using MimeDetective;
using System.Net.Mime;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram.Services;

public partial class MainService
{
    private readonly UserStoreService _storeService;
    private readonly TelegramService _tgService;
    private readonly MemogramService _memoService;

    private readonly ILogger<MainService> _logger;
    private readonly IContentInspector _contentInspector;

    public MainService(UserStoreService storeService, TelegramService tgService, MemogramService memoService,
        ILogger<MainService> logger)
    {
        _logger = logger;

        _tgService = tgService;
        _memoService = memoService;
        _storeService = storeService;
        _storeService.Init();

        _contentInspector = new ContentInspectorBuilder()
        {
            Definitions = MimeDetective.Definitions.DefaultDefinitions.All()
        }.Build();
    }

    public async Task Start(CancellationToken ct = default)
    {
        _logger.LogInformation("Memogram starting...");
        _logger.LogInformation("Instance profile: {Profile}", JsonSerializer.Serialize(_memoService.InstanceProfile));

        _ = _tgService.Start(StartHandler, SearchHandler, HandleMessageAsync, HandleCallbackQueryAsync, ct);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var from = message.From!;

        if (!_storeService.TryGetUserAccessToken(from.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
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
        string msg = $"Content saved as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid})";
        await _tgService.SendMessageSaved(bot, message, chatId, memo, msg, ct);
    }

    public async Task StartHandler(ITelegramBotClient bot, Message message, string accessToken, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        accessToken = accessToken.Trim();

        if (string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Usage: /start <access_token>", cancellationToken: ct);
            return;
        }

        try
        {
            var user = await _memoService.GetCurrentUserAsync(accessToken, ct);
            _storeService.SetUserAccessToken(message.From!.Id, accessToken);
            await bot.SendMessage(chatId, $"Hello {user.DisplayName}!", cancellationToken: ct);
        }
        catch
        {
            await bot.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
        }
    }

    private async Task SearchHandler(ITelegramBotClient bot, Message message, string searchString, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        if (string.IsNullOrEmpty(searchString))
        {
            await bot.SendMessage(chatId, "Usage: /search <words>", cancellationToken: ct);
            return;
        }

        if (!_storeService.TryGetUserAccessToken(message.From!.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await bot.SendMessage(chatId, "Please start the bot with /start <access_token>", cancellationToken: ct);
            return;
        }

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
            await bot.SendMessage(chatId, "No memos found for the specified search criteria.", cancellationToken: ct);
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

        if (!_storeService.TryGetUserAccessToken(userId, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, "Please start the bot with /start <access_token>", showAlert: true, cancellationToken: ct);
            return;
        }

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
        await bot.EditMessageText(chatId, messageId,
            $"Memo updated as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid}) {pinnedMarker}",
            parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQuery(callbackQuery.Id, "Memo updated", cancellationToken: ct);
    }

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
        }
        catch (Exception ex)
        {
            await _tgService.SendError(chatId, new InvalidOperationException($"Failed to save attachment: {ex.Message}"), ct);
        }
    }
}
