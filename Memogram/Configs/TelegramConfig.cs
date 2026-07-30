namespace Memogram.Configs;

public class TelegramConfig
{
    public required string BotToken { get; set; } = string.Empty;
    public string? BotProxyAddr { get; set; } = string.Empty;
    public string? Proxy { get; set; } = string.Empty;
    public string? AllowedUsernames { get; set; } = string.Empty;

    public string? OnlyLikeSavedMessageWith { get; set; } = string.Empty;
    
    public required int SearchReplyMessagesTrim { get; set; } = 200;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            throw new InvalidOperationException("Telegram:BotToken is required");
    }
}
