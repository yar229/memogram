using System.Net;
using System.Text;
using System.Text.Json;
using Memogram.Clients.Memos;
using Memogram.Clients.Memos.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Memogram.Tests;

public class ClientTests
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[]? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return await responder(request);
        }
    }

    [Fact]
    public async Task CreateAttachmentAsync_SendsFlatJsonBody_WithFilenameTypeMemoAndBase64Content()
    {
        var bytes = Encoding.UTF8.GetBytes("hello world");
        using var content = new MemoryStream(bytes);

        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"filename\":\"file.txt\",\"name\":\"attachments/1\",\"createTime\":\"2026-01-01T00:00:00Z\",\"externalLink\":\"\"}",
                Encoding.UTF8,
                "application/json"),
        }));

        using var httpClient = new HttpClient(handler);
        var client = new MemosClient("http://localhost:1234", httpClient, NullLogger<MemosClient>.Instance);

        var result = await client.CreateAttachmentAsync("token", "file.txt", "text/plain", content, memoName: "memos/1");

        Assert.Equal("attachments/1", result.Name);
        Assert.Equal("/api/v1/attachments", handler.Request?.RequestUri?.AbsolutePath);

        var body = Encoding.UTF8.GetString(handler.Body!);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("file.txt", root.GetProperty("filename").GetString());
        Assert.Equal("text/plain", root.GetProperty("type").GetString());
        Assert.Equal("memos/1", root.GetProperty("memo").GetString());
        Assert.Equal(Convert.ToBase64String(bytes), root.GetProperty("content").GetString());
    }

    [Fact]
    public async Task AttachmentJsonContent_SeekableStreamAtEnd_SerializesFullContent()
    {
        var bytes = Encoding.UTF8.GetBytes("hello world");
        using var content = new MemoryStream(bytes);
        content.Position = content.Length;

        using var jsonContent = new AttachmentJsonContent(new CreateAttachmentRequest
        {
            Filename = "file.txt",
            Type = "text/plain",
            Content = content,
        });

        var body = await jsonContent.ReadAsByteArrayAsync();

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("file.txt", doc.RootElement.GetProperty("filename").GetString());
        Assert.Equal(Convert.ToBase64String(bytes), doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CreateAttachmentAsync_OnServerError_DoesNotRetry()
    {
        var bytes = Encoding.UTF8.GetBytes("hello world");
        using var content = new MemoryStream(bytes);

        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        using var httpClient = new HttpClient(handler);
        var client = new MemosClient("http://localhost:1234", httpClient, NullLogger<MemosClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CreateAttachmentAsync("token", "file.txt", "text/plain", content, memoName: "memos/1"));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CreateMemoAsync_OnServerError_Retries()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        using var httpClient = new HttpClient(handler);
        var client = new MemosClient("http://localhost:1234", httpClient, NullLogger<MemosClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CreateMemoAsync("token", "hello"));

        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task GetMemoAsync_OnTransientError_Retries()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts < 3)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"name\":\"memos/1\",\"content\":\"hello\",\"visibility\":\"PRIVATE\",\"pinned\":false}",
                    Encoding.UTF8,
                    "application/json"),
            });
        });

        using var httpClient = new HttpClient(handler);
        var client = new MemosClient("http://localhost:1234", httpClient, NullLogger<MemosClient>.Instance);

        await client.GetMemoAsync("token", "memos/1");

        Assert.Equal(3, attempts);
    }
}
