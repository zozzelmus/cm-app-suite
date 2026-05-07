using System.Text.Json;
using System.Text.Json.Nodes;
using Conduct.Domain.Audit;
using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure.Multitenancy;
using Conduct.Infrastructure.Outbox;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Infrastructure.Cases.Intake;

public sealed record IntakeOutcome(bool IsAccepted, Guid? ReceiptId, IntakeError? Error);

public enum IntakeErrorKind { CaseTypeNotFound, LobNotFound, ValidationFailed, TenantUnknown }

public sealed record IntakeError(
    IntakeErrorKind Kind,
    string Code,
    string Message,
    IReadOnlyList<IntakeFieldError>? FieldErrors = null);

// Validates an IntakeRequest against the resolved CaseType + per-role party schemas, writes
// the outbox row + CaseIntake receipt + IntakeAccepted AuditEvent in a single transaction,
// and returns the receipt id. F3's relay picks up the outbox row and publishes to Kafka;
// F5's consumer finalizes the case. The HTTP handler surfaces 202 + receiptId on success.
public sealed class IntakeService(
    ConductDbContext db,
    ITenantContext tenantContext)
{
    public const string CreateCaseTopicV1 = "commands.case.create.v1";

    private static readonly JsonSerializerOptions s_jsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<IntakeOutcome> SubmitAsync(IntakeRequest request, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId;
        if (tenantId is null)
        {
            return new IntakeOutcome(false, null, new IntakeError(
                IntakeErrorKind.TenantUnknown, "tenant_unknown",
                "No tenant context resolved for request"));
        }

        var caseType = await db.CaseTypes.AsNoTracking().SingleOrDefaultAsync(
            x => x.Key == request.CaseTypeKey && x.IsActive, ct);
        if (caseType is null)
        {
            return new IntakeOutcome(false, null, new IntakeError(
                IntakeErrorKind.CaseTypeNotFound, "case_type_not_found",
                $"Unknown CaseType '{request.CaseTypeKey}'"));
        }

        var lob = await db.Lobs.AsNoTracking().SingleOrDefaultAsync(
            x => x.ShortCode == request.LobShortCode, ct);
        if (lob is null)
        {
            return new IntakeOutcome(false, null, new IntakeError(
                IntakeErrorKind.LobNotFound, "lob_not_found",
                $"Unknown LOB '{request.LobShortCode}'"));
        }

        // Title gate: regulator narratives, dashboards, list views all key on Title.
        // Empty title generates "untitled" rows that are operationally useless and the
        // user-persona review flagged this as a "non-starter" finding.
        var trimmedTitle = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            return new IntakeOutcome(false, null, new IntakeError(
                IntakeErrorKind.ValidationFailed, "title_required",
                "Title is required"));
        }
        if (trimmedTitle.Length > 512)
        {
            return new IntakeOutcome(false, null, new IntakeError(
                IntakeErrorKind.ValidationFailed, "title_too_long",
                "Title cannot exceed 512 characters"));
        }

        // Validate Case.Data against CaseType.FieldsSchemaJson, then validate every party's
        // role-specific Data payload against CaseType.PartyDataSchemasJson[role].
        var fieldErrors = new List<IntakeFieldError>();
        ValidateAgainstSchema(caseType.FieldsSchemaJson, request.Data, "data", fieldErrors);

        var partySchemas = ParsePartyDataSchemas(caseType.PartyDataSchemasJson);
        ValidateParty(request.Reporter, "reporter", "Reporter", partySchemas, fieldErrors);
        for (int i = 0; i < request.Subjects.Length; i++)
        {
            ValidateParty(request.Subjects[i], $"subjects[{i}]", "Subject", partySchemas, fieldErrors);
        }
        for (int i = 0; i < request.Witnesses.Length; i++)
        {
            ValidateParty(request.Witnesses[i], $"witnesses[{i}]", "Witness", partySchemas, fieldErrors);
        }

        if (fieldErrors.Count > 0)
        {
            return new IntakeOutcome(false, null, new IntakeError(
                IntakeErrorKind.ValidationFailed, "validation_failed",
                "Payload failed schema validation", fieldErrors));
        }

        // Construct entities INSIDE strategy.ExecuteAsync — Aspire's NpgsqlRetryingExecutionStrategy
        // can re-run the lambda. Building outside meant a retry would re-attach already-tracked
        // entities and either throw or double-write. Building inside makes the operation idempotent
        // at the strategy level (each retry rebuilds + reattaches fresh tracked entities).
        Guid? acceptedReceiptId = null;
        var schemaVersion = caseType.SchemaVersion;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async cancellationToken =>
        {
            db.ChangeTracker.Clear();

            var receiptId = Guid.NewGuid();
            var command = BuildCommand(receiptId, tenantId.Value, request, schemaVersion);
            var commandJson = JsonSerializer.Serialize(command, s_jsonOpts);

            db.CaseIntakes.Add(new CaseIntake
            {
                Id = receiptId,
                TenantId = tenantId.Value,
                Status = IntakeStatus.Queued,
                CaseTypeKey = request.CaseTypeKey,
                LobShortCode = request.LobShortCode,
            });

            db.Outbox.Add(new OutboxMessage
            {
                TenantId = tenantId.Value,
                Topic = CreateCaseTopicV1,
                Key = receiptId.ToString(),
                PayloadJson = commandJson,
            });

            db.AuditEvents.Add(new AuditEvent
            {
                TenantId = tenantId.Value,
                Actor = "system:intake",
                EntityType = nameof(CaseIntake),
                EntityId = receiptId,
                Action = "IntakeAccepted",
                ChangeSetJson = "{}",
                ContextJson = JsonSerializer.Serialize(new
                {
                    caseTypeKey = request.CaseTypeKey,
                    lobShortCode = request.LobShortCode,
                    schemaVersion,
                }, s_jsonOpts),
            });

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            acceptedReceiptId = receiptId;
        }, ct);

        return new IntakeOutcome(true, acceptedReceiptId, null);
    }

    // -------- schema helpers --------

    private static void ValidateAgainstSchema(string schemaJson, JsonNode? data, string pathPrefix, List<IntakeFieldError> sink)
    {
        if (string.IsNullOrWhiteSpace(schemaJson) || schemaJson == "{}") return; // no schema → nothing to validate
        var schema = JsonSchema.FromText(schemaJson);
        var element = JsonSerializer.SerializeToElement(data ?? JsonValue.Create(string.Empty));
        var result = schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return;

        var raw = new List<IntakeFieldError>();
        Walk(result, raw);
        foreach (var e in raw)
        {
            var p = string.IsNullOrEmpty(e.Path) || e.Path == "(root)" ? pathPrefix : $"{pathPrefix}.{e.Path.TrimStart('/')}";
            sink.Add(new IntakeFieldError(p, e.Message));
        }
    }

    private static void Walk(EvaluationResults node, List<IntakeFieldError> sink)
    {
        if (!node.IsValid && node.Errors is { Count: > 0 } errors)
        {
            var path = node.InstanceLocation.ToString();
            foreach (var (_, message) in errors)
            {
                sink.Add(new IntakeFieldError(string.IsNullOrEmpty(path) ? "(root)" : path, message));
            }
        }
        if (node.Details is { } details)
        {
            foreach (var child in details) Walk(child, sink);
        }
    }

    private static IReadOnlyDictionary<string, string> ParsePartyDataSchemas(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new Dictionary<string, string>();
        var node = JsonNode.Parse(json) as JsonObject;
        if (node is null) return new Dictionary<string, string>();
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in node)
        {
            dict[kvp.Key] = kvp.Value?.ToJsonString(s_jsonOpts) ?? "{}";
        }
        return dict;
    }

    private static void ValidateParty(IntakePartyRequest? party, string path, string roleKey,
        IReadOnlyDictionary<string, string> partySchemas, List<IntakeFieldError> sink)
    {
        if (party is null) return;
        if (!partySchemas.TryGetValue(roleKey, out var schemaJson)) return; // no role schema → nothing to validate
        ValidateAgainstSchema(schemaJson, party.Data, $"{path}.data", sink);
    }

    private static void ValidateParty(IntakeReporterRequest? reporter, string path, string roleKey,
        IReadOnlyDictionary<string, string> partySchemas, List<IntakeFieldError> sink)
    {
        if (reporter is null) return;
        if (!partySchemas.TryGetValue(roleKey, out var schemaJson)) return;
        ValidateAgainstSchema(schemaJson, reporter.Data, $"{path}.data", sink);
    }

    private static CreateCaseCommand BuildCommand(Guid receiptId, Guid tenantId, IntakeRequest req, int schemaVersion)
    {
        return new CreateCaseCommand
        {
            ReceiptId = receiptId,
            TenantId = tenantId,
            CaseTypeKey = req.CaseTypeKey,
            LobShortCode = req.LobShortCode,
            Title = req.Title,
            SchemaVersion = schemaVersion,
            DataJson = req.Data.ToJsonString(s_jsonOpts),
            ExternalRefsJson = req.ExternalRefs?.ToJsonString(s_jsonOpts),
            Reporter = req.Reporter is null ? null : new IntakeReporter
            {
                IsAnonymous = req.Reporter.IsAnonymous,
                DisplayName = req.Reporter.DisplayName,
                IdentityKind = req.Reporter.IdentityKind,
                EmployeeId = req.Reporter.EmployeeId,
                CustomerId = req.Reporter.CustomerId,
                VendorId = req.Reporter.VendorId,
                ContactEmail = req.Reporter.ContactEmail,
                ContactPhone = req.Reporter.ContactPhone,
                DataJson = req.Reporter.Data?.ToJsonString(s_jsonOpts) ?? "{}",
            },
            Subjects = Array.ConvertAll(req.Subjects, p => new IntakeParty
            {
                IdentityKind = p.IdentityKind,
                DisplayName = p.DisplayName,
                EmployeeId = p.EmployeeId,
                CustomerId = p.CustomerId,
                VendorId = p.VendorId,
                ContactEmail = p.ContactEmail,
                DataJson = p.Data?.ToJsonString(s_jsonOpts) ?? "{}",
            }),
            Witnesses = Array.ConvertAll(req.Witnesses, p => new IntakeParty
            {
                IdentityKind = p.IdentityKind,
                DisplayName = p.DisplayName,
                EmployeeId = p.EmployeeId,
                CustomerId = p.CustomerId,
                VendorId = p.VendorId,
                ContactEmail = p.ContactEmail,
                DataJson = p.Data?.ToJsonString(s_jsonOpts) ?? "{}",
            }),
            OccurredAt = req.OccurredAt ?? DateTimeOffset.UtcNow,
        };
    }
}
