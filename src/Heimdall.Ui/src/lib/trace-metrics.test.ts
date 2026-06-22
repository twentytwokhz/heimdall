import { describe, expect, it } from "vitest"
import type { RequestTrace, TraceOutcome, TraceStage } from "@/lib/api"
import { computeMetrics } from "@/lib/trace-metrics"

const NOW = Date.parse("2026-06-22T12:00:00.000Z")

// Minimal trace factory: only the fields the metrics read matter; the rest get harmless defaults.
function mk(overrides: Partial<RequestTrace> = {}): RequestTrace {
  return {
    requestId: "00000000-0000-0000-0000-000000000000",
    timestamp: new Date(NOW).toISOString(),
    method: "GET",
    path: "/catalog/items",
    apiId: "acme",
    apiName: "Acme",
    operationId: "get-items",
    operationMethod: "GET",
    subscriptionId: null,
    productId: null,
    statusCode: 200,
    durationMs: 10,
    outcome: "Completed" as TraceOutcome,
    error: null,
    stages: [],
    ...overrides,
  }
}

const inbound = (policies: string[]): TraceStage => ({
  section: "Inbound",
  durationMs: 1,
  policies: policies.map((name) => ({ name, durationMs: 0.5 })),
})
const backend: TraceStage = { section: "Backend", durationMs: 5, policies: [] }

describe("computeMetrics", () => {
  it("returns an empty result for no traces", () => {
    const m = computeMetrics([], NOW)
    expect(m.total).toBe(0)
    expect(m.last60s).toBe(0)
    expect(m.p50).toBeNull()
    expect(m.p95).toBeNull()
    expect(m.status).toEqual({ c2xx: 0, c4xx: 0, c5xx: 0, other: 0 })
    expect(m.rateLimited).toBe(0)
    expect(m.cache).toBeNull()
  })

  it("counts only traces within the trailing 60s window", () => {
    const traces = [
      mk({ timestamp: new Date(NOW - 5_000).toISOString() }), // in
      mk({ timestamp: new Date(NOW - 59_000).toISOString() }), // in
      mk({ timestamp: new Date(NOW - 61_000).toISOString() }), // out
      mk({ timestamp: new Date(NOW - 120_000).toISOString() }), // out
    ]
    expect(computeMetrics(traces, NOW).last60s).toBe(2)
  })

  it("computes p50 and p95 by nearest-rank over durations", () => {
    const traces = Array.from({ length: 10 }, (_, i) => mk({ durationMs: (i + 1) * 10 }))
    const m = computeMetrics(traces, NOW)
    // sorted [10..100]: p50 -> rank ceil(5)=5 -> idx4 -> 50; p95 -> rank ceil(9.5)=10 -> idx9 -> 100
    expect(m.p50).toBe(50)
    expect(m.p95).toBe(100)
  })

  it("buckets status codes; 429 also counts as rate-limited", () => {
    const traces = [
      mk({ statusCode: 200 }),
      mk({ statusCode: 201 }),
      mk({ statusCode: 401 }),
      mk({ statusCode: 429 }),
      mk({ statusCode: 500 }),
      mk({ statusCode: 302 }),
    ]
    const m = computeMetrics(traces, NOW)
    expect(m.status).toEqual({ c2xx: 2, c4xx: 2, c5xx: 1, other: 1 })
    expect(m.rateLimited).toBe(1)
  })

  it("infers cache hit (short-circuit, no Backend stage) vs miss (reached Backend)", () => {
    const traces = [
      mk({ stages: [inbound(["cache-lookup"])], outcome: "ShortCircuited" }), // HIT
      mk({ stages: [inbound(["cache-lookup"]), backend] }), // MISS
      mk({ stages: [inbound(["validate-jwt"]), backend] }), // not a caching trace
    ]
    expect(computeMetrics(traces, NOW).cache).toEqual({ hits: 1, misses: 1 })
  })

  it("reports null cache when no trace used cache-lookup", () => {
    const traces = [mk({ stages: [inbound(["rate-limit"]), backend] })]
    expect(computeMetrics(traces, NOW).cache).toBeNull()
  })
})
