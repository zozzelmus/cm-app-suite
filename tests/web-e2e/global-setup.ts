import { request, FullConfig } from '@playwright/test'
import * as fs from 'node:fs'
import * as path from 'node:path'

// Acquire a session cookie via the BFF dev test-login route once before the suite. Saves the
// resulting storageState so each test starts authenticated.
//
// The route is gated on Auth:TestLogin:Enabled (default true in appsettings.Development.json).
// In CI / non-dev environments this setup will fail with a 404, which is the correct signal
// that the bypass is locked down.
export default async function globalSetup(config: FullConfig) {
  const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5010'
  const storageStatePath = path.resolve(__dirname, 'artifacts', 'auth-state.json')
  fs.mkdirSync(path.dirname(storageStatePath), { recursive: true })

  const ctx = await request.newContext({ baseURL })
  const res = await ctx.post('/bff/_test/login-as')
  if (res.status() !== 200) {
    const body = await res.text()
    throw new Error(`E2E global setup: /bff/_test/login-as returned ${res.status()} ${body}`)
  }
  const json = await res.json() as { sub?: string; tenantId?: string }
  if (!json.tenantId) {
    throw new Error(`E2E global setup: tenant_id claim missing from test-login response: ${JSON.stringify(json)}`)
  }
  await ctx.storageState({ path: storageStatePath })
  await ctx.dispose()
}
