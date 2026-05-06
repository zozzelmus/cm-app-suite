import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { JSONSchema7 } from 'json-schema'
import { SchemaForm } from '@/features/intake/SchemaForm'
import fixture from '@/features/intake/fixtures/defaultCaseType.json'

const schema = fixture as unknown as JSONSchema7

describe('SchemaForm', () => {
  it('renders fields in x-ui:order with labels from x-ui:label', () => {
    const { container } = render(<SchemaForm schema={schema} onSubmit={async () => {}} />)
    // Read labels in DOM order; required marker is rendered as aria-hidden span,
    // so the visible accessible label is just the text node.
    const labels = Array.from(container.querySelectorAll('label')).map(
      (l) => l.firstChild?.textContent ?? '',
    )
    expect(labels).toEqual(['Summary', 'Occurred at', 'Severity'])
  })

  it('shows required-field error when summary is empty on submit', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn(async () => {})
    render(<SchemaForm schema={schema} onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: /submit/i }))
    await waitFor(() => expect(screen.getByText(/required|too small|empty/i)).toBeInTheDocument())
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submits valid payload', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn(async () => {})
    render(<SchemaForm schema={schema} onSubmit={onSubmit} />)
    await user.type(screen.getByLabelText(/Summary/), 'A real concern')
    await user.click(screen.getByRole('button', { name: /submit/i }))
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    const firstCall = onSubmit.mock.calls[0] as unknown as [Record<string, unknown>]
    expect(firstCall[0].summary).toBe('A real concern')
  })

  it('rejects extra unknown fields via additionalProperties: false', async () => {
    // Drive the same Zod that SchemaForm uses to ensure additionalProperties: false is honored.
    const { buildZodFromSchema } = await import('@/features/intake/buildZodFromSchema')
    const z = buildZodFromSchema(schema)
    const result = z.safeParse({ summary: 'ok', bogus: 'nope' })
    expect(result.success).toBe(false)
  })

  it('disables submit while submitting', async () => {
    const user = userEvent.setup()
    let resolve: (() => void) | null = null
    const onSubmit = vi.fn(
      () => new Promise<void>((r) => { resolve = r }),
    )
    render(<SchemaForm schema={schema} onSubmit={onSubmit} />)
    await user.type(screen.getByLabelText(/Summary/), 'A real concern')
    const btn = screen.getByRole('button', { name: /submit/i })
    await user.click(btn)
    await waitFor(() => expect(btn).toBeDisabled())
    resolve!()
    await waitFor(() => expect(btn).not.toBeDisabled())
  })
})
