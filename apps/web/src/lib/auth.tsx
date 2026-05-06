import { type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { bff, HttpError } from './bff'
import { AuthContext, type AuthCtx, type User } from './authContext'

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

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
