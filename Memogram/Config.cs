namespace Memogram;

public class MemogramConfig
{
    public string ServerAddr { get; set; } = string.Empty;
    public string Data { get; set; } = "data.txt";

    public string[] TagsToAdd { get; set; } = Array.Empty<string>();

    public string OnlyLikeSavedMessageWith { get;set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerAddr))
            throw new InvalidOperationException("Memogram:ServerAddr is required");

        if (string.IsNullOrWhiteSpace(Data))
            Data = "data.txt";

        if (!File.Exists(Data))
            File.WriteAllText(Data, string.Empty);

        Data = Path.GetFullPath(Data);
    }
}

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
