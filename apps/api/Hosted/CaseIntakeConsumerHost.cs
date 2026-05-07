using System.Text;
using System.Text.Json;
using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure.Cases.Intake;
using Conduct.Infrastructure.Multitenancy;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conduct.Api.Hosted;

// Subscribes to commands.case.create.v1, deserialises CreateCaseCommand envelopes, and
// invokes IntakeProcessor inside a fresh DI scope per message. Tenant context is set via
// BeginScope from the message's tenant-id header.
//
// Manual offset commit: only commits AFTER the processor returns Processed=true. Failures
// don't commit → Kafka redelivers; the processor's idempotency keys on CaseIntake.Id avoid
// double-writes on redelivery.
public sealed class CaseIntakeConsumerHost(
    IServiceScopeFactory scopeFactory,
    IConsumer<string, string> consumer,
    ILogger<CaseIntakeConsumerHost> logger) : BackgroundService
{
    private const string Topic = IntakeService.CreateCaseTopicV1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(Topic);
        logger.LogInformation("CaseIntakeConsumerHost subscribed to {Topic}", Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Kafka consume error on {Topic} — pausing 1s", Topic);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                if (result is null) continue;

                var processed = await ProcessOneAsync(result, stoppingToken);
                if (processed)
                {
                    try { consumer.Commit(result); }
                    catch (KafkaException ex)
                    {
                        logger.LogWarning(ex, "Failed to commit offset for receipt {Receipt} — will reprocess", ExtractOutboxId(result));
                    }
                }
            }
        }
        finally
        {
            try { consumer.Close(); } catch { /* ignore on shutdown */ }
            logger.LogInformation("CaseIntakeConsumerHost stopped");
        }
    }

    private async Task<bool> ProcessOneAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var tenantHeader = TryGetHeader(result, "tenant-id");
        if (tenantHeader is null || !Guid.TryParse(tenantHeader, out var tenantId))
        {
            logger.LogError("Message on {Topic} missing or malformed tenant-id header — committing offset to skip", Topic);
            return true; // commit + skip; otherwise we'd loop forever
        }

        CreateCaseCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<CreateCaseCommand>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialise CreateCaseCommand from {Topic} — committing offset to skip", Topic);
            return true;
        }
        if (command is null) return true;

        using var scope = scopeFactory.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginScope(tenantId);

        var processor = scope.ServiceProvider.GetRequiredService<IntakeProcessor>();
        try
        {
            var outcome = await processor.ProcessAsync(command, ct);
            if (!outcome.Processed)
            {
                // Receipt was marked Failed inside the processor; commit the offset so we
                // don't loop on a permanently-bad message. (Redelivery isn't going to fix
                // CaseType-not-found etc.)
                logger.LogWarning("Receipt {Receipt} processing yielded outcome.Processed=false (reason={Reason}) — committing to skip",
                    command.ReceiptId, outcome.ErrorReason);
                return true;
            }
            return true;
        }
        catch (Exception ex)
        {
            // Transient (network/db) — DON'T commit; Kafka will redeliver.
            logger.LogError(ex, "Transient error processing receipt {Receipt} — leaving offset uncommitted for retry",
                command.ReceiptId);
            return false;
        }
    }

    private static string? TryGetHeader(ConsumeResult<string, string> result, string name)
    {
        if (result.Message.Headers is null) return null;
        foreach (var h in result.Message.Headers)
        {
            if (string.Equals(h.Key, name, StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetString(h.GetValueBytes());
            }
        }
        return null;
    }

    private static string ExtractOutboxId(ConsumeResult<string, string> result)
        => TryGetHeader(result, "outbox-id") ?? "?";
}
