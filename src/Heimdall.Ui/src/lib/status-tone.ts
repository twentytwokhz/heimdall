// Status-code tone classes (border + text + tint), shared by the Live canvas pills and the
// Playground response panel. Mirrors the design legend: green 2xx, blue 3xx, amber 4xx, rose 5xx.
export function statusTone(code: number): string {
  if (code >= 500) return "text-rose border-rose/40 bg-rose/10"
  if (code >= 400) return "text-amber border-amber/40 bg-amber/10"
  if (code >= 300) return "text-blue border-blue/40 bg-blue/10"
  return "text-green border-green/40 bg-green/10"
}
