import type { RequestTrace } from "@/lib/api"

// Live-traffic metrics derived entirely from the recent traces the client already holds (the
// backend exposes no aggregates). Every number traces back to an observable fact in a RequestTrace,
// so the strip stays honest: no hardcoded or fabricated values. Pure and framework-free so the math
// is unit-tested in isolation; the surface formats these counts into the displayed percentages.

export type StatusMix = { c2xx: number; c4xx: number; c5xx: number; other: number }
export type CacheStats = { hits: number; misses: number }

export type TraceMetrics = {
  total: number
  /** Requests whose timestamp falls in the trailing 60s window (a rate, unlike the other stats). */
  last60s: number
  /** Latency percentiles over the buffer, in ms; null when there are no traces. */
  p50: number | null
  p95: number | null
  status: StatusMix
  /** Count of 429s (also included in status.c4xx). */
  rateLimited: number
  /** Hit/miss over caching traces; null when no trace ran a cache-lookup (nothing to report). */
  cache: CacheStats | null
}

const WINDOW_MS = 60_000

// Nearest-rank percentile over an ascending-sorted array (caller guarantees non-empty).
function percentile(sortedAsc: number[], p: number): number {
  const rank = Math.ceil((p / 100) * sortedAsc.length)
  const index = Math.min(sortedAsc.length - 1, Math.max(0, rank - 1))
  return sortedAsc[index]
}

const usedCache = (trace: RequestTrace): boolean =>
  trace.stages.some((stage) => stage.policies.some((policy) => policy.name === "cache-lookup"))

const reachedBackend = (trace: RequestTrace): boolean =>
  trace.stages.some((stage) => stage.section === "Backend")

export function computeMetrics(traces: RequestTrace[], nowMs: number): TraceMetrics {
  const status: StatusMix = { c2xx: 0, c4xx: 0, c5xx: 0, other: 0 }
  const durations: number[] = []
  let last60s = 0
  let rateLimited = 0
  let cacheHits = 0
  let cacheMisses = 0
  let sawCache = false

  for (const trace of traces) {
    durations.push(trace.durationMs)

    const ts = Date.parse(trace.timestamp)
    if (!Number.isNaN(ts) && nowMs - ts <= WINDOW_MS) {
      last60s++
    }

    const code = trace.statusCode
    if (code >= 200 && code < 300) status.c2xx++
    else if (code >= 400 && code < 500) status.c4xx++
    else if (code >= 500 && code < 600) status.c5xx++
    else status.other++

    if (code === 429) {
      rateLimited++
    }

    // A cache hit short-circuits the pipeline before the backend, so a caching trace that never
    // reached the Backend stage is a HIT; one that did is a MISS. The shape encodes the outcome.
    if (usedCache(trace)) {
      sawCache = true
      if (reachedBackend(trace)) cacheMisses++
      else cacheHits++
    }
  }

  durations.sort((a, b) => a - b)

  return {
    total: traces.length,
    last60s,
    p50: durations.length ? percentile(durations, 50) : null,
    p95: durations.length ? percentile(durations, 95) : null,
    status,
    rateLimited,
    cache: sawCache ? { hits: cacheHits, misses: cacheMisses } : null,
  }
}
