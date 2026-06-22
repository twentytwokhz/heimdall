import { useEffect, useMemo, useState, type ReactNode } from "react"
import { cn } from "@/lib/utils"
import { useTraceFeed } from "@/lib/use-trace-feed"
import { computeMetrics, type TraceMetrics } from "@/lib/trace-metrics"
import { statusTone } from "@/lib/status-tone"
import { fmtMs } from "@/lib/format"
import type { RequestTrace, TraceStage } from "@/lib/api"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { SurfaceState } from "@/components/data/SurfaceState"
import { Button } from "@/components/ui/button"

// The four canvas columns, in pipeline order. The Frontend stage carries no policies (it is the
// request as received); the rest list the policy elements that fired, from the selected trace.
const CANVAS_STAGES: { section: string; title: string; blurb: string }[] = [
  { section: "Frontend", title: "Frontend", blurb: "The request as received from the client." },
  { section: "Inbound", title: "Inbound", blurb: "Applied before the request reaches the backend." },
  { section: "Backend", title: "Backend", blurb: "Forward to the configured HTTP endpoint." },
  { section: "Outbound", title: "Outbound", blurb: "Applied to the response before it returns." },
]

const FLOW = new Set(["choose", "when", "otherwise", "forward-request", "return-response", "mock-response", "retry", "wait", "include-fragment"])
const AUTH = new Set(["validate-jwt", "authentication-basic", "authentication-certificate", "ip-filter", "check-header"])
const RATE = new Set(["rate-limit", "rate-limit-by-key", "quota", "quota-by-key"])

// Policy-family colour for the chip dot (mirrors the design legend): flow=cyan, auth=indigo,
// rate=amber, base=faint, everything else (transforms) = blue.
function policyFamily(name: string): string {
  if (name === "base") return "base"
  if (FLOW.has(name)) return "k-flow"
  if (AUTH.has(name)) return "k-auth"
  if (RATE.has(name)) return "k-rate"
  return "k-xform"
}

function StatusPill({ code }: { code: number }) {
  return (
    <span className={cn("rounded border px-1.5 py-0.5 font-mono text-[11px] tabular-nums", statusTone(code))}>
      {code}
    </span>
  )
}

function PolicyChip({ name, durationMs }: { name: string; durationMs: number }) {
  return (
    <div className={cn("chip", policyFamily(name))}>
      <span className="ic" />
      {name}
      <span className="ms">{fmtMs(durationMs)}</span>
    </div>
  )
}

function StageCard({
  def,
  stage,
  trace,
  index,
}: {
  def: (typeof CANVAS_STAGES)[number]
  stage: TraceStage | undefined
  trace: RequestTrace
  index: number
}) {
  const lit = stage != null
  return (
    <div
      className={cn(
        "rounded-xl border bg-card p-4 transition-colors",
        lit ? "glow border-cyan/40" : "border-border opacity-60",
      )}
    >
      <div className="mb-1 flex items-center gap-2 text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
        <span className="rounded border border-input px-1.5 font-mono text-[10px] text-faint">{index + 1}</span>
        {def.title}
      </div>
      <p className="mb-3 min-h-8 text-[11.5px] leading-snug text-faint">{def.blurb}</p>
      <div className="space-y-1.5">
        {def.section === "Frontend" ? (
          <div className="chip k-flow">
            <span className="ic" />
            {trace.method} {trace.path}
          </div>
        ) : !lit ? (
          <div className="text-[11px] text-faint">Not executed.</div>
        ) : stage.policies.length === 0 ? (
          <div className="text-[11px] text-faint">
            {def.section === "Backend" ? "Forwarded to the backend." : "No policies ran."}
          </div>
        ) : (
          stage.policies.map((policy, i) => (
            <PolicyChip key={`${policy.name}-${i}`} name={policy.name} durationMs={policy.durationMs} />
          ))
        )}
      </div>
    </div>
  )
}

function PipelineCanvas({ trace }: { trace: RequestTrace }) {
  const onError = trace.stages.find((s) => s.section === "OnError")
  return (
    <div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {CANVAS_STAGES.map((def, i) => (
          <StageCard
            key={def.section}
            def={def}
            index={i}
            trace={trace}
            stage={trace.stages.find((s) => s.section === def.section)}
          />
        ))}
      </div>
      {onError ? (
        <div className="mt-3 rounded-xl border border-rose/40 bg-rose/5 p-4">
          <div className="mb-2 text-[11px] uppercase tracking-[0.16em] text-rose">on-error</div>
          <div className="space-y-1.5">
            {onError.policies.length === 0 ? (
              <div className="text-[11px] text-faint">Fault handled; no on-error policies.</div>
            ) : (
              onError.policies.map((policy, i) => (
                <PolicyChip key={`${policy.name}-${i}`} name={policy.name} durationMs={policy.durationMs} />
              ))
            )}
          </div>
        </div>
      ) : null}
    </div>
  )
}

