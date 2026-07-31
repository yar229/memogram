using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Memogram.Clients.Memos.Models;

public sealed class AttachmentJsonContent : HttpContent
{
    private readonly CreateAttachmentRequest _request;
    private readonly byte[] _prefix;
    private readonly byte[] _suffix;
    public AttachmentJsonContent(CreateAttachmentRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (request.Content is null)
            throw new ArgumentException("Attachment content stream must not be null.", nameof(request));

        Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        _prefix = BuildPrefix(request);
        _suffix = "\"}"u8.ToArray();
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        Stream source = _request.Content;
        if (source.CanSeek)
            source.Position = 0;

        await stream.WriteAsync(_prefix, cancellationToken);

        using (var transform = new ToBase64Transform())
        using (var cryptoStream = new CryptoStream(stream, transform, CryptoStreamMode.Write, leaveOpen: true))
        {
            await source.CopyToAsync(cryptoStream, 81920).ConfigureAwait(false); // Standard 81,920-byte buffer for high-performance streaming
            cryptoStream.FlushFinalBlock();
        }

        await stream.WriteAsync(_suffix, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_request.Content.CanSeek)
        {
            length = _prefix.Length + GetBase64Length(_request.Content.Length) + _suffix.Length;
            return true;
        }

        length = -1;
        return false;
    }

    private static byte[] BuildPrefix(CreateAttachmentRequest request)
    {
        var sb = new StringBuilder(128);
        sb.Append("{\"filename\":").Append(JsonSerializer.Serialize(request.Filename));
        sb.Append(",\"type\":").Append(JsonSerializer.Serialize(request.Type));
        if (request.Memo is not null)
            sb.Append(",\"memo\":").Append(JsonSerializer.Serialize(request.Memo));
        sb.Append(",\"content\":\"");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static long GetBase64Length(long byteLength)
        => 4 * ((byteLength + 2) / 3);
}
