// Format a duration in milliseconds for the trace UIs: whole ms once it is >= 10ms, one decimal
// below that (sub-10ms timings carry meaningful precision). Shared by the Live canvas and Overview.
export function fmtMs(ms: number): string {
  if (ms >= 10) return `${Math.round(ms)} ms`
  return `${ms.toFixed(1)} ms`
}
