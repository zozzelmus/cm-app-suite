import type { FieldValues, Path } from 'react-hook-form'
import { Select } from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import type { ControlProps } from './types'

export function SelectControl<TValues extends FieldValues>({
  name,
  label,
  help,
  register,
  error,
  options,
  required,
}: ControlProps<TValues>) {
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>
        {label}
        {required ? <span aria-hidden="true"> *</span> : null}
      </Label>
      <Select id={name} aria-invalid={!!error} {...register(name as Path<TValues>)}>
        <option value="">Select…</option>
        {(options ?? []).map((opt) => (
          <option key={opt} value={opt}>
            {opt}
          </option>
        ))}
      </Select>
      {help ? <p className="text-xs text-[var(--color-muted-foreground)]">{help}</p> : null}
      {error?.message ? (
        <p role="alert" className="text-xs text-[var(--color-destructive)]">{error.message}</p>
      ) : null}
    </div>
  )
}
