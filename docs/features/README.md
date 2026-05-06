# Features

Each feature is a thin vertical slice with tests + self-review. Pattern: `F<N>-<slug>.md`.

Workflow per feature:
1. Write the feature doc (goal, AC, scope, manual test plan).
2. Tests first (unit + integration as appropriate).
3. Implementation.
4. Self-review via `code-reviewer` agent.
5. Small focused commit referencing the feature doc.
6. Update `../backlog.md` w/ anything discovered out-of-scope.

## Active / planned

| ID | Title | Status |
|----|-------|--------|
| F1 | Default CaseType + base LOB tree + demo Tenant seed | planned |
| F2 | Tenant context middleware + RLS migration enable | planned |
| F3 | Kafka outbox relay (background service) | planned |
| F4 | `POST /api/cases` intake endpoint (outbox publish, 202 + receiptId) | planned |
| F5 | `CaseIntakeConsumer` (Kafka → Case write + audit + notifications) | planned |
| F6 | `GET /api/cases/intake/{receiptId}` status endpoint | planned |
| F7 | Web intake form (JSON Schema → Zod → RHF, shadcn rendering) | planned |
| F8 | End-to-end smoke test (Playwright through full Aspire stack) | planned |

After F1–F8, the canonical create-case flow is provable. Remaining domain Qs (SLA/KPIs/dashboards/exports) get layered as additional features.
