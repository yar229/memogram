namespace Memogram.Configs;

public class TelegramConfig : IValidableConfig
{
    public static string SectionName => "Telegram";

    public required string BotToken { get; set; } = string.Empty;
    public string BotProxyAddr { get; set; } = "https://api.telegram.org";
    public string? Proxy { get; set; } = string.Empty;
    public string? AllowedUsernames { get; set; } = string.Empty;

    public required bool DoReplyToMessage { get; set; } = false;

    public required ReactionsConfig Reactions { get; set; } = new ();

    public required int SearchReplyMessagesTrim { get; set; } = 200;

    public TimeSpan CacheMessageForEditTime { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            throw new InvalidOperationException("Telegram:BotToken is required");

        if (string.IsNullOrWhiteSpace(BotProxyAddr))
            BotProxyAddr = "https://api.telegram.org";
    }


    public class ReactionsConfig
    {
        public string MemoCreated { get; set; } = "👌";

        public string MemoEdited { get; set; } = "✍";

        public string MemoEditFailed { get; set; } = "🥴";
    }
}
