import { useEffect, useMemo, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { Shield } from "lucide-react"
import { cn } from "@/lib/utils"
import { useConfig } from "@/lib/use-config"
import { useTraceFeed } from "@/lib/use-trace-feed"
import { computeMetrics } from "@/lib/trace-metrics"
import { statusTone } from "@/lib/status-tone"
import { fmtMs } from "@/lib/format"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { navItems } from "@/nav"

// A count tile linking to its surface. Real numbers come from the loaded config.
function CountTile({ label, value, to }: { label: string; value: number; to: string }) {
  const item = navItems.find((i) => i.path === to)
  const Icon = item?.icon
  return (
    <Link
      to={to}
      className="glow flex flex-col gap-2 rounded-xl border border-border bg-card p-4 transition-colors hover:border-input"
    >
      <div className="flex items-center justify-between text-faint">
        <span className="text-[11px] uppercase tracking-[0.14em]">{label}</span>
        {Icon ? <Icon className="size-4" /> : null}
      </div>
      <span className="font-mono text-3xl font-semibold text-foreground">{value}</span>
    </Link>
  )
}

// A compact stat for the recent-activity strip. `sub` states the period (only the requests stat is a
// 60s rate; the rest are over the whole trace buffer), so the tiles don't read as contradictory.
function Stat({ label, value, sub }: { label: string; value: string; sub: string }) {
  return (
    <div className="rounded-lg border border-border bg-background/40 p-2.5">
      <div className="text-[10px] uppercase tracking-[0.14em] text-faint">{label}</div>
      <div className="mt-1 font-mono text-lg font-semibold">{value}</div>
      <div className="mt-0.5 text-[10px] text-faint">{sub}</div>
    </div>
  )
}

// Recent activity, driven by the app-wide trace feed (same SignalR buffer the Live canvas uses).
// A summary line plus the five newest requests; each row opens that trace pinned in Live. The 60s
// window is kept honest by a slow ticker while traffic is idle (app code, so Date.now() is fine).
function RecentActivity() {
  const navigate = useNavigate()
  const { traces, select } = useTraceFeed()
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 5000)
    return () => clearInterval(id)
  }, [])
  const metrics = useMemo(() => computeMetrics(traces, now), [traces, now])
  const pct = (n: number) => (metrics.total ? Math.round((n / metrics.total) * 100) : 0)

  const open = (requestId: string) => {
    select(requestId)
    navigate("/live")
  }

  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="mb-3 flex items-center justify-between">
        <span className="text-[11px] uppercase tracking-[0.14em] text-faint">Recent activity</span>
        <Link to="/live" className="text-xs text-primary underline-offset-4 hover:underline">
          View live →
        </Link>
      </div>

      {traces.length === 0 ? (
        <div className="py-6 text-center text-sm text-muted-foreground">
          No requests yet. Replay one from the Playground, or send one to the gateway, to see it here.
        </div>
      ) : (
        <>
          <div className="mb-3 grid grid-cols-2 gap-2.5 sm:grid-cols-4">
            <Stat label="Requests · 60s" value={String(metrics.last60s)} sub={`${metrics.total} buffered`} />
            <Stat
              label="Latency p95"
              value={metrics.p95 == null ? "-" : fmtMs(metrics.p95)}
              sub={`over last ${metrics.total}`}
            />
            <Stat
              label="Errors"
              value={`${pct(metrics.status.c4xx + metrics.status.c5xx)}%`}
              sub={`over last ${metrics.total}`}
            />
            <Stat
              label="Rate-limited"
              value={`${pct(metrics.rateLimited)}%`}
              sub={`over last ${metrics.total}`}
            />
          </div>
          <div className="space-y-0.5">
            {traces.slice(0, 5).map((t) => (
              <button
                key={t.requestId}
                onClick={() => open(t.requestId)}
                aria-label={`HTTP ${t.statusCode} ${t.method} ${t.path}, open in Live`}
                className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left font-mono text-xs transition-colors hover:bg-accent/40"
              >
                <span
                  className={cn(
                    "rounded border px-1.5 py-0.5 text-[11px] tabular-nums",
                    statusTone(t.statusCode),
                  )}
                >
                  {t.statusCode}
                </span>
                <span className="w-9 shrink-0 text-muted-foreground">{t.method}</span>
                <span className="truncate text-muted-foreground">{t.path}</span>
                <span className="ml-auto shrink-0 text-faint">{fmtMs(t.durationMs)}</span>
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

export function Overview() {
  const { data, error, loading, reload } = useConfig()
  const [healthy, setHealthy] = useState<boolean | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetch("/health", { signal: controller.signal })
      .then((res) => setHealthy(res.ok))
      .catch(() => {
        if (!controller.signal.aborted) setHealthy(false)
      })
    return () => controller.abort()
  }, [])

  return (
    <>
      <SurfaceHeader
        title="Overview"
        blurb="Gateway health and the configuration Heimdall has loaded, at a glance."
      />

      {error ? (
        <div className="rounded-xl border border-destructive/30 bg-destructive/5 px-6 py-10 text-center">
          <p className="text-sm font-medium">Could not load the gateway config.</p>
          <p className="mt-1 font-mono text-xs text-muted-foreground">{error}</p>
          <button className="mt-4 text-sm text-primary underline-offset-4 hover:underline" onClick={reload}>
            Retry
          </button>
        </div>
      ) : (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-2 rounded-full border border-input bg-[rgba(86,227,154,0.06)] px-3 py-1.5 text-[11.5px] uppercase tracking-[0.14em] text-[#bfe9d2]">
              <span
                className={`size-2 rounded-full ${
                  healthy ? "pulse-dot bg-green shadow-[0_0_8px_var(--green)]" : "bg-faint"
                }`}
              />
              {healthy == null ? "Checking" : healthy ? "Gateway healthy" : "Gateway unreachable"}
            </div>
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Shield className="size-4" />
              Global policy:{" "}
              <b className="font-semibold text-foreground">
                {loading ? "..." : data?.hasGlobalPolicy ? "present" : "none"}
              </b>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            <CountTile label="APIs" value={data?.apis.length ?? 0} to="/apis" />
            <CountTile label="Products" value={data?.products.length ?? 0} to="/products" />
            <CountTile label="Subscriptions" value={data?.subscriptions.length ?? 0} to="/subscriptions" />
            <CountTile label="Named values" value={data?.namedValues.length ?? 0} to="/named-values" />
            <CountTile label="Backends" value={data?.backends.length ?? 0} to="/backends" />
            <CountTile label="Fragments" value={data?.fragments.length ?? 0} to="/fragments" />
          </div>

          <RecentActivity />
        </div>
      )}
    </>
  )
}
