using Memogram;
using Memogram.Clients.Memos;
using Memogram.Configs;
using Memogram.Services;
using Memogram.Services.Telegram;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeDetective;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration))
        .ConfigureAppConfiguration((context, config) =>
        {
            config.Sources.Clear();
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false);
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<MemogramConfig>(context.Configuration.GetSection("Memogram"));
            services.AddSingleton(sp =>
            {
                var memogram = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MemogramConfig>>().Value;
                memogram.Validate();
                return memogram;
            });
            services.Configure<TelegramConfig>(context.Configuration.GetSection("Telegram"));
            services.AddSingleton(sp =>
            {
                var telegram = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramConfig>>().Value;
                telegram.Validate();
                return telegram;
            });
            services.Configure<TelegramConfig>(context.Configuration.GetSection("LocalStorage"));
            services.AddSingleton(sp =>
            {
                var localStorage = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalStorageConfig>>().Value;
                localStorage.Validate();
                return localStorage;
            });

            services.AddSingleton(sp => new ContentInspectorBuilder
            {
                Definitions = MimeDetective.Definitions.DefaultDefinitions.All()
            }.Build());
            services.AddSingleton<UserStoreService>();
            services.AddSingleton<MemosClient>();
            services.AddSingleton<MemogramService>();
            services.AddSingleton<TelegramService>();
            services.AddSingleton<MainService>();
        })
        .Build();

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var service = host.Services.GetRequiredService<MainService>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    try
    {
        await service.Start(cts.Token);
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("Shutting down...");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Fatal error during startup");
        return 1;
    }

    return 0;
}
finally
{
    Log.CloseAndFlush();
}
