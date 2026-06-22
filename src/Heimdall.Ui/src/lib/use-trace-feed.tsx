import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react"
import { HubConnectionBuilder } from "@microsoft/signalr"
import { getRecentTraces, type RequestTrace } from "@/lib/api"

// The live trace feed: one app-wide SignalR connection to /_apim/hub/traces, shared through context
// like ConfigProvider. A single socket (opened once at the app root) feeds every surface, so the
// buffer is already warm when the Live canvas mounts. The hub pushes one "trace" per request; on
// connect and after a reconnect we also backfill GET /_apim/traces so the feed is never empty.

// Match the server's trace ring buffer so the client view and the backfill agree on depth.
export const BUFFER_CAP = 200

type FeedStatus = "connecting" | "live" | "reconnecting" | "offline"

type TraceFeedState = {
  traces: RequestTrace[]
  status: FeedStatus
  /** The trace the canvas/detail reflect: the newest while following, or the pinned one. */
  selectedTrace: RequestTrace | null
  /** True while auto-following the newest trace; false once the user pins a row. */
  following: boolean
  /** Pin a specific trace (pauses auto-follow). */
  select: (requestId: string) => void
  /** Resume auto-following the newest trace. */
  resumeLive: () => void
}

const TraceFeedContext = createContext<TraceFeedState | null>(null)

// Merge trace lists newest-first, de-duplicated by requestId (streamed pushes and a reconnect
// backfill can overlap), capped to the buffer depth. Sorting by timestamp is robust to arrival order.
function merge(incoming: RequestTrace[], existing: RequestTrace[]): RequestTrace[] {
  const byId = new Map<string, RequestTrace>()
  for (const trace of existing) byId.set(trace.requestId, trace)
  for (const trace of incoming) byId.set(trace.requestId, trace)
  return [...byId.values()]
    .sort((a, b) => (Date.parse(b.timestamp) || 0) - (Date.parse(a.timestamp) || 0))
    .slice(0, BUFFER_CAP)
}

export function TraceFeedProvider({ children }: { children: ReactNode }) {
  const [traces, setTraces] = useState<RequestTrace[]>([])
  const [status, setStatus] = useState<FeedStatus>("connecting")
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [following, setFollowing] = useState(true)

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/_apim/hub/traces")
      .withAutomaticReconnect()
      .build()
    let disposed = false
    let backfillController: AbortController | null = null

    // Backfill the feed from REST. Aborts any in-flight backfill first, so a reconnect storm cannot
    // pile up concurrent fetches that each re-render; the controller is also cancelled on cleanup.
    const backfill = async () => {
      backfillController?.abort()
      backfillController = new AbortController()
      try {
        const recent = await getRecentTraces(BUFFER_CAP, backfillController.signal)
        if (!disposed) setTraces((prev) => merge(prev, recent))
      } catch {
        // Non-fatal: aborted (unmount/superseded) or the gateway is unreachable; the stream fills it.
      }
    }

    connection.on("trace", (trace: RequestTrace) => {
      if (!disposed) setTraces((prev) => merge([trace], prev))
    })
    connection.onreconnecting(() => {
      if (!disposed) setStatus("reconnecting")
    })
    connection.onreconnected(() => {
      if (disposed) return
      setStatus("live")
      void backfill()
    })
    connection.onclose(() => {
      if (!disposed) setStatus("offline")
    })

    connection
      .start()
      .then(() => {
        if (disposed) return
        setStatus("live")
        return backfill()
      })
      .catch(() => {
        // Initial connect failed (auto-reconnect only arms after a first success), so retry is manual:
        // the user reloads once the gateway is up. Surface it as offline rather than a silent hang.
        if (!disposed) setStatus("offline")
      })

    return () => {
      disposed = true
      backfillController?.abort()
      void connection.stop()
    }
  }, [])

  const select = useCallback((requestId: string) => {
    setSelectedId(requestId)
    setFollowing(false)
  }, [])

  const resumeLive = useCallback(() => {
    setFollowing(true)
    setSelectedId(null)
  }, [])

  // While following, the newest trace drives the view; once pinned, the chosen trace does (falling
  // back to newest if it has aged out of the buffer).
  const selectedTrace = useMemo(() => {
    if (following) return traces[0] ?? null
    return traces.find((t) => t.requestId === selectedId) ?? traces[0] ?? null
  }, [following, selectedId, traces])

  const value = useMemo<TraceFeedState>(
    () => ({ traces, status, selectedTrace, following, select, resumeLive }),
    [traces, status, selectedTrace, following, select, resumeLive],
  )

  return <TraceFeedContext.Provider value={value}>{children}</TraceFeedContext.Provider>
}

export function useTraceFeed(): TraceFeedState {
  const ctx = useContext(TraceFeedContext)
  if (!ctx) {
    throw new Error("useTraceFeed must be used within a TraceFeedProvider")
  }
  return ctx
}
