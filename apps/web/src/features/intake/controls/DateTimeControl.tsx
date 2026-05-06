import type { FieldValues, Path } from 'react-hook-form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { ControlProps } from './types'

// Uses the native datetime-local input. The user-typed value is in local time
// without offset (e.g. "2026-05-06T10:30"). Conversion to a full ISO string with
// timezone offset is the form-level submit handler's responsibility — the field
// emits exactly what the user typed so the value remains predictable in tests.
export function DateTimeControl<TValues extends FieldValues>({
  name,
  label,
  help,
  register,
  error,
  required,
}: ControlProps<TValues>) {
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>
        {label}
        {required ? <span aria-hidden="true"> *</span> : null}
      </Label>
      <Input
        id={name}
        type="datetime-local"
        aria-invalid={!!error}
        {...register(name as Path<TValues>)}
      />
      {help ? <p className="text-xs text-[var(--color-muted-foreground)]">{help}</p> : null}
      {error?.message ? (
        <p role="alert" className="text-xs text-[var(--color-destructive)]">{error.message}</p>
      ) : null}
    </div>
  )
}
