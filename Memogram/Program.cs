using Memogram;
using Memogram.Clients.Memos;
using Memogram.Configs;
using Memogram.Services;
using Memogram.Services.Health;
using Memogram.Services.Memos;
using Memogram.Services.MimeTypeDetectors;
using Memogram.Services.Telegram;
using Memogram.Services.Telegram.Handlers;
using Memogram.Services.Telegram.Handlers.Commands;
using Memogram.Services.UserStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration));

    var webConfig = builder.Configuration.GetSection(WebConfig.SectionName).Get<WebConfig>();
    builder.WebHost.UseUrls($"{webConfig!.Address}:{webConfig.Port}");

    builder.Services.ConfigureAndValidate<MemogramConfig>(builder.Configuration);
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<MemosClient>();
    builder.Services.AddSingleton<MemogramService>();


    builder.Services.ConfigureAndValidate<TelegramConfig>(builder.Configuration);
    builder.Services.AddTelegramClient("telegram_bot_client");
    builder.Services.AddSingleton<ICmdHandler, CmdStartHandler>();
    builder.Services.AddSingleton<ICmdHandler, CmdSearchHandler>();
    builder.Services.AddSingleton<MessageHandler>();
    builder.Services.AddSingleton<CallbackQueryHandler>();
    builder.Services.AddSingleton<TelegramService>();
    builder.Services.AddSingleton<MemoLinkCache>();

    builder.Services.ConfigureAndValidate<LocalStorageConfig>(builder.Configuration);
    builder.Services.AddSingleton<UserStoreService>();

    builder.Services.ConfigureAndValidate<WebConfig>(builder.Configuration);
    builder.Services.AddSingleton<HealthCheckService>();

    builder.Services.AddSingleton<IMimeTypeDetector, FileExtensionMimeTypeDetector>();

    builder.Services.AddHostedService<MainService>();

    builder.Services.AddControllers();
    

    var app = builder.Build();
    app.MapControllers();
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
