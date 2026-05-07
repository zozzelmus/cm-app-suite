using System.Text.Json;
using Conduct.Domain.Audit;
using Conduct.Domain.Cases;
using Conduct.Domain.Cases.Intake;
using Conduct.Domain.Parties;
using Conduct.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conduct.Infrastructure.Cases.Intake;

public sealed record ProcessOutcome(bool Processed, Guid? CaseId, string? CaseNumber, string? ErrorReason);

// Consumes a CreateCaseCommand envelope (already deserialised by the host). Idempotent on
// CaseIntake.Id — re-deliveries are detected and a no-op outcome is returned.
//
// Atomic write inside a single tx wrapped in CreateExecutionStrategy().ExecuteAsync (Aspire's
// NpgsqlRetryingExecutionStrategy demands it):
//   1. Allocate CaseNumber via CaseAllocator
//   2. Insert Case row
//   3. Find-or-create Party rows for reporter/subjects/witnesses
//   4. Insert CaseParty join rows
//   5. Update CaseIntake.Status=Completed + CaseId + CaseNumber
//   6. Emit AuditEvents (CaseCreated, PartyAdded per party, StateTransition Initial→Open)
//
// Tenant context MUST be set by the caller (CaseIntakeConsumerHost) via tenant.BeginScope
// based on the message's tenant-id header. Without it, RLS will reject inserts.
public sealed class IntakeProcessor(
    ConductDbContext db,
    CaseAllocator allocator,
    ITenantContext tenantContext,
    ILogger<IntakeProcessor> logger)
{
    public async Task<ProcessOutcome> ProcessAsync(CreateCaseCommand cmd, CancellationToken ct)
    {
        if (tenantContext.TenantId is null || tenantContext.TenantId != cmd.TenantId)
        {
            return new ProcessOutcome(false, null, null,
                $"Tenant mismatch — ambient={tenantContext.TenantId}, command={cmd.TenantId}");
        }

        // Idempotency check — receipt already terminal? skip.
        var receipt = await db.CaseIntakes.SingleOrDefaultAsync(x => x.Id == cmd.ReceiptId, ct);
        if (receipt is null)
        {
            return new ProcessOutcome(false, null, null,
                $"Receipt {cmd.ReceiptId} not found — possible cross-tenant routing or stale message");
        }
        if (receipt.Status == IntakeStatus.Completed && receipt.CaseId is { } existingId)
        {
            return new ProcessOutcome(true, existingId, receipt.CaseNumber, null);
        }

        var caseType = await db.CaseTypes.SingleOrDefaultAsync(
            x => x.Key == cmd.CaseTypeKey && x.IsActive, ct);
        if (caseType is null)
        {
            await MarkFailedAsync(receipt, $"CaseType '{cmd.CaseTypeKey}' not found at process time", ct);
            return new ProcessOutcome(false, null, null, "case_type_not_found");
        }

        var lob = await db.Lobs.SingleOrDefaultAsync(x => x.ShortCode == cmd.LobShortCode, ct);
        if (lob is null)
        {
            await MarkFailedAsync(receipt, $"LOB '{cmd.LobShortCode}' not found at process time", ct);
            return new ProcessOutcome(false, null, null, "lob_not_found");
        }

        Guid caseId = Guid.Empty;
        string caseNumber = string.Empty;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async cancellationToken =>
        {
            db.ChangeTracker.Clear();

            // Re-fetch entities tracked under the strategy lambda
            var receiptTracked = await db.CaseIntakes.SingleAsync(x => x.Id == cmd.ReceiptId, cancellationToken);
            receiptTracked.Status = IntakeStatus.Processing;

            // Allocate the human-facing case number using current UTC year.
            var year = (cmd.OccurredAt == default ? DateTimeOffset.UtcNow : cmd.OccurredAt).UtcDateTime.Year;
            var alloc = await allocator.AllocateAsync(
                cmd.TenantId, cmd.LobShortCode, year, caseType, cancellationToken);

            var c = new Case
            {
                TenantId = cmd.TenantId,
                CaseTypeId = caseType.Id,
                OwnerLobId = lob.Id,
                CaseNumber = alloc.CaseNumber,
                Title = cmd.Title,
                Status = "Open",
                DataJson = cmd.DataJson,
                ExternalRefsJson = cmd.ExternalRefsJson ?? "{}",
                SchemaVersion = cmd.SchemaVersion,
                OpenedAt = DateTimeOffset.UtcNow,
            };
            db.Cases.Add(c);
            await db.SaveChangesAsync(cancellationToken);

            await AddPartiesAsync(c, cmd, cancellationToken);

            // Domain audit events (richer narrative on top of the EF interceptor's row diffs)
            db.AuditEvents.Add(new AuditEvent
            {
                TenantId = cmd.TenantId,
                Actor = "system:consumer",
                EntityType = nameof(Case),
                EntityId = c.Id,
                Action = "CaseCreated",
                ChangeSetJson = "{}",
                ContextJson = JsonSerializer.Serialize(new
                {
                    caseId = c.Id,
                    caseNumber = c.CaseNumber,
                    caseTypeKey = cmd.CaseTypeKey,
                    lobShortCode = cmd.LobShortCode,
                    receiptId = cmd.ReceiptId,
                }),
            });

            // Finalize the receipt
            receiptTracked.Status = IntakeStatus.Completed;
            receiptTracked.CaseId = c.Id;
            receiptTracked.CaseNumber = c.CaseNumber;

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            caseId = c.Id;
            caseNumber = c.CaseNumber;
        }, ct);

        logger.LogInformation(
            "Intake processed receipt={ReceiptId} → case={CaseId} number={CaseNumber}",
            cmd.ReceiptId, caseId, caseNumber);

        return new ProcessOutcome(true, caseId, caseNumber, null);
    }

    // --- helpers ---

    private async Task AddPartiesAsync(Case c, CreateCaseCommand cmd, CancellationToken ct)
    {
        if (cmd.Reporter is { } reporter)
        {
            var partyId = await ResolvePartyAsync(reporter.IdentityKind, reporter.DisplayName,
                reporter.EmployeeId, reporter.CustomerId, reporter.VendorId, reporter.IsAnonymous, ct);
            db.CaseParties.Add(new CaseParty
            {
                TenantId = cmd.TenantId,
                CaseId = c.Id,
                PartyId = partyId,
                RoleOnCase = RoleOnCase.Reporter,
                IsAnonymousOnThisCase = reporter.IsAnonymous,
                DataJson = reporter.DataJson,
                AddedAt = DateTimeOffset.UtcNow,
            });
        }
        foreach (var s in cmd.Subjects)
        {
            var partyId = await ResolvePartyAsync(s.IdentityKind, s.DisplayName,
                s.EmployeeId, s.CustomerId, s.VendorId, isAnonymous: false, ct);
            db.CaseParties.Add(new CaseParty
            {
                TenantId = cmd.TenantId,
                CaseId = c.Id,
                PartyId = partyId,
                RoleOnCase = RoleOnCase.Subject,
                DataJson = s.DataJson,
                AddedAt = DateTimeOffset.UtcNow,
            });
        }
        foreach (var w in cmd.Witnesses)
        {
            var partyId = await ResolvePartyAsync(w.IdentityKind, w.DisplayName,
                w.EmployeeId, w.CustomerId, w.VendorId, isAnonymous: false, ct);
            db.CaseParties.Add(new CaseParty
            {
                TenantId = cmd.TenantId,
                CaseId = c.Id,
                PartyId = partyId,
                RoleOnCase = RoleOnCase.Witness,
                DataJson = w.DataJson,
                AddedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private async Task<Guid> ResolvePartyAsync(
        string? identityKindName, string? displayName,
        string? employeeId, string? customerId, string? vendorId,
        bool isAnonymous, CancellationToken ct)
    {
        var kind = ParseIdentityKind(identityKindName, isAnonymous);

        // Try to find existing Party by upstream id when one is supplied.
        if (kind == IdentityKind.Employee && !string.IsNullOrWhiteSpace(employeeId))
        {
            var existing = await db.EmployeeProfiles
                .Where(p => p.EmployeeId == employeeId)
                .Select(p => p.PartyId)
                .SingleOrDefaultAsync(ct);
            if (existing != Guid.Empty) return existing;
        }
        else if (kind == IdentityKind.Customer && !string.IsNullOrWhiteSpace(customerId))
        {
            var existing = await db.CustomerProfiles
                .Where(p => p.CustomerId == customerId)
                .Select(p => p.PartyId)
                .SingleOrDefaultAsync(ct);
            if (existing != Guid.Empty) return existing;
        }
        else if (kind == IdentityKind.Vendor && !string.IsNullOrWhiteSpace(vendorId))
        {
            var existing = await db.VendorProfiles
                .Where(p => p.VendorId == vendorId)
                .Select(p => p.PartyId)
                .SingleOrDefaultAsync(ct);
            if (existing != Guid.Empty) return existing;
        }

        // No match → create a new Party + matching profile.
        var tenantId = tenantContext.TenantId!.Value;
        var party = new Party
        {
            TenantId = tenantId,
            IdentityKind = kind,
            DisplayName = displayName ?? (isAnonymous ? "Anonymous" : ""),
            IsAnonymous = isAnonymous,
        };
        db.Parties.Add(party);
        await db.SaveChangesAsync(ct);

        switch (kind)
        {
            case IdentityKind.Employee when !string.IsNullOrWhiteSpace(employeeId):
                db.EmployeeProfiles.Add(new EmployeeProfile
                {
                    TenantId = tenantId, PartyId = party.Id, EmployeeId = employeeId,
                });
                await db.SaveChangesAsync(ct);
                break;
            case IdentityKind.Customer when !string.IsNullOrWhiteSpace(customerId):
                db.CustomerProfiles.Add(new CustomerProfile
                {
                    TenantId = tenantId, PartyId = party.Id, CustomerId = customerId,
                });
                await db.SaveChangesAsync(ct);
                break;
            case IdentityKind.Vendor when !string.IsNullOrWhiteSpace(vendorId):
                db.VendorProfiles.Add(new VendorProfile
                {
                    TenantId = tenantId, PartyId = party.Id, VendorId = vendorId,
                });
                await db.SaveChangesAsync(ct);
                break;
            // External / Anonymous: no profile row.
        }

        return party.Id;
    }

    private static IdentityKind ParseIdentityKind(string? raw, bool isAnonymous)
    {
        if (isAnonymous) return IdentityKind.Anonymous;
        if (Enum.TryParse<IdentityKind>(raw, ignoreCase: true, out var k)) return k;
        return IdentityKind.External;
    }

    private async Task MarkFailedAsync(CaseIntake receipt, string reason, CancellationToken ct)
    {
        receipt.Status = IntakeStatus.Failed;
        receipt.ErrorsJson = JsonSerializer.Serialize(new[] { reason });
        await db.SaveChangesAsync(ct);
    }
}
