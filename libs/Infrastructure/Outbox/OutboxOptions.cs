namespace Conduct.Infrastructure.Outbox;

// Bound from configuration section "Outbox" — see appsettings.json.
public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 50;
    public TimeSpan BusyDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan IdleDelay { get; set; } = TimeSpan.FromSeconds(2);

    // Poison-row ceiling. After this many failed attempts, the row is excluded from polling
    // (LastError records the trail). Operators can manually inspect, increment, or move to
    // a dead-letter table out of band. Default 10.
    public int MaxAttempts { get; set; } = 10;
}
