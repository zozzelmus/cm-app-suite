using Conduct.Domain.Cases.Intake;

namespace Conduct.Api.Application.Routing;

// Decides which LOB a brand-new case should land in. Sits in the API's application layer
// so future strategies — JSON rule sets on CaseType, ML/AI classifiers, reporter-identity
// heuristics — can plug in without touching the intake pipeline or domain.
//
// Today: every reporter-channel intake routes to SUI (Speak-Up Intake). Non-reporter
// channels and per-CaseType overrides will be added as ResolveAsync gains inputs.
public interface ICaseRoutingService
{
    Task<RoutingDecision> ResolveAsync(RoutingContext context, CancellationToken ct);
}

public sealed record RoutingContext(
    Guid TenantId,
    string CaseTypeKey,
    IntakeRequest Request);

public sealed record RoutingDecision(
    string LobShortCode,
    // Free-form audit trail of how the decision was made. Persisted on the AuditEvent so
    // a regulator can see "why did this land in SUI?" later. Examples:
    //   "default:speak-up-intake"           — v1, hardcoded
    //   "case-type-default:HR-ER"           — CaseType.DefaultLobShortCode hit
    //   "rule:expenses-policy→CMP"          — JSON rule engine fired
    //   "ai:gpt-4.1@2026-05-10/score=0.91"  — ML classifier
    string Source);
