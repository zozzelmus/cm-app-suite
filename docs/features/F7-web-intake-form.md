# F7 — Web intake form (JSON Schema → Zod → RHF, shadcn rendering)

## Goal
Build the web intake form for creating a new conduct case. The form is **schema-driven**: it reads a JSON Schema (the seeded Default CaseType's `FieldsSchemaJson`), converts it to a Zod schema at app load, renders inputs with React Hook Form + shadcn primitives, and submits to a stubbed `/api/cases` endpoint (real endpoint lands in F4).

Memory references (must read before starting):
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_custom_fields.md` — locked schema/UI strategy
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_stack.md` — frontend libs already chosen
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_messaging_intake.md` — intake API contract (what shape to POST)
- `CLAUDE.md` — project conventions
- `docs/features/F1-default-casetype-and-tenant-seed.md` — sample seeded `FieldsSchemaJson` is in `libs/Infrastructure/Seed/Seeder.cs`; copy that as a fixture for now
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/feedback_delivery_discipline.md` — TDD + self-review

## Acceptance criteria
1. **AC1 — `apps/web/src/features/intake/SchemaForm.tsx`** — generic schema-driven form component:
   - Props: `{ schema: JSONSchema7, defaultValues?, onSubmit: (data) => Promise<void> }`.
   - At construction, converts JSON Schema → Zod via `json-schema-to-zod` (npm dep).
   - Uses `useForm` w/ `zodResolver`.
   - Renders fields by walking `schema.properties` in `x-ui:order` order, using `x-ui:control` to pick a renderer (`text`, `textarea`, `datetime`, `select`).
   - Supports the four control types in the seeded Default CaseType (textarea, datetime, select). Other controls render a plain text input as fallback.
   - Inline validation errors per field (RHF `formState.errors`).
   - Submit button disabled while submitting + spinner.
2. **AC2 — Sub-renderers under `apps/web/src/features/intake/controls/`** — one file per control: `TextControl.tsx`, `TextareaControl.tsx`, `DateTimeControl.tsx`, `SelectControl.tsx`. Each accepts `{ name, label, help?, register, error, options? }`. Use shadcn primitives (`Input`, `Textarea`, `Label`, etc.). Add the shadcn components needed via `pnpm dlx shadcn@latest add input label select textarea` first.
3. **AC3 — `apps/web/src/routes/IntakePage.tsx`** — wires `SchemaForm` to:
   - A fixture import at `apps/web/src/features/intake/fixtures/defaultCaseType.json` containing the same JSON as the seeder's `DefaultCaseTypeFieldsSchemaJson` constant. (When F4 lands, this fixture is replaced with a TanStack Query call to `/api/case-types/default`.)
   - A submit handler that `POST`s to `/api/cases` via the existing `bff` client (`apps/web/src/lib/bff.ts`). The endpoint doesn't exist yet (F4) — that's expected; the page should show a clear "queued — receipt: <id>" success on 202 and an inline error otherwise.
4. **AC4 — Wire the route into `apps/web/src/App.tsx`** under the existing `Layout` route at path `/intake`. Add a "New case" link in `apps/web/src/components/Layout.tsx` next to the existing nav.
5. **AC5 — Vitest unit tests** in `apps/web/src/features/intake/__tests__/`:
   - `SchemaForm.test.tsx` — renders required + optional fields per the fixture; submit button blocked while invalid; submits valid payload via mocked onSubmit.
   - `controls/SelectControl.test.tsx` — renders all enum options; selecting fires onChange.
   - `controls/DateTimeControl.test.tsx` — accepts ISO string; emits valid value on change.
   - At least one assertion per `additionalProperties: false` violation: extra unknown field rejected by Zod.
6. **AC6 — `pnpm test` passes; `pnpm build` produces a clean dist**. ESLint passes. No `any` in component signatures.

## Scope
**In scope:**
- All of `apps/web/src/features/intake/**`, `apps/web/src/routes/IntakePage.tsx`, edits to `App.tsx` + `Layout.tsx` for the route + nav link.
- Adding `json-schema-to-zod` + needed shadcn components.

**Out of scope (DO NOT TOUCH):**
- Anything outside `apps/web/**` (Tracks A and B own backend work).
- Real API integration to `/api/cases` (F4 lands that). Fixture-driven for now.
- Auth/login flow (already wired at `apps/web/src/lib/auth.tsx`).

## Manual test plan
1. `pnpm install` in `apps/web` (after adding deps).
2. `pnpm test` — all new tests pass.
3. `pnpm build` — dist builds clean.
4. With Aspire running, navigate to `http://localhost:5010/intake` — form renders w/ summary (textarea), occurredAt (datetime), severity (select). Submit fails w/ 404 (F4 not done) — the page surfaces the error gracefully.

## Self-review checklist
- [ ] All AC met.
- [ ] No `any` in props.
- [ ] No hardcoded "case type" strings — schema is the source of truth.
- [ ] Vitest tests fail before impl (red), pass after (green).
- [ ] `code-reviewer` agent run; findings addressed.
- [ ] Anything discovered out-of-scope is added to `docs/backlog.md`, not silently absorbed.
- [ ] Commit message: `F7: schema-driven intake form (web)` (mirror F1 style).
