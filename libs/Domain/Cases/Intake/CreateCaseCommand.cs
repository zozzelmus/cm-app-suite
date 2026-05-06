namespace Conduct.Domain.Cases.Intake;

// Async command envelope produced by F4 (POST /api/cases) and consumed by F5 (CaseIntakeConsumer).
// Topic: commands.case.create.v1. SchemaVersion is bumped on breaking change.
//
// Kept as POCOs (no behaviour) so it serializes cleanly to JSON for Kafka. Validation against
// the CaseType.FieldsSchema happens server-side in F4 BEFORE this command is written to the
// outbox; F5 re-validates on consume to catch schema drift between issue and process time.
public sealed class CreateCaseCommand
{
    public required Guid ReceiptId { get; init; }
    public required Guid TenantId { get; init; }
    public required string CaseTypeKey { get; init; }
    public required string LobShortCode { get; init; }
    public required string Title { get; init; }
    public required int SchemaVersion { get; init; }
    public required string DataJson { get; init; }                 // raw payload validated against CaseType.FieldsSchemaJson
    public string? ExternalRefsJson { get; init; }                 // surveillance ids, hotline refs, regulator ids
    public IntakeReporter? Reporter { get; init; }
    public IntakeParty[] Subjects { get; init; } = Array.Empty<IntakeParty>();
    public IntakeParty[] Witnesses { get; init; } = Array.Empty<IntakeParty>();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

// Reporter has anonymity stance; a known-employee Reporter populates IdentityKind=Employee + EmployeeId,
// an anonymous web-form Reporter populates IsAnonymous=true and may carry only a contact email.
public sealed class IntakeReporter
{
    public bool IsAnonymous { get; init; }
    public string? DisplayName { get; init; }
    public string? IdentityKind { get; init; }                     // Employee | Customer | Vendor | External | Anonymous
    public string? EmployeeId { get; init; }
    public string? CustomerId { get; init; }
    public string? VendorId { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public string DataJson { get; init; } = "{}";                  // role-specific data per CaseType.PartyDataSchemas[Reporter]
}

public sealed class IntakeParty
{
    public required string IdentityKind { get; init; }             // Employee | Customer | Vendor | External
    public string? DisplayName { get; init; }
    public string? EmployeeId { get; init; }
    public string? CustomerId { get; init; }
    public string? VendorId { get; init; }
    public string? ContactEmail { get; init; }
    public string DataJson { get; init; } = "{}";                  // role-specific data
}
