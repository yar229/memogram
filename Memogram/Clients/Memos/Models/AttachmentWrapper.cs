using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class AttachmentWrapper
{
    [JsonPropertyName("attachment")]
    public Attachment? Attachment { get; set; }
}
