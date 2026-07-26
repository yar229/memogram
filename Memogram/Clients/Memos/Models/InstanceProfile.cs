using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

// DTOs matching Memos REST API

public class InstanceProfile
{
    [JsonPropertyName("instanceUrl")]
    public string InstanceUrl { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
