using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class UpdateMemoRequest
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "PRIVATE";

    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }
}
