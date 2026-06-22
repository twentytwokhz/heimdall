import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react"
import { getConfig, type ConfigView } from "@/lib/api"

// The loaded config is one payload that feeds every explorer surface, so it is fetched once here and
// shared through context rather than refetched per surface. reload() is exposed now (it is cheap)
// so the later policy hot-reload task can refresh the snapshot without new plumbing.
type ConfigState = {
  data: ConfigView | null
  error: string | null
  loading: boolean
  reload: () => void
}

const ConfigContext = createContext<ConfigState | null>(null)

export function ConfigProvider({ children }: { children: ReactNode }) {
  const [data, setData] = useState<ConfigView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback((signal?: AbortSignal) => {
    setLoading(true)
    setError(null)
    getConfig(signal)
      .then((cfg) => {
        setData(cfg)
        setLoading(false)
      })
      .catch((err: unknown) => {
        if (signal?.aborted) return
        setError(err instanceof Error ? err.message : "Failed to load config.")
        setLoading(false)
      })
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal)
    return () => controller.abort()
  }, [load])

  // Stable, no-arg reload: discards any event arg (it is wired to onClick handlers) so it never
  // leaks a MouseEvent into load()'s signal parameter, while keeping a stable reference for consumers.
  const reload = useCallback(() => load(), [load])

  return (
    <ConfigContext.Provider value={{ data, error, loading, reload }}>
      {children}
    </ConfigContext.Provider>
  )
}

export function useConfig(): ConfigState {
  const ctx = useContext(ConfigContext)
  if (!ctx) {
    throw new Error("useConfig must be used within a ConfigProvider")
  }
  return ctx
}
