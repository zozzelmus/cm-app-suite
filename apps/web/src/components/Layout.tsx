import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '@/lib/useAuth'
import { cn } from '@/lib/utils'
import { TestUserSwitcher } from '@/components/TestUserSwitcher'

export function Layout() {
  const { user, loading, signIn, signOut } = useAuth()

  return (
    <div className="min-h-screen flex flex-col">
      <header className="sticky top-0 z-40 h-14 border-b border-[var(--color-border)] bg-[var(--color-background)] px-6 flex items-center gap-6">
        <span className="font-semibold tracking-tight text-[var(--color-foreground)]">Conduct</span>
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
      <main className="flex-1 px-6 py-6 md:px-8 xl:px-14">
        <div className="mx-auto max-w-[1240px]">
          <Outlet />
        </div>
      </main>
      <TestUserSwitcher />
    </div>
  )
}
