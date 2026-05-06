using Conduct.Domain.Notifications;

namespace Conduct.Infrastructure.Notifications;

// Stub — wires up to SMTP / Microsoft Graph / SendGrid via config in real impl.
// Reads notifications.email.v1 topic; renders Razor template; sends via configured provider.
public sealed class EmailNotificationChannel : INotificationChannel
{
    public string Name => "email";

    public bool SupportsRecipient(NotificationRecipient recipient)
        => !string.IsNullOrEmpty(recipient.Email);

    public Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        // TODO: render templated email, send via provider.
        return Task.CompletedTask;
    }
}
