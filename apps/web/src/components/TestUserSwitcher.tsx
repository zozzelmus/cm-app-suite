import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { bff, HttpError } from '@/lib/bff'
import { useAuth } from '@/lib/useAuth'

type TestUser = { username: string; label: string; role: string; scope: string }

// Dev-only quick-switch widget. The BFF only registers /_dev/* in IsDevelopment, so in
// non-dev builds the /users fetch 404s and we silently render nothing.
export function TestUserSwitcher() {
  if (!import.meta.env.DEV) return null
  return <TestUserSwitcherInner />
}

function TestUserSwitcherInner() {
  const { user } = useAuth()
  const qc = useQueryClient()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const usersQ = useQuery<TestUser[] | null>({
    queryKey: ['_dev', 'users'],
    queryFn: async () => {
      try {
        return await bff<TestUser[]>('/_dev/users')
      } catch (e) {
        // 404 in nonprod-but-not-Development builds — just hide the widget.
        if (e instanceof HttpError && e.status === 404) return null
        throw e
      }
    },
    staleTime: Infinity,
  })

  if (usersQ.data === null) return null

  async function loginAs(username: string) {
    setBusy(username)
    setErr(null)
    try {
      await bff('/_dev/login-as', {
        method: 'POST',
        body: JSON.stringify({ username }),
      })
      await qc.invalidateQueries({ queryKey: ['auth', 'user'] })
      // Reload so any cached per-user data is dropped cleanly.
      window.location.reload()
    } catch (e) {
      setErr(e instanceof HttpError ? `${e.status} ${e.message}` : String(e))
      setBusy(null)
    }
  }

  return (
    <div className="fixed bottom-4 right-4 z-50 text-sm">
      <button
        onClick={() => setOpen(o => !o)}
        className="rounded-full border border-[var(--color-border)] bg-[var(--color-background)] px-3 py-1.5 shadow hover:bg-[var(--color-muted)]"
        title="Dev: switch test user"
      >
        🧪 {user?.name ?? 'sign in as…'}
      </button>
      {open && (
        <div className="mt-2 w-96 max-h-[60vh] overflow-auto rounded-md border border-[var(--color-border)] bg-[var(--color-background)] shadow-lg">
          <div className="sticky top-0 border-b border-[var(--color-border)] bg-[var(--color-background)] px-3 py-2 text-xs uppercase tracking-wide text-[var(--color-muted-foreground)]">
            Test users (dev only)
          </div>
          {err && <div className="px-3 py-2 text-xs text-red-600">{err}</div>}
          {usersQ.isLoading && <div className="px-3 py-2 text-xs">loading…</div>}
          <ul>
            {usersQ.data?.map(u => (
              <li key={u.username}>
                <button
                  disabled={busy !== null}
                  onClick={() => loginAs(u.username)}
                  className="w-full text-left px-3 py-2 hover:bg-[var(--color-muted)] disabled:opacity-50 border-b border-[var(--color-border)] last:border-b-0"
                >
                  <div className="font-medium">{u.label}</div>
                  <div className="text-xs text-[var(--color-muted-foreground)]">
                    {u.role} · {u.scope} · <code>{u.username}</code>
                  </div>
                  {busy === u.username && <div className="text-xs">signing in…</div>}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
