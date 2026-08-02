using System.Net;
using System.Text;
using System.Text.Json;
using Memogram.Clients.Memos;
using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Memos;
using Memogram.Services.MimeTypeDetectors;
using Memogram.Services.Telegram;
using Memogram.Services.Telegram.Handlers;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;

namespace Memogram.Tests;

public class MemoLinkTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int RequestCount { get; private set; }
        public string? LastPatchBody { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Patch && request.Content is not null)
                LastPatchBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private static HttpResponseMessage MemoResponse(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"name\":\"memos/1\",\"content\":\"old\",\"visibility\":\"PRIVATE\",\"pinned\":false}",
            Encoding.UTF8,
            "application/json"),
    };

    private static TelegramConfig TgConfig() => new()
    {
        BotToken = "123:token",
        SearchReplyMessagesTrim = 200,
    };

    [Fact]
    public void MemoLinkCache_RecordAndGet_ReturnsMemoName()
    {
        var cache = new MemoLinkCache(new MemoryCache(new MemoryCacheOptions()), TgConfig());
        cache.Record(100, 5, "memos/1");

        Assert.True(cache.TryGetMemoName(100, 5, out var name));
        Assert.Equal("memos/1", name);
    }

    [Fact]
    public void MemoLinkCache_DifferentMessageId_DoesNotMatch()
    {
        var cache = new MemoLinkCache(new MemoryCache(new MemoryCacheOptions()), TgConfig());
        cache.Record(100, 5, "memos/1");

        Assert.False(cache.TryGetMemoName(100, 6, out _));
        Assert.False(cache.TryGetMemoName(200, 5, out _));
    }

    private static (MessageHandler Handler, RecordingHandler MemosStub, MemoLinkCache LinkCache, string DataFile) BuildHandler()
    {
        var memosStub = new RecordingHandler(MemoResponse);
        var memosClient = new MemosClient("http://localhost:5230", new HttpClient(memosStub), NullLogger<MemosClient>.Instance);

        var memoService = new MemogramService(
            memosClient,
            new MemogramConfig { ServerAddr = "http://localhost:5230", MediaCacheTtl = TimeSpan.FromSeconds(30) },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemogramService>.Instance);

        var dataFile = Path.Combine(Path.GetTempPath(), $"memogram-link-test-{Guid.NewGuid():N}.txt");
        var store = new UserStoreService(new LocalStorageConfig { Filename = dataFile }, NullLogger<UserStoreService>.Instance);
        store.SetUserAccessTokenAsync(42, "access-token").GetAwaiter().GetResult();

        var linkCache = new MemoLinkCache(new MemoryCache(new MemoryCacheOptions()), TgConfig());
        var handler = new MessageHandler(
            store,
            new MyTelegramBotClient(new TelegramBotClientOptions("123:token"), new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))),
            memoService,
            new TelegramConfig { BotToken = "123:token", SearchReplyMessagesTrim = 200 },
            new FileExtensionMimeTypeDetector(),
            linkCache,
            NullLogger<MessageHandler>.Instance);

        return (handler, memosStub, linkCache, dataFile);
    }

    private static Message EditMessage(int messageId, long chatId, string text, long userId = 42)
        => new()
        {
            Id = messageId,
            Chat = new Chat { Id = chatId },
            From = new User { Id = userId, Username = "user" },
            Text = text,
        };

    [Fact]
    public async Task HandleEditedAsync_WithLink_UpdatesMemoContent()
    {
        var (handler, memosStub, linkCache, dataFile) = BuildHandler();
        try
        {
            linkCache.Record(100, 5, "memos/1");

            await handler.HandleEditedAsync(EditMessage(5, 100, "new content"), CancellationToken.None);

            Assert.NotNull(memosStub.LastPatchBody);
            using var doc = JsonDocument.Parse(memosStub.LastPatchBody!);
            Assert.Equal("new content", doc.RootElement.GetProperty("content").GetString());
            Assert.Equal("PRIVATE", doc.RootElement.GetProperty("visibility").GetString());
        }
        finally
        {
            if (File.Exists(dataFile))
                File.Delete(dataFile);
        }
    }

    [Fact]
    public async Task HandleEditedAsync_WithoutLink_DoesNothing()
    {
        var (handler, memosStub, linkCache, dataFile) = BuildHandler();
        try
        {
            await handler.HandleEditedAsync(EditMessage(999, 100, "new content"), CancellationToken.None);

            Assert.Equal(0, memosStub.RequestCount);
        }
        finally
        {
            if (File.Exists(dataFile))
                File.Delete(dataFile);
        }
    }
}
