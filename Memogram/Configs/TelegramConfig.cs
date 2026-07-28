namespace Memogram.Configs;

public class TelegramConfig
{
    public string BotToken { get; set; } = string.Empty;
    public string BotProxyAddr { get; set; } = string.Empty;
    public string Proxy { get; set; } = string.Empty;
    public string AllowedUsernames { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            throw new InvalidOperationException("Telegram:BotToken is required");
    }
}
