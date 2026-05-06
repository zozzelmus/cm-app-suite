import { createContext } from 'react'

export type User = {
  name: string
  claims: { type: string; value: string }[]
}

export type AuthCtx = {
  user: User | null
  loading: boolean
  signIn: (returnUrl?: string) => void
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthCtx | null>(null)
