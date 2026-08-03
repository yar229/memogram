using Memogram.Services.Memos;
using Memogram.Services.Telegram;
using Memogram.Services.Telegram.Handlers;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Memogram.Services;

public class MainService : BackgroundService
{
    private readonly UserStoreService _storeService;
    private readonly TelegramService _tgService;
    private readonly MemogramService _memoService;
    private readonly MessageHandler _messageHandler;
    private readonly CallbackQueryHandler _callbackQueryHandler;

    private readonly ILogger<MainService> _logger;

    public MainService(UserStoreService storeService, TelegramService tgService, MemogramService memoService,
        MessageHandler messageHandler, CallbackQueryHandler callbackQueryHandler,
        ILogger<MainService> logger)
    {
        _logger = logger;
        _tgService = tgService;
        _memoService = memoService;
        _storeService = storeService;
        _messageHandler = messageHandler;
        _callbackQueryHandler = callbackQueryHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Memogram starting...");

        await _storeService.InitializeAsync(stoppingToken);
        await _memoService.InitializeAsync(stoppingToken);

        _logger.LogInformation("Instance profile: {Profile}", JsonSerializer.Serialize(_memoService.InstanceProfile));

        await _tgService.Start(stoppingToken);

        _logger.LogInformation("Shutting down...");
    }
}
