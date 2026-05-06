namespace Conduct.Domain.Notifications;

// Contract every notification medium implements (SignalR, Email, SMS, ...).
// Per-channel impls live in Conduct.Infrastructure; consumers run as Kafka consumers
// reading per-channel topics like notifications.signalr.v1, notifications.email.v1.
public interface INotificationChannel
{
    string Name { get; }                                                   // "signalr" | "email" | "sms" | ...
    bool SupportsRecipient(NotificationRecipient recipient);               // e.g., email channel needs an email
    Task SendAsync(NotificationMessage message, CancellationToken ct);
}

public sealed record NotificationMessage(
    Guid Id,                                       // idempotency key (consumer dedupes on this)
    Guid TenantId,
    string EventType,                              // "case.created" | "case.transferred" | "task.assigned" | ...
    string Channel,                                // "signalr" | "email" | "sms"
    NotificationRecipient Recipient,
    string TemplateKey,                            // e.g., "case.created.email.html"
    IReadOnlyDictionary<string, object> Data,      // template variables
    DateTimeOffset OccurredAt);

public sealed record NotificationRecipient(
    Guid? PartyId,                                 // null only for ad-hoc system messages
    string? Email,                                 // optional contact info; channel decides what it needs
    string? Phone,
    string? UserId);                               // identity-store user id (for SignalR session lookup)
