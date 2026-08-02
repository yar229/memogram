using System.Net;
using System.Text;
using System.Text.Json;
using Memogram.Clients.Memos;
using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Xunit;

namespace Memogram.Tests;

public class HealthCheckTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private static ServiceProvider BuildServices(Func<HttpRequestMessage, HttpResponseMessage> telegram, Func<HttpRequestMessage, HttpResponseMessage> memos)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMyTelegramBotClient>(
            new MyTelegramBotClient(new TelegramBotClientOptions("123:token"), new HttpClient(new StubHandler(telegram))));
        services.AddSingleton(sp => new MemosClient(
            "http://localhost:5230",
            new HttpClient(new StubHandler(memos)),
            NullLogger<MemosClient>.Instance));
        return services.BuildServiceProvider();
    }

    private static Task<HttpResponseMessage> MemosOk(HttpRequestMessage _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"instanceUrl\":\"http://localhost:5230\",\"version\":\"0.30.0\",\"demo\":false,\"commit\":\"x\",\"needsSetup\":false,\"admin\":null}",
            Encoding.UTF8,
            "application/json"),
    });

    private static Task<HttpResponseMessage> TelegramOk(HttpRequestMessage _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"ok\":true,\"result\":{\"id\":123,\"is_bot\":true,\"first_name\":\"x\",\"username\":\"xbot\"}}",
            Encoding.UTF8,
            "application/json"),
    });

}
