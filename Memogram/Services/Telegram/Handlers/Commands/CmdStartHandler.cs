using Memogram.Clients.Telegram;
using Memogram.Services.Memos;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Memogram.Services.Telegram.Handlers.Commands;

public class CmdStartHandler(MemogramService _memoService, IMyTelegramBotClient _tgBotClient, UserStoreService _storeService,
    ILogger<CmdStartHandler> _logger)
    : ICmdHandler
{
    public string Command => "/start";
    public string Usage => "Usage: /start <access_token>";

    public bool RequireRegistration => false;

    public async Task Handle(Message message, string args, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var accessToken = args.Trim();

        if (string.IsNullOrEmpty(accessToken))
        {
            await _tgBotClient.SendMessage(chatId, Usage, cancellationToken: ct);
            return;
        }

        try
        {
            var user = await _memoService.GetCurrentUserAsync(accessToken, ct);
            await _storeService.SetUserAccessTokenAsync(message.From!.Id, accessToken);
            await _tgBotClient.SendMessage(chatId, $"Hello {user.DisplayName}!", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid access token");
            await _tgBotClient.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
        }
    }
}
