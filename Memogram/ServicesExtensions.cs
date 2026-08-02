using Memogram.Clients.Telegram;
using Memogram.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using Telegram.Bot;

namespace Memogram;

internal static class ServicesExtensions
{
    public static void ConfigureAndValidate<T>(this IServiceCollection services, IConfiguration configuration)
        where T : class, IValidableConfig
    {
        services.Configure<T>(configuration.GetSection(T.SectionName));
        services.AddSingleton(sp =>
        {
            var memogram = sp.GetRequiredService<IOptions<T>>().Value;
            memogram.Validate();
            return memogram;
        });
    }

    public static void AddTelegramClient(this IServiceCollection services, string name)
    {
        services
            .AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler((sp) =>
            {
                var telegramConfig = sp.GetService<IOptions<TelegramConfig>>()?.Value;
                ArgumentNullException.ThrowIfNull(telegramConfig);

                if (string.IsNullOrWhiteSpace(telegramConfig.Proxy))
                    return new HttpClientHandler();
                var proxy = new WebProxy(telegramConfig.Proxy);
                return new HttpClientHandler { Proxy = proxy, UseProxy = true };
            })
            .RemoveAllLoggers()
            .AddTypedClient<IMyTelegramBotClient>((httpClient, sp) =>
            {
                var telegramConfig = sp.GetService<IOptions<TelegramConfig>>()?.Value;
                ArgumentNullException.ThrowIfNull(telegramConfig);

                var options = !string.IsNullOrEmpty(telegramConfig.BotProxyAddr)
                        ? new TelegramBotClientOptions(telegramConfig.BotToken, telegramConfig.BotProxyAddr)
                        : new TelegramBotClientOptions(telegramConfig.BotToken);
                options.RetryCount = 3;

                return new MyTelegramBotClient(options, httpClient);
            });
    }
}
