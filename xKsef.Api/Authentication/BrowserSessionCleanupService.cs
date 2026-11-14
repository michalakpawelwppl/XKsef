using Microsoft.Extensions.Options;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.Authentication;

public sealed class BrowserSessionCleanupOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
    public int BatchSize { get; set; } = 1000;
    public int MaxBatches { get; set; } = 5;
}

public sealed class BrowserSessionCleanupService(SqlBrowserTicketStore store, IOptions<BrowserSessionCleanupOptions> options,
    TimeProvider clock, ILogger<BrowserSessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        using var timer = new PeriodicTimer(settings.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                for (int i = 0; i < settings.MaxBatches; i++)
                    if (await store.DeleteExpiredAsync(settings.BatchSize, stoppingToken) < settings.BatchSize) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Never include SQL, tickets or session identifiers in operational logs.
                logger.LogWarning("Browser session cleanup failed ({FailureType}). Next run remains scheduled.", error.GetType().Name);
            }
        }
    }
}
