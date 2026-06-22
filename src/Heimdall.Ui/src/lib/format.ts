// Format a duration in milliseconds for the trace UIs: whole ms once it is >= 10ms, one decimal
// below that (sub-10ms timings carry meaningful precision). Shared by the Live canvas and Overview.
export function fmtMs(ms: number): string {
  if (ms >= 10) return `${Math.round(ms)} ms`
  return `${ms.toFixed(1)} ms`
}

// Format a live request count for the topbar. The trace feed is a bounded ring buffer holding only
// the last `cap`, so at/over the cap the exact total is unknown: show "cap+" rather than fake a number.
export function fmtRequestCount(count: number, cap: number): string {
  if (count >= cap) return `${cap}+ requests`
  if (count === 1) return "1 request"
  return `${count} requests`
}
