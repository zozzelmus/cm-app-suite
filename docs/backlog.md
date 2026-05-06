# Backlog — future features (post-MVP)

Items captured from grilling sessions and incremental discovery. Not prioritized yet; this is a parking lot, not a roadmap.

## Identity & RBAC
- HR system sync (Workday / SuccessFactors via SCIM 2.0 or REST)
- Keycloak admin event webhook → User.IsActive sync (near-real-time deprovisioning)
- Periodic full reconciliation job (Keycloak Admin API → User mirror)
- Multi-manager quorum: `NofM` and `SpecificUsers` quorum modes
- OOO / delegation chain for approval tasks (manager-delegate routing)
- Group hierarchy (currently flat)
- CaseType-scoped role assignments UI

## Cases
- Bulk operations (close many, transfer many, bulk reassign)
- Bulk import legacy cases (preserves CaseNumber, sets `IsImported=true`)
- Reopen workflow w/ explicit reason capture + privileged-only gate
- Threaded note replies (post-MVP)
- @mention support in notes (parser + `CaseNoteMention` table + routing rule)
- Case priority / severity field (CaseType-config'd levels)

## Tasks framework
- Beyond Cross-LOB Transfer Approval: closure approval, evidence sign-off, escalation review, RCA completion, training verification
- Task SLA + breach notifications
- Concurrent-transfer locking enforcement tests
- Same-person-both-sides approval audit annotation

## Evidence
- TUS resumable upload protocol for >1GB files
- ClamAV scanner container in Aspire local; Azure Defender for Storage in prod
- Per-tenant Azure Storage account / container provisioning
- Evidence redaction tooling (UI for annotating + producing redacted variant)
- Sensitivity-tier promotion UI w/ approval flow
- Retention policy admin UI

## Intake channels
- Email-to-case adapter (IMAP poller / Microsoft Graph subscription)
- Third-party hotline vendor integrations (NAVEX, EthicsPoint, Convercent)
- Surveillance feed adapters (Behavox/Theta Lake/NICE chat-comms; trade surveillance)
- Anonymous reporter follow-up channel (status-check token UI)

## Notifications
- SMS channel implementation (Azure Communication Services / Twilio)
- Microsoft Teams adapter
- User notification preferences UI
- Digest delivery (daily/weekly summaries vs immediate)
- Escalation rules (if not acknowledged in N minutes, fan out)

## SLA / KPIs / dashboards
- SLA model per CaseType (target time-in-state, total time-to-close)
- Time-bucket tracking (active investigation vs approval-wait vs in-flight transfer)
- KPI dashboards per LOB / per CaseType / per role
- Manager dashboard cut (cases in their LOB by stage + age)
- Auditor dashboard cut (audit-event-based timelines)

## Reporting / regulatory
- Regulator export bundle (case + evidence + audit log) in standard formats (e.g., FINRA Form U4 amendments, SAR)
- Per-jurisdiction reporting (UK FCA / US FINRA-SEC / EU / APAC variations)
- Audit log export tooling (filtered, signed)
- Schema migration tools for evolving CaseType field schemas (data backfill)

## Search / discovery
- Full-text search on Case / Notes (Postgres `tsvector` + GIN index for MVP; Azure AI Search later)
- Saved views per user
- Faceted filters on cases list (CaseType, LOB, Status, Date ranges, Custom fields)
- Vector-similarity search for case similarity (uses pgvector — already wired)

## Tenant + multi-tenant
- Per-tenant resolver (subdomain `bank-a.conduct.example` or JWT `tenant_id` claim)
- Per-tenant Keycloak realm vs single realm w/ tenant claim mapper
- Per-tenant Azure Storage container isolation
- Tenant onboarding workflow

## Schema governance
- Confluent Schema Registry integration for Kafka topics
- CaseType field schema versioning + automated data migration
- LOB schema versioning

## Admin UIs
- LOB tree CRUD (add/edit/move/archive LOBs, set ShortCode + ApprovalQuorum)
- CaseType CRUD + lifecycle editor + field schema editor
- Role + Permission management
- Group management
- Assignment management (who has what role on what scope)
- User admin (deprovisioning, manual overrides)

## Operational
- Outbox relay metrics + dead-letter handling
- Kafka topic provisioning automation
- Backup + restore runbook
- Disaster recovery RPO/RTO documentation
- Performance: closure-table for LOB hierarchy if adjacency-walk slows

## Tech debt — surfaced by F1 review
- **FK constraints** on cross-table Guid columns (Cases.CaseTypeId/OwnerLobId, CaseParty.CaseId/PartyId, Assignments.RoleId/SubjectId, User.PartyId, EmployeeProfile.PartyId, GroupMembership.GroupId/UserId, CustomerProfile.AccountManagerEmployeePartyId, etc.). Currently absent so migrations stay flexible while domain stabilizes; revisit before prod.
- **PostgresFixture cleanup** — drop test databases on dispose (not just stop container) to avoid leaks if container is ever reused (e.g. `WithReuse()` switch).
- **PostgresFixture SQL** — db-name interpolation is regex-bounded by `Guid:N` but the pattern looks injection-shaped; tighten with explicit guard or use a parameterized identifier helper.
