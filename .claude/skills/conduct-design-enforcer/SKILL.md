---
name: conduct-design-enforcer
description: Enforce the Conduct design system on every web UI change in apps/web. Use this skill whenever authoring, editing, reviewing, or generating React components, routes, pages, Tailwind classes, CSS, or any frontend file under apps/web/ — even small tweaks like a single class change. Catches raw hex colors, off-palette Tailwind classes (`bg-blue-500`, etc.), emoji, marketing tone, misuse of accent red, gradients, backdrop-blur, drop-shadow excess, wrong icon library, wrong fonts, missing focus rings, non-conformant page chrome (top bar height, max-width, padding), and copy-tone violations. Apply both during authoring and as a final pre-commit pass. Trigger generously — if the change touches the user-visible surface of apps/web, this skill applies.
---

# Conduct design enforcer

Conduct is a finance/compliance case-management app. The visual language is **Claude restraint + L3Harris gravity**: warm-paper neutrals, charcoal headings, a single accent red used surgically, no decoration. The audience is compliance officers and lawyers — never consumers. Every UI decision should default to *quiet, dense, trustworthy*. When in doubt, lean colder, plainer, more textual.

This skill exists because the design system is easy to *drift away from* one component at a time. A single `bg-blue-500` or a stray emoji erodes the whole effect. Catch them at authoring time.

## Source of truth

[apps/web/src/index.css](../../../apps/web/src/index.css) defines every token. Read it first — never duplicate values into a component. The tokens you'll use most:

- Neutrals: `--paper` `--surface` `--surface-2` `--ink` `--ink-2` `--muted` `--subtle` `--border` `--border-strong`
- Accent (sparingly!): `--accent` `--accent-2`
- Severity ramp: `--sev-low` `--sev-med` `--sev-high` `--sev-crit`
- Status: `--success` `--info`
- Radii: `--r-sm` (4) `--r-md` (6) `--r-lg` (10) `--r-pill`
- Shadow: `--shadow-1` (default) `--shadow-2` (modals only)
- Type scale: `--fs-display` `--fs-h1` `--fs-h2` `--fs-h3` `--fs-body` `--fs-small` `--fs-micro` `--fs-mono` (each with matching `--lh-*` and optional `--tr-*`)

Tokens are aliased to shadcn names via `@theme inline` (e.g. `--color-foreground` → `--ink`). Use the shadcn-style Tailwind utilities when they exist (`bg-[var(--color-background)]`, `text-[var(--color-foreground)]`, `border-[var(--color-border)]`), and the raw `--*` vars when you need a token that has no shadcn alias (severity, ink-2, mono font, etc.).

## The 10 rules

### 1. Tokens or nothing — no raw hex, no Tailwind palette

Raw hex (`#FF0000`, `#1A1D1D`) and Tailwind palette utilities (`bg-blue-500`, `text-gray-700`, `border-slate-200`) are both forbidden. They snapshot a moment instead of binding to the system. Always go through a token.

```tsx
// no
<div className="bg-slate-50 text-gray-900 border-gray-200" />
<div style={{ color: '#1A1D1D' }} />

// yes
<div className="bg-[var(--color-background)] text-[var(--color-foreground)] border-[var(--color-border)]" />
<div style={{ color: 'var(--ink)' }} />
```

If a token doesn't exist for what you need, **add a token to `index.css`** — don't inline.

### 2. Accent red is reserved

`--accent` / `--sev-crit` / `--color-destructive` is for **destructive actions, severity Critical, and the brand mark accent only**. Not for primary CTAs. Not for links. Not for hover states. Not for active nav items. Not for badges that just want attention.

Primary CTA is `--ink` (`bg-[var(--color-primary)]`). Links inherit `--ink` and underline. If your eye says "this feels too quiet," good — the calm is the brand.

### 3. Borders carry the load — one shadow exists

Cards, panels, popovers, table containers: 1px border in `--border`, background `--surface`, radius `--r-md`. Optionally `--shadow-1` if they float. That's the entire card vocabulary.

Forbidden: drop shadows beyond `--shadow-1` (modals get `--shadow-2`, nothing else); left-border accent stripes; gradient headers; two-tone borders; `0 20px 40px` consumer floats; `backdrop-filter: blur(*)` anywhere (it reads consumer SaaS — sticky bars use solid `--paper` with a 1px bottom border).

