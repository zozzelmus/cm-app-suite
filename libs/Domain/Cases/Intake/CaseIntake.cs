using Conduct.Domain.Common;

namespace Conduct.Domain.Cases.Intake;

// Receipt + tracking row for an async intake. Created at POST /api/cases time;
// updated by CaseIntakeConsumer (F5) with the resulting CaseId / errors / completion.
// Polled by GET /api/cases/intake/{receiptId} (F6).
public class CaseIntake : TenantedEntity
{
    public IntakeStatus Status { get; set; } = IntakeStatus.Queued;
    public string CaseTypeKey { get; set; } = string.Empty;
    public string LobShortCode { get; set; } = string.Empty;
    public Guid? CaseId { get; set; }                       // populated by F5 on success
    public string? CaseNumber { get; set; }                 // mirrored from Case.CaseNumber once allocated
    public string? ErrorsJson { get; set; }                 // structured validation errors when Status=Failed
}

public enum IntakeStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
}
