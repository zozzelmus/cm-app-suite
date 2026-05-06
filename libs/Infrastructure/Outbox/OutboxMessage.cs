namespace Conduct.Infrastructure.Outbox;

// Transactional outbox row. Domain handlers write to this in the same DB tx as their state changes;
// a background relay polls and publishes to Kafka, then marks PublishedAt.
// Idempotency on the consumer side via OutboxMessage.Id (used as Kafka message key/headers).
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Topic { get; set; } = string.Empty;        // e.g., "events.case.created.v1"
    public string? Key { get; set; }                         // Kafka partition key (e.g., CaseId)
    public string PayloadJson { get; set; } = string.Empty;  // serialized event/command
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }         // null = pending; non-null = relay confirmed publish
    public int AttemptCount { get; set; }                    // increments on each publish attempt
    public string? LastError { get; set; }                   // last failure for debugging
}
