using Memogram.Clients.Memos.Models;
using Memogram.Clients.Telegram;
using Memogram.Services.Memos;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Memogram.Services.Telegram.Handlers;

public class CallbackQueryHandler(MemogramService _memoService, IMyTelegramBotClient _tgBotClient,
        ILogger<CallbackQueryHandler> _logger)
{
    public async Task HandleAsync(CallbackQuery callbackQuery, string memoAccessToken, CancellationToken ct)
    {
        var data = callbackQuery.Data ?? "";
        var userId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var messageId = callbackQuery.Message?.MessageId ?? 0;

        var parts = data.Split(' ');
        if (parts.Length != 2)
        {
            await _tgBotClient.AnswerCallbackQuery(callbackQuery.Id, "Invalid command", showAlert: true, cancellationToken: ct);
            return;
        }

        var action = parts[0];
        var memoName = parts[1];

        Memo memo;
        try
        {
            memo = await _memoService.GetMemoAsync(memoAccessToken!, memoName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memo {memoName} not found", memoName);
            await _tgBotClient.AnswerCallbackQuery(callbackQuery.Id, $"Memo {memoName} not found", true, cancellationToken: ct);
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
                await _tgBotClient.AnswerCallbackQuery(callbackQuery.Id, "Unknown action", showAlert: true, cancellationToken: ct);
                return;
        }

        try
        {
            memo = await _memoService.UpdateMemoAsync(memoAccessToken!, memo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update memo callbackQuery.Id = {id}", callbackQuery.Id);
            await _tgBotClient.AnswerCallbackQuery(callbackQuery.Id, "Failed to update memo", showAlert: true, cancellationToken: ct);
            return;
        }

        var pinnedMarker = memo.Pinned ? "📌" : "";
        var memoUid = MemogramService.ExtractMemoUidFromName(memo.Name);
        var inlineKeyboard = BuildKeyboard(memo.Name);
        await _tgBotClient.EditMessageText(chatId, messageId,
            $"Memo updated as {memo.Visibility} with [{memo.Name}]({_memoService.BaseUrl}/memos/{memoUid}) {pinnedMarker}",
            ParseMode.Markdown, inlineKeyboard,
            cancellationToken: ct);

        await _tgBotClient.AnswerCallbackQuery(callbackQuery.Id, "Memo updated", showAlert: false, cancellationToken: ct);
    }

    public static InlineKeyboardMarkup BuildKeyboard(string memoname)
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
}
