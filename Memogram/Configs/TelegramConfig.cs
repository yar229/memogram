namespace Memogram.Configs;

public class TelegramConfig : IValidableConfig
{
    public static string SectionName => "Telegram";

    public required string BotToken { get; set; } = string.Empty;
    public string BotProxyAddr { get; set; } = "https://api.telegram.org";
    public string? Proxy { get; set; } = string.Empty;
    public string? AllowedUsernames { get; set; } = string.Empty;

    public string? OnlyLikeSavedMessageWith { get; set; } = string.Empty;
    
    public required int SearchReplyMessagesTrim { get; set; } = 200;

    public TimeSpan CacheMessageForEditTime { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            throw new InvalidOperationException("Telegram:BotToken is required");

        if (string.IsNullOrWhiteSpace(BotProxyAddr))
            BotProxyAddr = "https://api.telegram.org";
    }
}
