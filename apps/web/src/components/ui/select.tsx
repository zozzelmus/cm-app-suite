import * as React from 'react'
import { cn } from '@/lib/utils'

// Native <select> wrapper styled to match shadcn aesthetic. Native is fine for the
// intake form's enum dropdowns; full Radix Select can be added later if a richer UX is needed.
export type SelectProps = React.SelectHTMLAttributes<HTMLSelectElement>

export const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  ({ className, children, ...props }, ref) => (
    <select
      ref={ref}
      className={cn(
        'flex h-9 w-full rounded-md border border-[var(--color-input)] bg-transparent px-3 py-1 text-sm shadow-sm',
        'focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-foreground)]',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    >
      {children}
    </select>
  ),
)
Select.displayName = 'Select'
