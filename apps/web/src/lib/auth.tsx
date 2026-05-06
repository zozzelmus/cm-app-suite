import { createContext, useContext, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { bff, HttpError } from './bff'

export type User = {
  name: string
  claims: { type: string; value: string }[]
}

type AuthCtx = {
  user: User | null
  loading: boolean
  signIn: (returnUrl?: string) => void
  signOut: () => Promise<void>
}

const Ctx = createContext<AuthCtx | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const q = useQuery<User | null>({
    queryKey: ['auth', 'user'],
    queryFn: async () => {
      try {
        return await bff<User>('/bff/user')
      } catch (e) {
        if (e instanceof HttpError && e.status === 401) return null
        throw e
      }
    },
  })

  const value: AuthCtx = {
    user: q.data ?? null,
    loading: q.isLoading,
    signIn: (returnUrl = window.location.pathname + window.location.search) => {
      window.location.href = `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`
    },
    signOut: async () => {
      await bff('/bff/logout', { method: 'POST' })
      window.location.href = '/signed-out'
    },
  }

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useAuth() {
  const ctx = useContext(Ctx)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
