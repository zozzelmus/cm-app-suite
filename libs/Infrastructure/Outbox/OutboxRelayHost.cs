using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conduct.Infrastructure.Outbox;

// BackgroundService wrapper. Singleton — must NOT inject scoped services directly.
// Each tick opens a fresh service scope to resolve OutboxRelay (scoped) which has
// the request-scoped DbContext. Loop backs off based on whether the last tick found work.
public sealed class OutboxRelayHost(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelayHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        logger.LogInformation(
            "OutboxRelayHost starting (batch={Batch}, busy={Busy}, idle={Idle})",
            opts.BatchSize, opts.BusyDelay, opts.IdleDelay);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool didWork = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
                didWork = await relay.TickOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxRelayHost tick threw — sleeping idleDelay before retry");
            }

            try
            {
                await Task.Delay(didWork ? opts.BusyDelay : opts.IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("OutboxRelayHost stopped");
    }
}
