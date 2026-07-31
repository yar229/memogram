using Memogram.Services.Memos;
using Memogram.Services.Telegram;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Memogram.Services.Telegram.Handlers.Commands;

public class CmdStartHandler(MemogramService _memoService, TelegramService _tgService, UserStoreService _storeService,
    ILogger<CmdStartHandler> _logger)
    : ICmdHandler
{
    public string Command => "/start";
    public string Usage => "Usage: /start <access_token>";

    public async Task Handle(Message message, string args, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var accessToken = args.Trim();

        if (string.IsNullOrEmpty(accessToken))
        {
            await _tgService.SendMessage(chatId, Usage, ct);
            return;
        }

        try
        {
            var user = await _memoService.GetCurrentUserAsync(accessToken, ct);
            await _storeService.SetUserAccessTokenAsync(message.From!.Id, accessToken);
            await _tgService.SendMessage(chatId, $"Hello {user.DisplayName}!", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid access token");
            await _tgService.SendMessage(chatId, "Invalid access token", ct);
        }
    }
}
