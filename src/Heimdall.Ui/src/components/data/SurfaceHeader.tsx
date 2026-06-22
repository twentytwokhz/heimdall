// The page header shared by every explorer surface: title, one-line blurb, and a live count chip
// (null while the count is unknown, e.g. during the initial config load).
export function SurfaceHeader({
  title,
  blurb,
  count,
}: {
  title: string
  blurb: string
  count?: number | null
}) {
  return (
    <div className="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold">{title}</h1>
        <p className="mt-1 max-w-2xl text-sm text-muted-foreground">{blurb}</p>
      </div>
      {count != null ? (
        <span className="shrink-0 rounded-full border border-border px-3 py-1 font-mono text-[11px] tracking-[0.14em] text-muted-foreground">
          {count}
        </span>
      ) : null}
    </div>
  )
}
