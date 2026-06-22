import type { ReactNode } from "react"
import { AlertTriangle } from "lucide-react"
import { Skeleton } from "@/components/ui/skeleton"
import { Button } from "@/components/ui/button"

// Loading / error / empty states shared by the explorer surfaces. A surface renders its own content
// (the table) only once data has loaded and is non-empty; until then this owns the screen so no
// surface has to reimplement skeletons, the error panel, or the empty state.
export function SurfaceState({
  loading,
  error,
  empty,
  emptyLabel = "Nothing loaded.",
  onRetry,
  children,
}: {
  loading: boolean
  error: string | null
  empty: boolean
  emptyLabel?: string
  onRetry?: () => void
  children: ReactNode
}) {
  if (loading) {
    return (
      <div className="space-y-2.5">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-11 w-full" />
        ))}
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex flex-col items-center justify-center rounded-xl border border-destructive/30 bg-destructive/5 px-6 py-12 text-center">
        <AlertTriangle className="mb-3 size-6 text-destructive" />
        <p className="text-sm font-medium">Could not reach the gateway.</p>
        <p className="mt-1 max-w-md font-mono text-xs text-muted-foreground">{error}</p>
        {onRetry ? (
          <Button variant="outline" className="mt-4" onClick={onRetry}>
            Retry
          </Button>
        ) : null}
      </div>
    )
  }

  if (empty) {
    return (
      <div className="rounded-xl border border-border bg-card/50 px-6 py-12 text-center text-sm text-muted-foreground">
        {emptyLabel}
      </div>
    )
  }

  return <>{children}</>
}
