using Telegram.Bot;

namespace Memogram.Clients.Telegram;

public class MyTelegramBotClient : TelegramBotClient, IMyTelegramBotClient
{
    public HttpClient? HttpClient => _httpClient;
    private readonly HttpClient? _httpClient;

    public TelegramBotClientOptions Options { get; private set; }

    public MyTelegramBotClient(TelegramBotClientOptions options, HttpClient? httpClient = null, CancellationToken cancellationToken = default) 
        : base(options, httpClient, cancellationToken)
    {
        _httpClient = httpClient;
        Options = options;
    }

    public MyTelegramBotClient(string token, HttpClient? httpClient = null, CancellationToken cancellationToken = default) 
        : base(token, httpClient, cancellationToken)
    {
        _httpClient = httpClient;
    }
}
