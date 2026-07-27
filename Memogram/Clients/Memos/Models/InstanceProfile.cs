using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class InstanceProfile
{
    [JsonPropertyName("instanceUrl")]
    public string InstanceUrl { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; } = string.Empty;

    [JsonPropertyName("demo")]
    public bool? IsDemo { get; set; }

    [JsonPropertyName("commit")]
    public string? Commit { get; set; }

    [JsonPropertyName("needsSetup")]
    public bool? NeedsSetup { get; set; }

    [JsonPropertyName("admin")]
    public User? Admin { get; set; }
}
