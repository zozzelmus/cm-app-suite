import type { FieldValues, Path } from 'react-hook-form'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import type { ControlProps } from './types'

export function TextareaControl<TValues extends FieldValues>({
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
      <Textarea id={name} aria-invalid={!!error} {...register(name as Path<TValues>)} />
      {help ? <p className="text-xs text-[var(--color-muted-foreground)]">{help}</p> : null}
      {error?.message ? (
        <p role="alert" className="text-xs text-[var(--color-destructive)]">{error.message}</p>
      ) : null}
    </div>
  )
}
