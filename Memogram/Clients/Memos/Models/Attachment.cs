using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class Attachment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;
}
