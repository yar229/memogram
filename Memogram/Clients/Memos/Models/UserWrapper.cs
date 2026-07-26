using System.Text.Json.Serialization;

namespace Memogram.Clients.Memos.Models;

public class UserWrapper
{
    [JsonPropertyName("user")]
    public User? User { get; set; }
}
