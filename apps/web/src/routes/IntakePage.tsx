import { useState } from 'react'
import type { JSONSchema7 } from 'json-schema'
import { bff, HttpError } from '@/lib/bff'
import { SchemaForm } from '@/features/intake/SchemaForm'
import defaultCaseType from '@/features/intake/fixtures/defaultCaseType.json'

// F4 will replace the fixture import with a query against /api/case-types/default.
const schema = defaultCaseType as unknown as JSONSchema7

type IntakeAck = { receiptId: string; statusUrl?: string }

type SubmitState =
  | { kind: 'idle' }
  | { kind: 'success'; receiptId: string }
  | { kind: 'error'; message: string }

export function IntakePage() {
  const [state, setState] = useState<SubmitState>({ kind: 'idle' })

  return (
    <div className="space-y-4 max-w-2xl">
      <h1 className="text-2xl font-semibold">New case</h1>
      <p className="text-[var(--color-muted-foreground)]">
        Submit a conduct case. Fields are driven by the Default CaseType schema.
      </p>

      <SchemaForm
        schema={schema}
        onSubmit={async (data) => {
          try {
            const ack = await bff<IntakeAck>('/api/cases', {
              method: 'POST',
              body: JSON.stringify({ caseTypeKey: 'default', data }),
            })
            setState({ kind: 'success', receiptId: ack.receiptId })
          } catch (e) {
            // /api/cases lands in F4. Until then surface a clear-but-non-scary message.
            const msg =
              e instanceof HttpError && e.status === 404
                ? 'Intake endpoint not yet available (lands in F4).'
                : e instanceof Error
                ? e.message
                : 'Submission failed.'
            setState({ kind: 'error', message: msg })
          }
        }}
      />

      {state.kind === 'success' ? (
        <p role="status" className="text-sm">
          Queued — receipt: <code>{state.receiptId}</code>
        </p>
      ) : null}
      {state.kind === 'error' ? (
        <p role="alert" className="text-sm text-[var(--color-destructive)]">{state.message}</p>
      ) : null}
    </div>
  )
}
