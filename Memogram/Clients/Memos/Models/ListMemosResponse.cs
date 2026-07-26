using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class ListMemosResponse
{
    [JsonPropertyName("memos")]
    public List<Memo> Memos { get; set; } = new();
}
