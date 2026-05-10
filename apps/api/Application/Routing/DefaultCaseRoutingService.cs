using Conduct.Infrastructure.Seed;

namespace Conduct.Api.Application.Routing;

// V1 implementation: send every case to Speak-Up Intake. The SUI triager moves cases
// to the right investigation LOB via the (forthcoming) transfer-approval task workflow.
//
// Future shape (sketched, not built):
//   1. If RoutingContext.Request carries an explicit non-null LobShortCode (admin tool /
//      bulk import path), respect it and short-circuit. Audit Source = "client:override".
//   2. Look up CaseType.DefaultLobShortCode — Non-reporter channels (audit-found,
//      regulator-referral) can carve themselves out of the SUI funnel via this column.
//   3. Evaluate any rules attached to the CaseType (JSON rule engine). Could fire on
//      reporter dept, severity, occurredAt time-of-day, etc.
//   4. Hand off to an ML classifier (e.g., GPT-class model) given the summary + parties;
//      classifier returns a candidate LOB + confidence. Below a threshold → fall through.
//   5. Final fallback: SUI.
//
// Each layer attaches a `Source` so RegEx of the audit log answers "why did THIS case
// end up in THAT LOB" without reading code.
public sealed class DefaultCaseRoutingService : ICaseRoutingService
{
    public Task<RoutingDecision> ResolveAsync(RoutingContext context, CancellationToken ct)
    {
        return Task.FromResult(new RoutingDecision(
            LobShortCode: SeedConstants.LobSpeakUpIntake,
            Source: "default:speak-up-intake"));
    }
}
