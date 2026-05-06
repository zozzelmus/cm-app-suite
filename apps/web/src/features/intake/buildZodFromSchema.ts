import { jsonSchemaToZod, type JsonSchema } from 'json-schema-to-zod'
import type { JSONSchema7 } from 'json-schema'
import { z, type ZodTypeAny } from 'zod'

// Convert a JSON Schema document into a runnable Zod schema.
// json-schema-to-zod returns a code STRING (intended for codegen). We compile it
// once via `new Function` against the Zod runtime injected at call time. The code
// comes from the deterministic json-schema-to-zod parser (NOT user input), so the
// `new Function` call is safe — its only inputs are the trusted server-supplied
// CaseType.FieldsSchema (validated by JsonSchema.Net on the server before reaching us).
export function buildZodFromSchema(schema: JSONSchema7): ZodTypeAny {
  const code = jsonSchemaToZod(schema as JsonSchema, {
    module: 'none',
    noImport: true,
    zodVersion: 4,
  })
  const factory = new Function('z', `return ${code}`) as (z: typeof import('zod').z) => ZodTypeAny
  return factory(z)
}
