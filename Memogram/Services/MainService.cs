using Memogram.Services.Memos;
using Memogram.Services.Telegram;
using Memogram.Services.Telegram.Handlers;
using Memogram.Services.Telegram.Handlers.Commands;
using Memogram.Services.UserStore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Memogram.Services;

public class MainService
{
    private readonly UserStoreService _storeService;
    private readonly TelegramService _tgService;
    private readonly MemogramService _memoService;
    private readonly MessageHandler _messageHandler;
    private readonly CallbackQueryHandler _callbackQueryHandler;

    private readonly ILogger<MainService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public MainService(UserStoreService storeService, TelegramService tgService, MemogramService memoService,
        MessageHandler messageHandler, CallbackQueryHandler callbackQueryHandler,
        ILogger<MainService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _tgService = tgService;
        _memoService = memoService;
        _storeService = storeService;
        _messageHandler = messageHandler;
        _callbackQueryHandler = callbackQueryHandler;
    }

    public async Task Start(CancellationToken ct = default)
    {
        _logger.LogInformation("Memogram starting...");

        await _storeService.InitializeAsync(ct);
        await _memoService.InitializeAsync(ct);

        _logger.LogInformation("Instance profile: {Profile}", JsonSerializer.Serialize(_memoService.InstanceProfile));

        await _tgService
            .Start([ 
                    new CmdStartHandler(_memoService, _tgService, _storeService, _loggerFactory.CreateLogger<CmdStartHandler>()), //TODO: baaad
                    new CmdSearchHandler(_memoService, _tgService, _storeService, _loggerFactory.CreateLogger<CmdSearchHandler>())
                ],
                _messageHandler, _callbackQueryHandler,
                ct);

        await Task.Delay(Timeout.Infinite, ct);
    }
}
