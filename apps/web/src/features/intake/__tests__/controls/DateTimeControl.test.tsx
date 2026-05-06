import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { DateTimeControl } from '@/features/intake/controls/DateTimeControl'

function Harness() {
  const { register, watch } = useForm<{ occurredAt: string }>({ defaultValues: { occurredAt: '' } })
  const value = watch('occurredAt')
  return (
    <>
      <DateTimeControl name="occurredAt" label="Occurred at" register={register} />
      <output data-testid="value">{value}</output>
    </>
  )
}

describe('DateTimeControl', () => {
  it('uses datetime-local input type', () => {
    render(<Harness />)
    const input = screen.getByLabelText('Occurred at') as HTMLInputElement
    expect(input.type).toBe('datetime-local')
  })

  it('emits the entered value on change', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const input = screen.getByLabelText('Occurred at') as HTMLInputElement
    await user.type(input, '2026-05-06T10:30')
    expect(screen.getByTestId('value').textContent).toBe('2026-05-06T10:30')
  })
})
