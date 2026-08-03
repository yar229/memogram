using Memogram.Clients.Memos.Models;
using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Telegram.Handlers;
using Memogram.Services.Telegram.Handlers.Commands;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram.Services.Telegram;

public class TelegramService
{
    private readonly TelegramConfig _config;
    private readonly ILogger<TelegramService> _logger;
    private readonly IMyTelegramBotClient _bot;
    private readonly UserStoreService _storeService;
    private readonly HashSet<string> _allowedUsernames;
    private ICmdHandler[] _botCommands = null!;
    private MessageHandler _messageHandler = null!;
    private CallbackQueryHandler _callbackQueryHandler = null!;

    public TelegramService(TelegramConfig config, IMyTelegramBotClient bot, UserStoreService storeService,
        IEnumerable<ICmdHandler> cmdHandlers,
        MessageHandler messageHandler,
        CallbackQueryHandler callbackQueryHandler,
        ILogger<TelegramService> logger)
    {
        _config = config;
        _bot = bot;
        _storeService = storeService;
        _botCommands = cmdHandlers.ToArray();
        _messageHandler = messageHandler;
        _callbackQueryHandler = callbackQueryHandler;
        
        _logger = logger;

        _allowedUsernames = ParseAllowedUsernames(config.AllowedUsernames);
    }

    private static readonly UpdateType[] AllowedUpdates =
        [UpdateType.Message, UpdateType.EditedMessage, UpdateType.CallbackQuery];

    public async Task Start(CancellationToken ct = default)
    {
        

        await PollingLoopAsync(ct);
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        int offset = 0;
        int consecutiveFailures = 0;
        bool isCommandsSet = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!isCommandsSet)
                { 
                    _logger.LogInformation("Setting bot commands...");
                    await _bot.SetMyCommands(_botCommands.Select(bc => new BotCommand(bc.Command, bc.Usage)));
                    isCommandsSet = true;
                }

                _logger.LogInformation("Bot is listening...");
                await _bot.ReceiveAsync(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.EditedMessage, UpdateType.CallbackQuery] },
                    cancellationToken: ct);

                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                await HandleErrorAsync(_bot, ex, ct);

                var delay = TimeSpan.FromSeconds(Math.Min(consecutiveFailures, 5));
                _logger.LogWarning("Polling error: {Message} {InnerMessage}. Restarting in {Delay:F1}s...", ex.Message, ex.InnerException?.Message, delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackAsync(callbackQuery, ct);
                return;
            }

            if (update.EditedMessage is { } editedMessage)
            {
                await HandleEditedMessageAsync(editedMessage, ct);
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

    #region Handlers ===========================================================================================================

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        bool isUserAllowed = await ProcessUserAllowed(message.Chat.Id, message.From?.Username, ct);
        if (!isUserAllowed)
            return;

        var processed = await ProcessBotCommand(message, ct);
        if (processed)
            return;

        string? accessToken = await ProcessAccessToken(message.From?.Id, message.Chat.Id, ct);
        if (string.IsNullOrEmpty(accessToken))
            return;

        await _messageHandler.HandleMessageCreateAsync(message, accessToken, ct);
    }

    private async Task HandleEditedMessageAsync(Message message, CancellationToken ct)
    {
        bool isUserAllowed = await ProcessUserAllowed(message.Chat.Id, message.From?.Username, ct);
        if (!isUserAllowed)
            return;

        string? accessToken = await ProcessAccessToken(message.From?.Id, message.Chat.Id, ct);
        if (string.IsNullOrEmpty(accessToken))
            return;

        if (null != ExtractBotCommand(message))
            return;

        await _messageHandler.HandleEditedAsync(message, accessToken, ct);
    }

    private async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        bool isUserAllowed = await ProcessUserAllowed(callbackQuery.Message?.Chat.Id, callbackQuery.From.Username, ct);
        if (!isUserAllowed)
            return;

        if (!_storeService.TryGetUserAccessToken(callbackQuery.From?.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "Please start the bot with /start <access_token>", showAlert: true, cancellationToken: ct);
            return;
        }

        await _callbackQueryHandler.HandleAsync(callbackQuery, accessToken, ct);
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
    #endregion Handlers ===========================================================================================================

    private async Task<bool> ProcessBotCommand(Message message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message.Text))
            return false;

        var entity = ExtractBotCommand(message);
        if (null == entity)
            return false;

        string fullCommand = message.Text.Substring(entity.Offset, entity.Length);
        string cleanCommand = fullCommand.Split('@')[0].ToLowerInvariant();
        var handler = _botCommands.FirstOrDefault(h => h.Command == cleanCommand);
        if (null == handler)
            return false;
        if (handler.RequireRegistration && string.IsNullOrEmpty(await ProcessAccessToken(message.From?.Id, message.Chat.Id, ct)))
            return true;

        string arguments = message.Text.Substring(entity.Offset + entity.Length).Trim();
        _logger.LogDebug("Processing command {cmd} for (chat: {chatId}, message: {messageId})", cleanCommand, message.Chat.Id, message.Id);
        await handler.Handle(message, arguments, ct);
        return true;
    }

    private async Task<string?> ProcessAccessToken(long? fromId, long? chatId, CancellationToken ct)
    {
        if (!_storeService.TryGetUserAccessToken(fromId, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            if (null != chatId)
                await _bot.SendMessage(chatId, "Please start the bot with /start <access_token>", cancellationToken: ct);
            return null;
        }
        return accessToken;
    }

    private MessageEntity? ExtractBotCommand(Message message) 
        => message.Entities?.FirstOrDefault(ent => ent.Type == MessageEntityType.BotCommand && ent.Offset == 0);

    private async Task SendError(long? chatId, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, ex.Message);
        try
        {
            if (null != chatId)
                await _bot.SendMessage(chatId, $"Error: {ex.Message}", cancellationToken: ct);
        }
        catch
        {
            _logger.LogError(ex, "Failed to send error to telegram: {Message}", ex.Message);
        }
    }

    private bool IsUserAllowed(string? username)
    {
        if (_allowedUsernames.Count == 0)
            return true;
        if (string.IsNullOrEmpty(username))
            return false;
        return _allowedUsernames.Contains(username.Trim().ToLowerInvariant());
    }

    private async Task<bool> ProcessUserAllowed(long? chatId, string? username, CancellationToken ct)
    {
        if (!IsUserAllowed(username))
        {
            if (string.IsNullOrEmpty(username))
            {
                    await SendError(chatId, new InvalidOperationException("Your account must have a username to use this bot"), ct);
                return false;
            }
            await SendError(chatId, new InvalidOperationException($"Your account {username} is not allowed to use this bot"), ct);
            return false;
        }

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
