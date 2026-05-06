import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '@/lib/useAuth'
import { cn } from '@/lib/utils'

export function Layout() {
  const { user, loading, signIn, signOut } = useAuth()

  return (
    <div className="min-h-screen flex flex-col">
      <header className="border-b border-[var(--color-border)] px-6 py-3 flex items-center gap-6">
        <span className="font-semibold tracking-tight">Conduct</span>
        <nav className="flex gap-4 text-sm text-[var(--color-muted-foreground)]">
          <NavLink to="/" end className={({ isActive }) => cn(isActive && 'text-[var(--color-foreground)]')}>Home</NavLink>
          <NavLink to="/cases" className={({ isActive }) => cn(isActive && 'text-[var(--color-foreground)]')}>Cases</NavLink>
          <NavLink to="/intake" className={({ isActive }) => cn(isActive && 'text-[var(--color-foreground)]')}>New case</NavLink>
        </nav>
        <div className="ml-auto text-sm">
          {loading ? '…'
            : user ? (
                <button onClick={signOut} className="text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]">
                  {user.name} · sign out
                </button>
              )
            : (
                <button onClick={() => signIn()} className="text-[var(--color-foreground)] underline">Sign in</button>
              )}
        </div>
      </header>
      <main className="flex-1 p-6">
        <Outlet />
      </main>
    </div>
  )
}
