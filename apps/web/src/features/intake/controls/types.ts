import type { FieldError, UseFormRegister, FieldValues } from 'react-hook-form'

// Shared shape every schema-driven control accepts. Generic over the form's
// field-values type so consumers keep their RHF type-narrowing.
export type ControlProps<TValues extends FieldValues = FieldValues> = {
  name: string
  label: string
  help?: string
  register: UseFormRegister<TValues>
  error?: FieldError
  options?: readonly string[]
  required?: boolean
}
