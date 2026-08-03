using Memogram.Services.Memos;
using Memogram.Services.Telegram;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Memogram.Services;

public class MainService(UserStoreService _storeService, TelegramService _tgService, MemogramService _memoService,
        ILogger<MainService> _logger)
    : BackgroundService
{
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
