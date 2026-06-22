import type { NavItem } from "@/nav"

// Placeholder for every nav surface this phase. Each real view (config explorer, live trace canvas,
// playground, ...) replaces its placeholder in a later task on the plan chain.
export function Surface({ item, notFound }: { item?: NavItem; notFound?: boolean }) {
  const Icon = item?.icon
  const title = notFound ? "Page not found" : (item?.title ?? "")
  const blurb = notFound
    ? "No console surface is mapped to this path."
    : (item?.blurb ?? "")

  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center text-center">
      <div className="glow mb-5 flex size-14 items-center justify-center rounded-2xl border border-border bg-card">
        {Icon ? <Icon className="size-6 text-muted-foreground" /> : null}
      </div>
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">{blurb}</p>
      <span className="mt-5 rounded-full border border-border px-3 py-1 font-mono text-[11px] uppercase tracking-[0.18em] text-faint">
        Coming soon
      </span>
    </div>
  )
}
