using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Memos;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram.Services.Telegram.Handlers.Commands;

public class CmdSearchHandler(MemogramService _memoService, IMyTelegramBotClient _tgBotClient, UserStoreService _storeService,
    TelegramConfig _config,
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
            await _tgBotClient.SendMessage(chatId, Usage, cancellationToken: ct);
            return;
        }

        if (!_storeService.TryGetUserAccessToken(message.From!.Id, out var accessToken) || string.IsNullOrEmpty(accessToken))
        {
            await _tgBotClient.SendMessage(chatId, "Please start the bot with /start <access_token>", cancellationToken: ct);
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
            await _tgBotClient.SendMessage(chatId, "Invalid access token", cancellationToken: ct);
            return;
        }

        var memos = await _memoService.SearchMemoAsync(searchString, accessToken!, user.Name, user.Username, ct);

        if (memos.Count == 0)
            await _tgBotClient.SendMessage(chatId, "No memos found for the specified search criteria.", cancellationToken: ct);
        else
        {
            foreach (var memo in memos)
                await SendMemoMessage(_memoService.BaseUrl, memo.Name, memo.Content, chatId, ct);
        }
    }

    public async Task SendMemoMessage(string baseUrl, string memoUrl, string content, long chatId, CancellationToken ct)
    {
        int trimCount = _config.SearchReplyMessagesTrim;
        string trimmedContent = content.Length > trimCount
            ? $"{content[..trimCount]}..."
            : content;
        string tgMessage = $"[🔗]({baseUrl}/{memoUrl}) {trimmedContent.TrimEnd()}";

        await _tgBotClient.SendMessage(chatId, tgMessage,
            parseMode: ParseMode.Markdown,
            disableNotification: true,
            linkPreviewOptions: LinkPreviewOptions.Disabled,
            cancellationToken: ct);
    }
}
