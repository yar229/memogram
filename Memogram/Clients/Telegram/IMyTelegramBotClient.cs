using Telegram.Bot;

namespace Memogram.Clients.Telegram;

public interface IMyTelegramBotClient : ITelegramBotClient
{
    HttpClient? HttpClient { get;}
}
