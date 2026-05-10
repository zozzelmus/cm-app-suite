using System.Text.Json.Nodes;

namespace Conduct.Domain.Cases.Intake;

// HTTP wire shape for POST /api/cases. Distinct from CreateCaseCommand (the Kafka envelope)
// because this carries raw JSON for `data` / party `data` so we can re-serialize cleanly
// after schema validation.
public sealed record IntakeRequest
{
    public required string CaseTypeKey { get; init; }
    // Nullable on the wire so the SPA can omit it — the API's ICaseRoutingService resolves
    // the initial LOB. Tests and internal callers may still set it explicitly to pin a LOB.
    public string? LobShortCode { get; init; }
    public required string Title { get; init; }
    public required JsonNode Data { get; init; }
    public JsonNode? ExternalRefs { get; init; }
    public IntakeReporterRequest? Reporter { get; init; }
    public IntakePartyRequest[] Subjects { get; init; } = Array.Empty<IntakePartyRequest>();
    public IntakePartyRequest[] Witnesses { get; init; } = Array.Empty<IntakePartyRequest>();
    public DateTimeOffset? OccurredAt { get; init; }
}

public sealed class IntakeReporterRequest
{
    public bool IsAnonymous { get; init; }
    public string? DisplayName { get; init; }
    public string? IdentityKind { get; init; }
    public string? EmployeeId { get; init; }
    public string? CustomerId { get; init; }
    public string? VendorId { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public JsonNode? Data { get; init; }
}

public sealed class IntakePartyRequest
{
    public required string IdentityKind { get; init; }
    public string? DisplayName { get; init; }
    public string? EmployeeId { get; init; }
    public string? CustomerId { get; init; }
    public string? VendorId { get; init; }
    public string? ContactEmail { get; init; }
    public JsonNode? Data { get; init; }
}

public sealed record IntakeAcceptedResponse(Guid ReceiptId, string StatusUrl);

// Stable machine-readable Code (e.g. "case_type_not_found") + human-readable Error message.
// Web client (F7) keys i18n / UX state on Code; humans read Error.
public sealed record IntakeErrorResponse(
    string Code,
    string Error,
    IReadOnlyList<IntakeFieldError>? FieldErrors = null);

public sealed record IntakeFieldError(string Path, string Message);