### 4. No gradients, no large radii, no textures

No `linear-gradient`, `radial-gradient`, or `conic-gradient` anywhere — not in heros, not in cards, not in pills. No textures or background patterns on app surfaces. No radii > 10px (`--r-lg`); the only pill is the status chip (`--r-pill`). Soft 16px+ radii read consumer.

### 5. Typography: Inter + JetBrains Mono, tabular figures on data

Body and headings: Inter via the `--font-sans` token (set globally in `index.css`). Don't re-declare `font-family` on components. Headings use `--ink-2` (charcoal) via the global `h1/h2/h3` styles — don't override.

Anything that's an **identifier, case number, receipt ID, timestamp, money figure, or code** uses `--font-mono` (JetBrains Mono) with `font-variant-numeric: tabular-nums`. The base CSS already applies this to `<code>`, `<pre>`, `<kbd>`, `<samp>`, and `.font-mono`. For inline numerics in regular text, add `className="tnum"`.

```tsx
// case number
<span className="font-mono">2026-INV-APAC-000001</span>
// receipt
<code>{receiptId}</code>
// dates / money in body copy
<span className="tnum">14:32 UTC</span>
```

### 6. Iconography: Lucide, 1.5px stroke, currentColor

`components.json` declares `iconLibrary: "lucide"`. Use `lucide-react` exclusively. Stroke weight `1.5`. Sizes 14/16/18/20/24 only (integer px). Color is always `currentColor` (inherits from surrounding text token); never set icon color to red unless the icon sits inside a destructive button.

Forbidden: emoji (`🚨 ✅ ⚠️ 🔥`), unicode pseudo-icons (`✓ ✗ → ←`), filled glyphs (use outline; the only exception is a state that genuinely needs a filled form, e.g. a selected radio). No competing icon sets (Heroicons, Phosphor, Tabler).

```tsx
import { Check, X, ArrowRight, AlertCircle } from 'lucide-react'

<Check className="size-4" strokeWidth={1.5} aria-hidden />
```

### 7. Copy tone: terse, neutral, sentence case, no emoji

The audience is compliance officers. Sacrifice warmth for clarity.

- Sentence case for all UI: titles, headings, buttons, table headers, menu items.
- Title Case only for proper nouns (Conduct, Compliance, LOB names like "Investments — APAC").
- ALL CAPS only for tags / pills / monogram marks.
- Voice is third-person or imperative. Avoid "I". Use "you" only in micro-copy and confirmations.
- No emoji. No exclamations. No "let's", "we're excited to…", "Welcome back!".
- Empty states state the fact, then offer the next action.
- Status labels are single words: *Queued · In review · Transferred · Closed · Failed*. Never sentence fragments ("Has been transferred").
- Identifiers in copy use backticks/code: `Queued — receipt: <id>`.
- Dates: ISO machine, "07 May 2026" human, 24h with timezone (`14:32 UTC`) when ambiguous. Never US-abbreviated like `5/7/26`.

Tone exemplars (from the codebase, follow these):
- *"Greenfield — domain modelling in progress. This shell verifies the BFF/API/auth wiring."*
- *"Awaiting domain decisions: LOB list, case typology, lifecycle states, transfer protocol."*
- *"Submit a conduct case. Fields are driven by the Default CaseType schema."*

### 8. Page chrome

- Top bar: sticky, `h-14` (56px), 1px bottom border in `--border`, solid `--paper` background (no blur). Never auto-hides on scroll.
- Main content: `max-w-[1240px]` centered, responsive page padding `px-6 md:px-8 xl:px-14` (24 / 32 / 56 px).
- Side nav (when added): 240px fixed, no desktop collapse.
- Forms: single column, label above input, help text below in `--muted`, max width 640px for intake.
- Tables: row hover `--surface-2`, no zebra striping, sticky header on scroll, left-aligned to gutter.

### 9. Focus is sacred

The global `:focus-visible` rule sets a 2px `--ink` outline at 2px offset on every interactive element. Don't override it. Don't remove it. Don't use `outline-none` without immediately re-establishing a visible focus indicator with the same contrast. Compliance audiences include keyboard users and screen-reader users; visible focus is non-negotiable.

