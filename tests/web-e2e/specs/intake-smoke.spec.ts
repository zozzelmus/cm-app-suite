import { test, expect } from '@playwright/test'

// End-to-end smoke flow:
//   1. Land on the SPA via the BFF (proves BFF→Vite forwarding)
//   2. Hit the API echo endpoint via BFF→YARP→API
//   3. Submit a case via POST /api/cases (F4)
//   4. Poll GET /api/cases/intake/{receiptId} until Completed (F6)
//   5. Confirm CaseId + CaseNumber populated (proves F3 outbox relay + F5 consumer ran)
//
// Auth in this flow is BYPASS-able: the BFF doesn't [Authorize] the proxied API path
// in the current POC, and the API resolves tenant from the demo TenantContext middleware.
// When real OIDC enforcement lands, swap in a `_test/login-as` route or bypass.

const BASE_URL = process.env.E2E_BASE_URL ?? 'http://localhost:5010'

test.describe('intake → consumer → status', () => {
  test('homepage renders the SPA', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 15_000 })
    // The shell shows either a "Sign in" CTA or the authenticated home, depending on session.
    // Either way, the React tree must mount without console errors.
    const errs: string[] = []
    page.on('pageerror', e => errs.push(e.message))
    await page.waitForLoadState('networkidle')
    expect(errs, `unexpected page errors: ${errs.join('\n')}`).toHaveLength(0)
  })

  test('echo endpoint reachable via BFF YARP proxy', async ({ request }) => {
    const res = await request.get('/api/_meta/echo')
    expect(res.status()).toBe(200)
    const body = await res.json()
    expect(body.ok).toBe(true)
    expect(body.service).toBe('conduct.api')
  })

  test('intake form renders all expected fields', async ({ page }) => {
    await page.goto('/intake')
    // SchemaForm renders summary (textarea, required), occurredAt (datetime), severity (select)
    await expect(page.getByLabel(/summary/i)).toBeVisible({ timeout: 15_000 })
    await expect(page.getByLabel(/occurred ?at/i)).toBeVisible()
    await expect(page.getByLabel(/severity/i)).toBeVisible()
  })

  test('submit case → 202 + receipt → consumer finalises → status reports Completed', async ({ request }) => {
    const submitRes = await request.post('/api/cases', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        caseTypeKey: 'default',
        lobShortCode: 'INV-APAC',
        title: 'E2E smoke - submit',
        data: {
          summary: 'Playwright smoke submission via /api/cases',
          severity: 'High',
          occurredAt: new Date().toISOString(),
        },
      },
    })

    expect(submitRes.status(), `submit body=${await submitRes.text()}`).toBe(202)
    const submitBody = await submitRes.json()
    expect(submitBody.receiptId).toMatch(/^[0-9a-f-]{36}$/)
    expect(submitBody.statusUrl).toMatch(/\/api\/cases\/intake\//)

    // Poll the status endpoint until the consumer finalises the case (≤ ~30s).
    const deadline = Date.now() + 30_000
    let finalStatus = 'Queued'
    let caseId: string | undefined
    let caseNumber: string | undefined
    while (Date.now() < deadline) {
      const sRes = await request.get(submitBody.statusUrl)
      expect(sRes.status()).toBe(200)
      const sBody = await sRes.json()
      finalStatus = sBody.status
      caseId = sBody.caseId
      caseNumber = sBody.caseNumber
      if (finalStatus === 'Completed' || finalStatus === 'Failed') break
      await new Promise(r => setTimeout(r, 1000))
    }

    expect(finalStatus, `last status=${finalStatus}, caseId=${caseId}`).toBe('Completed')
    expect(caseId).toMatch(/^[0-9a-f-]{36}$/)
    expect(caseNumber).toMatch(/^\d{4}-INV-APAC-\d{6}$/)
  })

  test('submit form via UI then poll status (full visual flow)', async ({ page, request }) => {
    await page.goto('/intake')
    await expect(page.getByLabel(/summary/i)).toBeVisible({ timeout: 15_000 })

    await page.getByLabel(/summary/i).fill('Playwright UI submission - smoke test')
    // Datetime input expects YYYY-MM-DDTHH:mm
    const now = new Date()
    const localIso = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}T${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`
    await page.getByLabel(/occurred ?at/i).fill(localIso)
    await page.getByLabel(/severity/i).selectOption('Medium')

    const [response] = await Promise.all([
      page.waitForResponse(r => r.url().includes('/api/cases') && r.request().method() === 'POST', { timeout: 30_000 }),
      page.getByRole('button', { name: /submit|create|file/i }).first().click(),
    ])
    expect(response.status()).toBe(202)
    const submitBody = await response.json()
    expect(submitBody.receiptId).toBeTruthy()

    // Poll via API to confirm end-to-end
    const deadline = Date.now() + 30_000
    let finalStatus = 'Queued'
    while (Date.now() < deadline) {
      const sRes = await request.get(submitBody.statusUrl)
      const sBody = await sRes.json()
      finalStatus = sBody.status
      if (finalStatus === 'Completed' || finalStatus === 'Failed') break
      await new Promise(r => setTimeout(r, 1000))
    }
    expect(finalStatus).toBe('Completed')
  })
})
