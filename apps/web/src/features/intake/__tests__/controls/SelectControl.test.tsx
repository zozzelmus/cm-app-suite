import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { SelectControl } from '@/features/intake/controls/SelectControl'

function Harness({ onChange }: { onChange: (v: string) => void }) {
  const { register, watch } = useForm<{ severity: string }>({ defaultValues: { severity: '' } })
  const value = watch('severity')
  // Surface watched value so tests can assert.
  return (
    <>
      <SelectControl
        name="severity"
        label="Severity"
        register={register}
        options={['Low', 'Medium', 'High', 'Critical']}
      />
      <output data-testid="value">{value}</output>
      <button type="button" onClick={() => onChange(value)}>read</button>
    </>
  )
}

describe('SelectControl', () => {
  it('renders all enum options + a placeholder', () => {
    const { container } = render(<Harness onChange={() => {}} />)
    const opts = Array.from(container.querySelectorAll('option')).map((o) => o.textContent)
    // First option is placeholder, then enum values.
    expect(opts.slice(-4)).toEqual(['Low', 'Medium', 'High', 'Critical'])
  })

  it('updates form value on change', async () => {
    const user = userEvent.setup()
    render(<Harness onChange={() => {}} />)
    await user.selectOptions(screen.getByLabelText('Severity'), 'High')
    expect(screen.getByTestId('value').textContent).toBe('High')
  })
})