### 10. Motion: short, no springs

Hover 120ms, dropdowns/popovers 160ms, dialogs 200ms. Easing `cubic-bezier(0.2, 0, 0, 1)`. No bounces, no springs, no scale-on-hover. No page transitions — the app is information-dense; motion is distraction. Skeletons shimmer 800ms `--surface-2` → `--paper`.

Press feedback is `transform: translateY(0.5px)` plus one step darker background. Hover is background → `--surface-2` (rows, ghost buttons) or border → `--border-strong` (input-likes). Never opacity-shift borders.

## Severity vs status

These have separate color ramps on purpose — keep them separate.

- **Status** (Queued, In review, Transferred, Closed, Failed) uses neutrals + `--success` / `--info`. Failed = `--accent`.
- **Severity** (Low, Medium, High, Critical) uses the `--sev-*` ramp. Critical = `--accent`.

A status pill and a severity pill on the same case are *both colored* but communicating different things. Don't collapse them.

## Pre-write checklist

Before writing or editing any UI in `apps/web`, mentally tick:

- [ ] Every color reference goes through a token (no hex, no Tailwind palette).
- [ ] Red appears only on destructive / Critical / brand mark.
- [ ] Identifiers, case numbers, receipts, code, and inline numerics use mono + tabular figures.
- [ ] Icons are `lucide-react`, stroke 1.5, `currentColor`. No emoji, no pseudo-icons.
- [ ] Copy is sentence case, terse, no marketing, no emoji.
- [ ] No gradients, no backdrop-blur, no radii > 10px, no consumer drop shadows.
- [ ] Cards are 1px border + `--surface` + `--r-md`, optionally `--shadow-1`.
- [ ] Page chrome respects sticky 56px top bar, 1240px max-width, responsive padding.
- [ ] Focus indicator visible everywhere; not removed; uses the global ring.
- [ ] If you needed a value the tokens don't cover, you added a token to `index.css` — you didn't inline.

## Common mistakes (caught and corrected)

**Mistake: primary button styled with accent red.**
```tsx
// no
<Button className="bg-red-600 hover:bg-red-700">Submit</Button>
// yes — primary is ink
<Button>Submit</Button>
```

**Mistake: emoji status indicator.**
```tsx
// no
<span>✅ Queued</span>
// yes
import { Check } from 'lucide-react'
<span className="inline-flex items-center gap-1.5"><Check className="size-4" strokeWidth={1.5} aria-hidden /> Queued</span>
```

**Mistake: case number in proportional font.**
```tsx
// no
<td>{caseNumber}</td>
// yes
<td className="font-mono">{caseNumber}</td>
```

**Mistake: cheerful empty state.**
```tsx
// no
<p>Nothing here yet! 🎉 Let's create your first case.</p>
// yes
<p className="text-[var(--color-muted-foreground)]">No cases. <Link to="/intake" className="underline">Submit a case</Link>.</p>
```

**Mistake: ad-hoc card shadow.**
```tsx
// no
<div className="shadow-xl rounded-2xl bg-white border-l-4 border-red-500" />
// yes
<div className="rounded-md border border-[var(--color-border)] bg-[var(--color-background)] p-5" style={{ boxShadow: 'var(--shadow-1)' }} />
```

**Mistake: removing focus outline.**
```css
/* no */
button:focus { outline: none; }
/* yes — global :focus-visible already correct; don't override */
```

## When to push back on the user

If the user asks for something the system forbids — e.g. "make the submit button red", "add a gradient to the header", "use Inter Display Bold for marketing punch", "throw an emoji on the success state" — say so plainly and offer the conformant alternative. The design system is the product's voice; deviation costs more than it earns. One sentence is enough: "Conduct reserves red for destructive — using ink for the primary CTA, accent only on Critical."

## Out of scope (don't enforce here)

- Backend code, .NET, SQL, infra — this skill is web UI only.
- Test files (`*.test.tsx`) — focus on production UI files. Tests can use any styling.
- Storybook / preview fixtures — same.
- One-off internal debugging pages clearly marked as such.
