using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Telegram.Handlers;
using Memogram.Services.Telegram.Handlers.Commands;
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
    private readonly IMyTelegramBotClient _bot;
    private readonly HashSet<string> _allowedUsernames;
    private ICmdHandler[] _botCommands = null!;
    private MessageHandler _handleMessage = null!;
    private CallbackQueryHandler _handleCallback = null!;

    public TelegramService(TelegramConfig config, IMyTelegramBotClient bot, ILogger<TelegramService> logger)
    {
        _config = config;
        _bot = bot;
        _logger = logger;

        _allowedUsernames = ParseAllowedUsernames(config.AllowedUsernames);
    }

    public async Task Start(IEnumerable<ICmdHandler> cmdHandlers,
        MessageHandler handleMessage,
        CallbackQueryHandler handleCallback,
        CancellationToken ct = default)
    {
        _handleMessage = handleMessage;
        _handleCallback = handleCallback;

        _botCommands = cmdHandlers.ToArray();
        await _bot.SetMyCommands(_botCommands.Select(bc => new BotCommand(bc.Command, bc.Usage)));

        _bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.EditedMessage, UpdateType.CallbackQuery] },
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


    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await _handleCallback.HandleAsync(bot, callbackQuery, ct);
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

        await _handleMessage.HandleAsync(_bot, message, ct);
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
        var handler = _botCommands.FirstOrDefault(h => h.Command == cleanCommand);
        if (null == handler)
            return false;

        string arguments = message.Text.Substring(entity.Offset + entity.Length).Trim();
        await handler.Handle(message, arguments, ct);
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