function Bar({ segments }: { segments: { width: number; className: string }[] }) {
  return (
    <div className="flex h-2 overflow-hidden rounded-full bg-secondary">
      {segments.map((s, i) => (
        <span key={i} className={s.className} style={{ width: `${s.width}%` }} />
      ))}
    </div>
  )
}

// `scope` is the period the metric covers, shown as a small footer so each tile declares its own
// window (only the requests tile is a 60s rate; the rest are computed over the whole trace buffer).
function MetricTile({
  label,
  children,
  scope,
}: {
  label: string
  children: ReactNode
  scope?: string
}) {
  return (
    <div className="rounded-xl border border-border bg-card p-3">
      <div className="mb-2 text-[11px] uppercase tracking-[0.14em] text-faint">{label}</div>
      {children}
      {scope ? <div className="mt-1.5 text-[10px] tracking-[0.08em] text-faint">{scope}</div> : null}
    </div>
  )
}

function MetricsStrip({ metrics }: { metrics: TraceMetrics }) {
  const { total, last60s, p50, p95, status, rateLimited, cache } = metrics
  const pct = (n: number) => (total ? (n / total) * 100 : 0)
  const cacheTotal = cache ? cache.hits + cache.misses : 0

  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
      <MetricTile label="Requests · last 60s">
        <div className="font-mono text-2xl font-semibold">{last60s}</div>
        <div className="mt-1 text-[11px] text-faint">{total} in buffer</div>
      </MetricTile>

      <MetricTile label="Latency p95" scope={total ? `over last ${total}` : undefined}>
        <div className="font-mono text-2xl font-semibold">{p95 == null ? "-" : fmtMs(p95)}</div>
        <div className="mt-1 text-[11px] text-faint">p50 {p50 == null ? "-" : fmtMs(p50)}</div>
      </MetricTile>

      <MetricTile label="Status mix" scope={total ? `over last ${total}` : undefined}>
        <div className="mb-2 font-mono text-2xl font-semibold">
          {total ? `${Math.round(pct(status.c2xx))}%` : "-"}
          <span className="ml-1 text-xs text-faint">2xx</span>
        </div>
        <Bar
          segments={[
            { width: pct(status.c2xx), className: "bg-green" },
            { width: pct(status.c4xx), className: "bg-amber" },
            { width: pct(status.c5xx), className: "bg-rose" },
            { width: pct(status.other), className: "bg-faint" },
          ]}
        />
      </MetricTile>

      <MetricTile label="Rate-limited" scope={total ? `over last ${total}` : undefined}>
        <div className="mb-2 font-mono text-2xl font-semibold">
          {total ? `${Math.round(pct(rateLimited))}%` : "-"}
          <span className="ml-1 text-xs text-faint">429</span>
        </div>
        <Bar segments={[{ width: pct(rateLimited), className: "bg-amber" }]} />
      </MetricTile>

      <MetricTile label="Cache hit ratio">
        {cache == null ? (
          <div className="text-xs text-faint">No cached operations observed.</div>
        ) : (
          <>
            <div className="mb-2 font-mono text-2xl font-semibold">
              {Math.round((cache.hits / cacheTotal) * 100)}%
              <span className="ml-1 text-xs text-faint">
                {cache.hits}/{cacheTotal}
              </span>
            </div>
            <Bar segments={[{ width: (cache.hits / cacheTotal) * 100, className: "bg-cyan" }]} />
          </>
        )}
      </MetricTile>
    </div>
  )
}

