using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memogram;

public class JsonStreamConverter : JsonConverter<Stream>
{
    public override void Write(Utf8JsonWriter writer, Stream source, JsonSerializerOptions options)
    {
        if (source == null)
        {
            writer.WriteNullValue();
            return;
        }
        if (source.CanSeek)
            source.Position = 0;

        if (source is MemoryStream sourceMemoryStream)
        {
            writer.WriteBase64StringValue(
                sourceMemoryStream.TryGetBuffer(out var segment)
                    ? (ReadOnlySpan<byte>)segment
                    : (ReadOnlySpan<byte>)sourceMemoryStream.ToArray());
        }
        else
        {
            using var memoryStream = new MemoryStream();
            source.CopyTo(memoryStream);

            writer.WriteBase64StringValue(
                memoryStream.TryGetBuffer(out var segment)
                    ? (ReadOnlySpan<byte>)segment
                    : (ReadOnlySpan<byte>)memoryStream.ToArray());
        }
    }

    public override Stream Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null!;

        return new MemoryStream(Convert.FromBase64String(reader.GetString()!));
    }
}
