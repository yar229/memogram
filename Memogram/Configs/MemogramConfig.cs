namespace Memogram.Configs;

public class MemogramConfig
{
    public required string ServerAddr { get; set; } = string.Empty;
    public string[] TagsToAdd { get; set; } = Array.Empty<string>();

    public required TimeSpan MediaCacheTtl { get; set; }

    public IconsConfig? Icons { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerAddr))
            throw new InvalidOperationException("Memogram:ServerAddr is required");
    }

    public class IconsConfig
    {
        public string? Human { get; set; } = "👤";
        public string? Bot { get; set; } = "🤖";
    }
}

