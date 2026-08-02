using Memogram.Clients.Memos;
using Memogram.Clients.Telegram;
using Memogram.Configs;
using Memogram.Services.Health.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Memogram.Services.Health;

public class HealthCheckService(IMyTelegramBotClient _telegramBotClient,
        MemosClient _memosClient,
        ILogger<HealthCheckService> _logger)
{
    private const string StatusOk = "ok";
    private const string StatusError = "error";

    public async Task<CheckResult> CheckAsync(CancellationToken ct)
    {
        var telegramTask = CheckTelegramAsync(ct);
        var memosTask = CheckMemosAsync(ct);
        await Task.WhenAll(telegramTask, memosTask);

        return new CheckResult 
        { 
            IsHealthy = telegramTask.Result && memosTask.Result,
            Checks = 
            [
                KeyValuePair.Create("telegram", telegramTask.Result ? StatusOk : StatusError),
                KeyValuePair.Create("memos", memosTask.Result ? StatusOk : StatusError)
            ]
        };
    }

    public async Task<bool> CheckTelegramAsync(CancellationToken ct)
    {
        try
        {
            await _telegramBotClient.TestApi(ct);
            _logger.LogDebug("Telegram health check passed ({BaseUrl})", _telegramBotClient.Options.BaseUrl);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Telegram health check timed out");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram health check failed");
            return false;
        }
    }

    public async Task<bool> CheckMemosAsync(CancellationToken ct)
    {
        try
        {
            var profile = await _memosClient.GetInstanceProfileAsync(ct);
            _logger.LogDebug("Memos health check passed ({BaseUrl})", profile.InstanceUrl);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Memos health check timed out");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memos health check failed");
            return false;
        }
    }
}
