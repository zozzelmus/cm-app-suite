import { Link } from 'react-router-dom'

export function SignedOutPage() {
  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="space-y-3 text-center">
        <h1 className="text-xl font-semibold">Signed out</h1>
        <Link to="/" className="text-sm underline text-[var(--color-muted-foreground)]">Return to app</Link>
      </div>
    </div>
  )
}