function TraceDetail({ trace }: { trace: RequestTrace }) {
  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="flex items-center gap-2 font-mono text-sm">
        <StatusPill code={trace.statusCode} />
        <span className="font-semibold">{trace.method}</span>
        <span className="truncate text-muted-foreground">{trace.path}</span>
        <span className="ml-auto shrink-0 text-faint">{fmtMs(trace.durationMs)}</span>
      </div>
      <div className="mt-1 text-[11px] text-faint">
        {trace.apiName} · {trace.operationMethod} {trace.operationId} · {trace.outcome}
        {trace.subscriptionId ? ` · sub ${trace.subscriptionId}` : ""}
      </div>

      <div className="mt-4 space-y-3">
        {trace.stages.map((stage, si) => (
          <div key={`${stage.section}-${si}`} className="border-l border-input pl-3">
            <div className="flex items-baseline justify-between text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
              <span>{stage.section}</span>
              <span className="font-mono text-faint">{fmtMs(stage.durationMs)}</span>
            </div>
            {stage.policies.length > 0 ? (
              <div className="mt-1 space-y-0.5">
                {stage.policies.map((policy, pi) => (
                  <div key={`${policy.name}-${pi}`} className="flex justify-between font-mono text-xs">
                    <span className="text-cyan">{policy.name}</span>
                    <span className="text-faint">{fmtMs(policy.durationMs)}</span>
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        ))}
      </div>

      {trace.error ? (
        <div className="mt-4 rounded-lg border border-rose/40 bg-rose/5 p-3 font-mono text-xs text-rose">
          <div className="font-semibold">
            {trace.error.source} · {trace.error.reason}
          </div>
          <div className="mt-1 text-rose/80">{trace.error.message}</div>
        </div>
      ) : null}
    </div>
  )
}

function FeedRow({
  trace,
  active,
  onSelect,
}: {
  trace: RequestTrace
  active: boolean
  onSelect: () => void
}) {
  return (
    <button
      onClick={onSelect}
      aria-current={active ? "true" : undefined}
      aria-label={`HTTP ${trace.statusCode} ${trace.method} ${trace.path}, ${fmtMs(trace.durationMs)}`}
      className={cn(
        "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left font-mono text-xs transition-colors hover:bg-accent/40",
        active && "bg-accent/60",
      )}
    >
      <StatusPill code={trace.statusCode} />
      <span className="w-9 shrink-0 text-muted-foreground">{trace.method}</span>
      <span className="truncate text-muted-foreground">{trace.path}</span>
      <span className="ml-auto shrink-0 text-faint">{fmtMs(trace.durationMs)}</span>
    </button>
  )
}

const STATUS_DOT: Record<string, string> = {
  live: "bg-green pulse-dot",
  connecting: "bg-faint",
  reconnecting: "bg-amber",
  offline: "bg-rose",
}
const STATUS_LABEL: Record<string, string> = {
  live: "streaming",
  connecting: "connecting",
  reconnecting: "reconnecting",
  offline: "offline",
}

export function Live() {
  const { traces, status, selectedTrace, following, select, resumeLive } = useTraceFeed()

  // A slow ticker keeps the "last 60s" window honest while traffic is idle (no new traces to
  // trigger a recompute). App code, so Date.now() is fine here (only workflow scripts forbid it).
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 5000)
    return () => clearInterval(id)
  }, [])
  const metrics = useMemo(() => computeMetrics(traces, now), [traces, now])

  return (
    <>
      <div className="mb-6 flex items-start justify-between gap-4">
        <SurfaceHeader
          title="Live traffic"
          blurb="Watch requests stream across the Frontend, Inbound, Backend, and Outbound stages in real time."
        />
        <div className="flex shrink-0 items-center gap-3">
          {!following ? (
            <Button variant="outline" size="sm" onClick={resumeLive}>
              Resume live
            </Button>
          ) : null}
          <span className="flex items-center gap-2 text-[11px] uppercase tracking-[0.14em] text-faint">
            <span className={cn("size-2 rounded-full", STATUS_DOT[status])} />
            {following ? STATUS_LABEL[status] : "paused"}
          </span>
        </div>
      </div>

      <SurfaceState
        loading={status === "connecting" && traces.length === 0}
        error={
          status === "offline" && traces.length === 0
            ? "The live trace feed is unavailable. Start the gateway, then reload."
            : null
        }
        empty={traces.length === 0}
        emptyLabel="No traffic yet. Replay a request from the Playground, or send one to the gateway, to watch it stream here."
        onRetry={() => window.location.reload()}
      >
        <div className="grid gap-5 lg:grid-cols-[1fr_320px]">
          <div className="min-w-0 space-y-5">
            {selectedTrace ? <PipelineCanvas trace={selectedTrace} /> : null}
            <MetricsStrip metrics={metrics} />
            {selectedTrace ? <TraceDetail trace={selectedTrace} /> : null}
          </div>

          <aside className="min-w-0">
            <div className="mb-2 text-[11px] uppercase tracking-[0.14em] text-faint">
              Live feed · {traces.length}
            </div>
            <div className="max-h-[70vh] space-y-0.5 overflow-y-auto rounded-xl border border-border bg-card/50 p-1.5">
              {traces.map((trace) => (
                <FeedRow
                  key={trace.requestId}
                  trace={trace}
                  active={trace.requestId === selectedTrace?.requestId}
                  onSelect={() => select(trace.requestId)}
                />
              ))}
            </div>
          </aside>
        </div>
      </SurfaceState>
    </>
  )
}
