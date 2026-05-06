using Conduct.Domain.Notifications;

namespace Conduct.Infrastructure.Notifications;

// Stub — wires up to a SignalR hub (added in apps/api once hub is defined).
// Reads notifications.signalr.v1 topic; pushes to the recipient's authenticated session(s) by UserId.
public sealed class SignalRNotificationChannel : INotificationChannel
{
    public string Name => "signalr";

    public bool SupportsRecipient(NotificationRecipient recipient)
        => !string.IsNullOrEmpty(recipient.UserId);

    public Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        // TODO: resolve IHubContext<ConductHub> and push to user group keyed by recipient.UserId
        // Skipped here — real impl lands once hub is defined alongside the Kafka consumer service.
        return Task.CompletedTask;
    }
}
