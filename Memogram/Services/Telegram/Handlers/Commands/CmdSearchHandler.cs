using Memogram.Services.Memos;
using Memogram.Services.Telegram;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Memogram.Services.Telegram.Handlers.Commands;

public class CmdSearchHandler(MemogramService _memoService, TelegramService _tgService, UserStoreService _storeService,
    ILogger<CmdSearchHandler> _logger)
    : ICmdHandler
{
    public string Command => "/search";
    public string Usage => "Usage: /search <words>";

    public async Task Handle(Message message, string searchString, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        if (string.IsNullOrEmpty(searchString))
        {
            await _tgService.SendMessage(chatId, Usage, ct);
            return;
        }

        if (!_storeService.TryGetUserAccessToken(message.From!.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await _tgService.SendMessage(chatId, "Please start the bot with /start <access_token>", ct);
            return;
        }

        Clients.Memos.Models.User? user;
        try
        {
            user = await _memoService.GetCurrentUserAsync(accessToken!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid access token");
            await _tgService.SendMessage(chatId, "Invalid access token", ct);
            return;
        }

        var memos = await _memoService.SearchMemoAsync(searchString, accessToken!, user.Name, user.Username, ct);

        if (memos.Count == 0)
            await _tgService.SendMessage(chatId, "No memos found for the specified search criteria.", ct);
        else
        {
            foreach (var memo in memos)
                await _tgService.SendMemoMessage(_memoService.BaseUrl, memo.Name, memo.Content, chatId, ct);
        }
    }
}
