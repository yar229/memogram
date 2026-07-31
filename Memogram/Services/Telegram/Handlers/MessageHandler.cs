using Memogram.Clients.Memos.Models;
using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Memos;
using Memogram.Services.MimeTypeDetectors;
using Memogram.Services.Telegram;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Memogram.Services.Telegram.Handlers;

public class MessageHandler
{
    private readonly UserStoreService _storeService;
    private readonly MemogramService _memoService;
    private readonly TelegramConfig _config;
    private readonly IMimeTypeDetector _mimeTypeDetector;
    private readonly ILogger<MessageHandler> _logger;

    public MessageHandler(UserStoreService storeService, MemogramService memoService,
        TelegramConfig config,
        IMimeTypeDetector mimeTypeDetector,
        ILogger<MessageHandler> logger)
    {
        _storeService = storeService;
        _memoService = memoService;
        _config = config;
        _mimeTypeDetector = mimeTypeDetector;
        _logger = logger;
    }

    public async Task HandleAsync(IMyTelegramBotClient bot, Message message, CancellationToken ct)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create memo");
            await bot.SendMessage(chatId, "Failed to create memo", cancellationToken: ct);
            return;
        }

        if (message.Document is not null)
            await ProcessFileMessage(bot, accessToken!, chatId, message.Document.FileId, memo, ct);
        if (message.Voice is not null)
            await ProcessFileMessage(bot, accessToken!, chatId, message.Voice.FileId, memo, ct);
        if (message.Video is not null)
            await ProcessFileMessage(bot, accessToken!, chatId, message.Video.FileId, memo, ct);
        if (message.Photo?.Length > 0)
        {
            var photo = message.Photo[^1];
            await ProcessFileMessage(bot, accessToken!, chatId, photo.FileId, memo, ct);
        }

        var memoUid = MemogramService.ExtractMemoUidFromName(memo.Name);
        string msg = $"Content saved as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid})";
        await SendMessageSaved(bot, message, chatId, memo.Name, msg, ct);
    }

    private async Task ProcessFileMessage(IMyTelegramBotClient bot, string accessToken, long chatId, string fileId, Memo memo, CancellationToken ct)
    {
        var (filepath, contentStream) = await GetFileAsync(bot, fileId, ct);
        try
        {
            var contentType = _mimeTypeDetector.Detect(filepath, contentStream);

            await _memoService.ProcessFileMessage(accessToken,
                new MemogramService.FileInfo { FilePath = filepath, Content = contentStream, ContentType = contentType },
                chatId, fileId, memo, ct);
        }
        catch (Exception ex)
        {
            await SendError(bot, chatId, new InvalidOperationException($"Failed to save attachment: {ex.Message}"), ct);
        }
        finally
        {
            await contentStream.DisposeAsync();
        }
    }

    public async Task SendMessageSaved(IMyTelegramBotClient bot, Message message, long chatId, string memoname, string msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.OnlyLikeSavedMessageWith))
        {
            var inlineKeyboard = CallbackQueryHandler.BuildKeyboard(memoname);
            await bot.SendMessage(
                chatId,
                msg,
                parseMode: ParseMode.Markdown,
                disableNotification: true,
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }
        else
        {
            var likeReaction = new ReactionTypeEmoji { Emoji = _config.OnlyLikeSavedMessageWith };
            await bot.SetMessageReaction(chatId, message.Id, [likeReaction]);
        }
    }

    public async Task<(string filePath, Stream content)> GetFileAsync(IMyTelegramBotClient bot, string fileId, CancellationToken ct)
    {
        var file = await bot.GetFile(fileId, cancellationToken: ct);
        if (null == file)
            throw new FileNotFoundException($"Telegram cannot find file with fileId = {fileId}");
        var fileLink = $"{_config.BotProxyAddr}/file/bot{_config.BotToken}/{file.FilePath!}";

        var response = await bot.HttpClient!.GetAsync(fileLink, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return (file.FilePath!, stream);
    }

    public async Task SendError(ITelegramBotClient bot, long chatId, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, ex.Message);
        try
        {
            await bot.SendMessage(chatId, $"Error: {ex.Message}", cancellationToken: ct);
        }
        catch
        {
            _logger.LogError(ex, "Failed to send error to telegram: {Message}", ex.Message);
        }
    }

}
