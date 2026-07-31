using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class CreateAttachmentRequest
{
    /// <summary>
    /// The filename of the attachment.
    /// </summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; set; }

    /// <summary>
    /// The content of the attachment.
    /// </summary>
    [JsonPropertyName("content")]
    //[JsonConverter(typeof(JsonStreamConverter))]
    public required Stream Content { get; set; }

    /// <summary>
    /// The MIME type of the attachment.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// The related memo. Refer to Memo.name. Format: memos/{memo}
    /// </summary>
    [JsonPropertyName("memo")]
    public string? Memo { get; set; }
}
