using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class CreateMemoRequest
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "PRIVATE";
}
