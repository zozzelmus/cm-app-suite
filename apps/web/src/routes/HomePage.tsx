import { useQuery } from '@tanstack/react-query'
import { bff } from '@/lib/bff'
import { useAuth } from '@/lib/auth'

type Echo = { ok: boolean; service: string; ts: string }

export function HomePage() {
  const { user, signIn } = useAuth()
  const echo = useQuery<Echo>({
    queryKey: ['api', 'echo'],
    queryFn: () => bff<Echo>('/api/_meta/echo'),
    enabled: !!user,
  })

  return (
    <div className="space-y-4 max-w-2xl">
      <h1 className="text-2xl font-semibold">Conduct case management</h1>
      <p className="text-[var(--color-muted-foreground)]">
        Greenfield — domain modelling in progress. This shell verifies the BFF/API/auth wiring.
      </p>
      {!user ? (
        <button onClick={() => signIn()} className="rounded bg-[var(--color-primary)] text-[var(--color-primary-foreground)] px-3 py-1.5 text-sm">
          Sign in to continue
        </button>
      ) : echo.isLoading ? <p>Calling /api/_meta/echo…</p>
        : echo.isError ? <p className="text-[var(--color-destructive)]">API call failed</p>
        : (
            <pre className="rounded bg-[var(--color-muted)] p-3 text-sm">{JSON.stringify(echo.data, null, 2)}</pre>
          )}
    </div>
  )
}
