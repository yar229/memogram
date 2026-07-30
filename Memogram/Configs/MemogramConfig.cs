namespace Memogram.Configs;

public class MemogramConfig
{
    public string ServerAddr { get; set; } = string.Empty;
    public string[] TagsToAdd { get; set; } = Array.Empty<string>();

    public TimeSpan MediaCacheTtl { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerAddr))
            throw new InvalidOperationException("Memogram:ServerAddr is required");
    }
}

