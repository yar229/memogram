using Memogram.Clients.Memos;
using Memogram.Clients.Telegram;
using Memogram.Configs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Memogram.Services.Health;

[ApiController]
[Route("api/health")]
public class HealthCheckController(WebConfig _config,
        HealthCheckService _healthCheckService,
        ILogger<HealthCheckController> _logger)
    : ControllerBase
{
    private const string StatusHealthy = "healthy";
    private const string StatusUnhealthy = "unhealthy";

    [HttpGet()]
    public async Task HandleAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.HealthCheckTimeoutSeconds));

        var checks = await _healthCheckService.CheckAsync(cts.Token);

        HttpContext.Response.StatusCode = checks.IsHealthy
            ? StatusCodes.Status200OK 
            : StatusCodes.Status503ServiceUnavailable;
        await HttpContext.Response.WriteAsJsonAsync(new 
        { 
            status = checks.IsHealthy ? StatusHealthy : StatusUnhealthy, 
            checks 
        }, HttpContext.RequestAborted);
    }
}
