import { useMemo } from 'react'
import {
  useForm,
  type DefaultValues,
  type FieldError,
  type FieldValues,
  type Resolver,
} from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import type { JSONSchema7 } from 'json-schema'
import { Button } from '@/components/ui/button'
import { buildZodFromSchema } from './buildZodFromSchema'
import { TextControl } from './controls/TextControl'
import { TextareaControl } from './controls/TextareaControl'
import { DateTimeControl } from './controls/DateTimeControl'
import { SelectControl } from './controls/SelectControl'
import type { ControlProps } from './controls/types'

// Loose JSON-Schema property type that admits the x-ui:* extension keys.
type PropSchema = JSONSchema7 & {
  enum?: readonly string[]
  'x-ui:control'?: string
  'x-ui:order'?: number
  'x-ui:label'?: string
  'x-ui:help'?: string
}

type Payload = Record<string, unknown>

export type SchemaFormProps = {
  schema: JSONSchema7
  defaultValues?: Payload
  onSubmit: (data: Payload) => Promise<void>
}

const CONTROLS: Record<string, <T extends FieldValues>(p: ControlProps<T>) => React.JSX.Element> = {
  text: TextControl,
  textarea: TextareaControl,
  datetime: DateTimeControl,
  select: SelectControl,
}

// Convert a datetime-local string ("YYYY-MM-DDTHH:mm") to a full ISO string in
// the user's timezone. Empty string passes through (lets optional fields stay undefined).
function localDateTimeToIso(value: string): string {
  if (!value) return ''
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? value : d.toISOString()
}

export function SchemaForm({ schema, defaultValues, onSubmit }: SchemaFormProps) {
  const zodSchema = useMemo(() => buildZodFromSchema(schema), [schema])

  const properties = (schema.properties ?? {}) as Record<string, PropSchema>
  const required = new Set((schema.required as string[] | undefined) ?? [])

  // Order by x-ui:order then by declaration order.
  const ordered = useMemo(() => {
    return Object.entries(properties)
      .map(([name, prop], idx) => ({ name, prop, idx }))
      .sort((a, b) => {
        const ao = a.prop['x-ui:order'] ?? Number.POSITIVE_INFINITY
        const bo = b.prop['x-ui:order'] ?? Number.POSITIVE_INFINITY
        return ao !== bo ? ao - bo : a.idx - b.idx
      })
  }, [properties])

  const initialValues = useMemo<Payload>(() => {
    const empty: Payload = {}
    for (const { name } of ordered) empty[name] = ''
    return { ...empty, ...(defaultValues ?? {}) }
  }, [ordered, defaultValues])

  // Wrap zodResolver: native form inputs emit "" for blank optional fields, but our
  // Zod schema (built from JSON Schema) marks those fields as `string().optional()`,
  // which rejects "". Strip empty strings on optional/non-required fields before validation.
  const baseResolver = useMemo(
    () => zodResolver(zodSchema as never) as unknown as Resolver<Payload>,
    [zodSchema],
  )
  const resolver: Resolver<Payload> = useMemo(
    () => async (values, ctx, opts) => {
      const cleaned: Payload = { ...values }
      for (const { name, prop } of ordered) {
        if (cleaned[name] === '' && !required.has(name)) delete cleaned[name]
        // Convert datetime-local "YYYY-MM-DDTHH:mm" to ISO so date-time format check passes.
        if (prop['x-ui:control'] === 'datetime' && typeof cleaned[name] === 'string' && cleaned[name]) {
          cleaned[name] = localDateTimeToIso(cleaned[name] as string)
        }
      }
      return baseResolver(cleaned, ctx, opts)
    },
    [baseResolver, ordered, required],
  )

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Payload>({
    resolver,
    defaultValues: initialValues as DefaultValues<Payload>,
    mode: 'onSubmit',
  })

  // Resolver already stripped empty optionals + converted datetimes, so `data` matches
  // what the Zod schema validated. Forward as-is to the consumer.
  const submit = handleSubmit(async (data) => {
    await onSubmit(data)
  })

  return (
    <form onSubmit={submit} className="space-y-4 max-w-xl" noValidate>
      {ordered.map(({ name, prop }) => {
        const controlKey = prop['x-ui:control'] ?? 'text'
        const Component = CONTROLS[controlKey] ?? TextControl
        const label = prop['x-ui:label'] ?? name
        const help = prop['x-ui:help']
        const options = prop.enum
        return (
          <Component
            key={name}
            name={name}
            label={label}
            help={help}
            register={register}
            error={errors[name] as FieldError | undefined}
            options={options}
            required={required.has(name)}
          />
        )
      })}
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? 'Submitting…' : 'Submit'}
      </Button>
    </form>
  )
}
