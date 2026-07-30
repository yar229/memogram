using Memogram.Configs;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Memogram.Services.Telegram;

public class TelegramService
{
    private readonly TelegramConfig _config;
    private readonly ILogger<TelegramService> _logger;
    private readonly ITelegramBotClient _bot;
    private readonly HashSet<string> _allowedUsernames;
    private BotCommandHandler[] _botCommands = null!;
    private Func<Message, CancellationToken, Task> _handleMessage = null!;
    private Func<CallbackQuery, CancellationToken, Task> _handleCallback = null!;

    public TelegramService(TelegramConfig config, ITelegramBotClient bot, ILogger<TelegramService> logger)
    {
        _config = config;
        _bot = bot;
        _logger = logger;

        _allowedUsernames = ParseAllowedUsernames(config.AllowedUsernames);
    }

    public async Task Start(
        Func<Message, string, CancellationToken, Task> startHandler,
        Func<Message, string, CancellationToken, Task> searchHandler,
        Func<Message, CancellationToken, Task> handleMessage,
        Func<CallbackQuery, CancellationToken, Task> handleCallback,
        
        CancellationToken ct = default)
    {
        _handleMessage = handleMessage;
        _handleCallback = handleCallback;

        _botCommands = [
            new BotCommandHandler{Command = new BotCommand("/start", "Usage: /start <memos_user_access_token>"), Handler = startHandler },
            new BotCommandHandler{Command = new BotCommand("/search", "Usage: /search <what_to_search>"), Handler = searchHandler } ];
        await _bot.SetMyCommands(_botCommands.Select(bc => bc.Command));

        _bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] },
            cancellationToken: ct);

        _logger.LogInformation("Bot is listening...");
    }

    public Task SendMessage(long chatId, string message, CancellationToken ct) 
        => _bot.SendMessage(chatId, message, cancellationToken: ct);

    public bool IsUserAllowed(string? username)
    {
        if (_allowedUsernames.Count == 0)
            return true;
        if (string.IsNullOrEmpty(username))
            return false;
        return _allowedUsernames.Contains(username.Trim().ToLowerInvariant());
    }

    public async Task SendMessageSaved(Message message, long chatId, string memoname, string msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.OnlyLikeSavedMessageWith))
        {
            var inlineKeyboard = BuildKeyboard(memoname);
            await _bot.SendMessage(
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
            await _bot.SetMessageReaction(chatId, message.Id, [likeReaction]);
        }
    }

    public Task<Message> EditMessageText(long chatId, int messageId, string message, ParseMode parseMode, InlineKeyboardMarkup inlineKeyboard, CancellationToken ct) 
        => _bot.EditMessageText(chatId, messageId,
                    message,
                    parseMode: parseMode, replyMarkup: inlineKeyboard,
                    cancellationToken: ct
                );

    public Task AnswerCallbackQuery(string callbackQueryId, string message, bool showAlert, CancellationToken ct) 
        => _bot.AnswerCallbackQuery(callbackQueryId, message, showAlert: showAlert, cancellationToken: ct);


    public InlineKeyboardMarkup BuildKeyboard(string memoname)
    {
        return new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData("Public", $"public {memoname}"),
                InlineKeyboardButton.WithCallbackData("Private", $"private {memoname}"),
                InlineKeyboardButton.WithCallbackData("Pin", $"pin {memoname}"),
            ]
        ]);
    }

    public async Task SendMemoMessage(string baseUrl, string memoUrl, string content, long chatId, CancellationToken ct)
    {
        int trimCount = _config.SearchReplyMessagesTrim;
        string trimmedContent = content.Length > trimCount
            ? $"{content[..trimCount]}..."
            : content;
        string tgMessage = $"[🔗]({baseUrl}/{memoUrl}) {trimmedContent.TrimEnd()}";

        await _bot.SendMessage(chatId, tgMessage,
            parseMode: ParseMode.Markdown,
            disableNotification: true,
            linkPreviewOptions: LinkPreviewOptions.Disabled,
            cancellationToken: ct);
    }


    private async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await _handleCallback(callbackQuery, ct);
                return;
            }

            if (update.Message is not { } message || message.From is not { } from)
                return;

            if (string.IsNullOrEmpty(message.Text) && message.Document is null && message.Photo?.Length == 0 && message.Voice is null && message.Video is null && string.IsNullOrEmpty(message.Caption))
                return;

            await HandleMessageAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }
    }



    /// <summary>
    /// 
    /// </summary>
    /// <param name="fileId"></param>
    /// <param name="ms"></param>
    /// <param name="ct"></param>
    /// <returns>filepath</returns>
    /// <exception cref="FileNotFoundException"></exception>
    public async Task<string> GetFileAsync(string fileId, Stream ms, CancellationToken ct)
    {
        var file = await _bot.GetFile(fileId, cancellationToken: ct);
        if (null == file)
            throw new FileNotFoundException($"Telegram cannot find file with fileId = {fileId}" );

        await _bot.DownloadFile(file, ms, ct);
        return file.FilePath!;
    }

    public async Task SendError(long chatId, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, ex.Message);
        try
        {
            await _bot.SendMessage(chatId, $"Error: {ex.Message}", cancellationToken: ct);
        }
        catch
        {
            _logger.LogError(ex, "Failed to send error to telegram: {Message}", ex.Message);
        }
    }


    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var from = message.From!;
        if (!IsUserAllowed(from.Username))
        {
            if (string.IsNullOrEmpty(from.Username))
            {
                await SendError(chatId, new InvalidOperationException("Your account must have a username to use this bot"), ct);
                return;
            }
            await SendError(chatId, new InvalidOperationException($"Your account {from.Username} is not allowed to use this bot"), ct);
            return;
        }

        var processed = await ProcessBotCommand(message, ct);
        if (processed)
            return;

        await _handleMessage(message, ct);
    }

    private Task HandleErrorAsync(ITelegramBotClient _, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ApiRequestException api:
                _logger.LogError(api, "Telegram API Error: [{Code}] {Message}", api.ErrorCode, api.Message);
                break;
            case RequestException rex:
                if (rex.InnerException is HttpRequestException hrex && hrex.HttpRequestError == HttpRequestError.ResponseEnded &&
                    hrex.InnerException is HttpIOException iorex && iorex.HttpRequestError == HttpRequestError.ResponseEnded)
                    _logger.LogTrace(rex, rex.Message);
                else
                    _logger.LogError(rex, rex.Message);
                break;
            default:
                _logger.LogError(exception, "Unknown error in bot update");
                break;
        }
        return Task.CompletedTask;
    }

    private async Task<bool> ProcessBotCommand(Message message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message.Text))
            return false;

        var entity = message.Entities?.FirstOrDefault(ent => ent.Type == MessageEntityType.BotCommand && ent.Offset == 0);
        if (null == entity)
            return false;

        string fullCommand = message.Text.Substring(entity.Offset, entity.Length);
        string cleanCommand = fullCommand.Split('@')[0].ToLower();
        var command = _botCommands.FirstOrDefault(cmd => cmd.Command.Command == cleanCommand);
        if (null == command)
            return false;

        string arguments = message.Text.Substring(entity.Offset + entity.Length).Trim();
        await command.Handler(message, arguments, ct);
        return true;
    }

    private static HashSet<string> ParseAllowedUsernames(string? raw)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
            return allowed;

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
}
