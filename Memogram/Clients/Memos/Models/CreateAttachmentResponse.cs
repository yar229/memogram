using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class CreateAttachmentResponse
{
    [JsonPropertyName("filename")]
    public required string Filename { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("createTime")]
    public required DateTime CreateTime { get; set; }

    [JsonPropertyName("externalLink")]
    public required string ExternalLink { get; set; }
}