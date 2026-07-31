using Memogram.Clients.Memos.Models;
using Memogram.Services.Memos;
using Memogram.Services.MimeTypeDetectors;
using Memogram.Services.Telegram;
using Memogram.Services.Telegram.Handlers.Commands;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
using System.Text.Json;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static System.Net.Mime.MediaTypeNames;

namespace Memogram.Services;

public class MainService
{
    private readonly UserStoreService _storeService;
    private readonly TelegramService _tgService;
    private readonly MemogramService _memoService;
    private readonly IMimeTypeDetector _mimeTypeDetector;

    private readonly ILogger<MainService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public MainService(UserStoreService storeService, TelegramService tgService, MemogramService memoService,
        IMimeTypeDetector mimeTypeDetector,
        ILogger<MainService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _tgService = tgService;
        _memoService = memoService;

        _storeService = storeService;

        _mimeTypeDetector = mimeTypeDetector;
    }

    public async Task Start(CancellationToken ct = default)
    {
        _logger.LogInformation("Memogram starting...");

        await _storeService.InitializeAsync();
        await _memoService.InitializeAsync(ct);

        _logger.LogInformation("Instance profile: {Profile}", JsonSerializer.Serialize(_memoService.InstanceProfile));

        await _tgService
            .Start([ 
                    new CmdStartHandler(_memoService, _tgService, _storeService, _loggerFactory.CreateLogger<CmdStartHandler>()),
                    new CmdSearchHandler(_memoService, _tgService, _storeService, _loggerFactory.CreateLogger<CmdSearchHandler>())
                ],
                HandleMessageAsync, HandleCallbackQueryAsync,
                ct);
            //.ContinueWith(t => 
            //{
            //    if (t.IsFaulted) 
            //        _logger.LogCritical(t.Exception, "Bot failed to start");
            //}, TaskContinuationOptions.OnlyOnFaulted);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var from = message.From!;

        if (!_storeService.TryGetUserAccessToken(from.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await _tgService.SendMessage(chatId, "Please start the bot with /start <access_token>", ct);
            return;
        }

        string content = _memoService.PrepareMessageContent(message);

        bool hasAttachment = message.Document is not null
            || message.Photo?.Length > 0
            || message.Voice is not null
            || message.Video is not null;

        if (string.IsNullOrEmpty(content) && !hasAttachment)
        {
            await _tgService.SendMessage(chatId, "Please input memo content", ct);
            return;
        }

        Memo memo;
        try
        {
            memo = await _memoService.HandleMemoCreation(accessToken!, message.MediaGroupId, content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create memo");
            await _tgService.SendMessage(chatId, "Failed to create memo", ct);
            return;
        }

        if (message.Document is not null)
            await ProcessFileMessage(accessToken!, chatId, message.Document.FileId, memo, ct);
        if (message.Voice is not null)
            await ProcessFileMessage(accessToken!, chatId, message.Voice.FileId, memo, ct);
        if (message.Video is not null)
            await ProcessFileMessage(accessToken!, chatId, message.Video.FileId, memo, ct);
        if (message.Photo?.Length > 0)
        {
            var photo = message.Photo[^1];
            await ProcessFileMessage(accessToken!, chatId, photo.FileId, memo, ct);
        }

        var memoUid = MemogramService.ExtractMemoUidFromName(memo.Name);
        string msg = $"Content saved as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid})";
        await _tgService.SendMessageSaved(message, chatId, memo.Name, msg, ct);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var data = callbackQuery.Data ?? "";
        var userId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var messageId = callbackQuery.Message?.MessageId ?? 0;

        if (!_storeService.TryGetUserAccessToken(userId, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await _tgService.AnswerCallbackQuery(callbackQuery.Id, "Please start the bot with /start <access_token>", showAlert: true, ct);
            return;
        }

        var parts = data.Split(' ');
        if (parts.Length != 2)
        {
            await _tgService.AnswerCallbackQuery(callbackQuery.Id, "Invalid command", showAlert: true, ct);
            return;
        }

        var action = parts[0];
        var memoName = parts[1];

        Memo memo;
        try
        {
            memo = await _memoService.GetMemoAsync(accessToken!, memoName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memo {memoName} not found", memoName);
            await _tgService.AnswerCallbackQuery(callbackQuery.Id, $"Memo {memoName} not found", true, ct);
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
                await _tgService.AnswerCallbackQuery(callbackQuery.Id, "Unknown action", showAlert: true, ct);
                return;
        }

        try
        {
            memo = await _memoService.UpdateMemoAsync(accessToken!, memo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update memo callbackQuery.Id = {id}", callbackQuery.Id);
            await _tgService.AnswerCallbackQuery(callbackQuery.Id, "Failed to update memo", showAlert: true, ct);
            return;
        }

        var pinnedMarker = memo.Pinned ? "📌" : "";
        var memoUid = MemogramService.ExtractMemoUidFromName(memo.Name);
        var inlineKeyboard = _tgService.BuildKeyboard(memo.Name);
        await _tgService.EditMessageText(chatId, messageId,
            $"Memo updated as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid}) {pinnedMarker}",
            ParseMode.Markdown, inlineKeyboard,
            ct
        );

        await _tgService.AnswerCallbackQuery(callbackQuery.Id, "Memo updated", showAlert: false, ct);
    }

    private async Task ProcessFileMessage(string accessToken, long chatId, string fileId, Memo memo, CancellationToken ct)
    {
        var (filepath, contentStream) = await _tgService.GetFileAsync(fileId, ct);
        try
        {
            var contentType = _mimeTypeDetector.Detect(filepath, contentStream);

            await _memoService.ProcessFileMessage(accessToken, 
                new MemogramService.FileInfo { FilePath = filepath, Content = contentStream, ContentType = contentType },
                chatId, fileId, memo, ct);
        }
        catch (Exception ex)
        {
            await _tgService.SendError(chatId, new InvalidOperationException($"Failed to save attachment: {ex.Message}"), ct);
        }
        finally
        {
            await contentStream.DisposeAsync();
        }
    }
}
