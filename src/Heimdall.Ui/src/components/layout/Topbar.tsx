import { Link } from "react-router-dom"
import { FlaskConical } from "lucide-react"
import { Button } from "@/components/ui/button"
import { SearchCommand } from "@/components/layout/SearchCommand"
import { useConfig } from "@/lib/use-config"
import { BUFFER_CAP, useTraceFeed } from "@/lib/use-trace-feed"
import { fmtRequestCount } from "@/lib/format"

// Full-width bar across the top: brand on the left (above the nav rail), gateway status in the
// middle, search + a quick Playground action on the right. The backend count is live from config;
// the request count is the live trace feed length, bounded by the buffer (shows "cap+" at the ceiling).
export function Topbar() {
  const { data } = useConfig()
  const { traces } = useTraceFeed()
  const backendCount = data?.backends.length ?? 0

  return (
    <header
      className="flex items-center gap-3 border-b border-border px-3 py-3 backdrop-blur-md sm:gap-4 sm:px-5 lg:gap-6"
      style={{ background: "linear-gradient(180deg, rgba(14,19,34,.7), rgba(6,8,15,.2))" }}
    >
      <div className="flex min-w-0 shrink-0 flex-col gap-1.5 sm:min-w-[200px]">
        <div className="wordmark text-[23px] leading-none">HEIMDALL</div>
        <div className="spectrum-rule" />
        <div className="pl-0.5 text-[10px] uppercase tracking-[0.32em] text-faint">
          API Management · local
        </div>
      </div>

      <div className="hidden items-center gap-2 rounded-full border border-input bg-[rgba(86,227,154,0.06)] px-3 py-1.5 text-[11.5px] uppercase tracking-[0.14em] text-[#bfe9d2] sm:flex">
        <span className="pulse-dot size-2 rounded-full bg-green shadow-[0_0_8px_var(--green)]" />
        Watching · :8080
      </div>

      <div className="hidden text-[11px] tracking-[0.06em] text-faint xl:block">
        <b className="font-semibold text-muted-foreground">{backendCount}</b>{" "}
        {backendCount === 1 ? "backend" : "backends"} ·{" "}
        <b className="font-semibold text-muted-foreground">{fmtRequestCount(traces.length, BUFFER_CAP)}</b>
      </div>

      <div className="flex-1" />

      <SearchCommand />

      <Button asChild className="btn-accent h-9 shrink-0 gap-2 rounded-[10px] font-semibold">
        <Link to="/playground">
          <FlaskConical className="size-[15px]" />
          <span className="hidden sm:inline">Playground</span>
        </Link>
      </Button>
    </header>
  )
}
