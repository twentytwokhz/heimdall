import { useEffect, useMemo, useRef, useState } from "react"
import { useNavigate } from "react-router-dom"
import { cn } from "@/lib/utils"
import { statusTone } from "@/lib/status-tone"
import {
  ApiError,
  getPolicySource,
  replayRequest,
  savePolicy,
  type PlaygroundResponse,
  type PolicyScope,
} from "@/lib/api"
import { buildScopeRef, deriveReplayRequest } from "@/lib/policy-authoring"
import { useConfig } from "@/lib/use-config"
import { useTraceFeed } from "@/lib/use-trace-feed"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { SurfaceState } from "@/components/data/SurfaceState"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"

const SCOPES: { value: PolicyScope; label: string }[] = [
  { value: "global", label: "Global" },
  { value: "api", label: "API" },
  { value: "operation", label: "Operation" },
  { value: "product", label: "Product" },
]

const selectClass =
  "h-9 rounded-md border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30"

export function Authoring() {
  const { data: config, loading, error, reload } = useConfig()
  const navigate = useNavigate()
  const { select } = useTraceFeed()

  const [scope, setScope] = useState<PolicyScope>("global")
  const [apiId, setApiId] = useState("")
  const [operationId, setOperationId] = useState("")
  const [productId, setProductId] = useState("")

  const [xml, setXml] = useState("")
  const [loadingSource, setLoadingSource] = useState(false)
  const [sourceError, setSourceError] = useState<string | null>(null)

  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const [replayKey, setReplayKey] = useState("")
  const [replaying, setReplaying] = useState(false)
  const [replayError, setReplayError] = useState<string | null>(null)
  const [response, setResponse] = useState<PlaygroundResponse | null>(null)

  const apis = useMemo(() => config?.apis ?? [], [config])
  const products = useMemo(() => config?.products ?? [], [config])
  // Every operation across all APIs, as replay candidates: a save can be tested against any of them.
  const allOps = useMemo(
    () => apis.flatMap((a) => a.operations.map((op) => ({ api: a, op }))),
    [apis],
  )

  // Effective selections: the raw picker state, defaulted to the first available item so no dropdown
  // sits empty. Derived in render (not synced through effects) so they cannot drift or cascade renders.
  const effectiveApiId = apiId || apis[0]?.id || ""
  const selectedApi = apis.find((a) => a.id === effectiveApiId)
  const effectiveOperationId = selectedApi?.operations.some((o) => o.id === operationId)
    ? operationId
    : (selectedApi?.operations[0]?.id ?? "")
  const effectiveProductId = productId || products[0]?.id || ""
  const defaultReplayKey =
    scope === "operation" && effectiveApiId && effectiveOperationId
      ? `${effectiveApiId}::${effectiveOperationId}`
      : allOps[0]
        ? `${allOps[0].api.id}::${allOps[0].op.id}`
        : ""
  const effectiveReplayKey = replayKey || defaultReplayKey

  // The scope ref to read/save, or null while a required id is still unavailable (e.g. no APIs loaded).
  const ref = buildScopeRef(scope, effectiveApiId, effectiveOperationId, effectiveProductId)

  // One controller for the in-flight source load / save / replay; aborted on unmount.
  const inflight = useRef<AbortController | null>(null)
  useEffect(() => () => inflight.current?.abort(), [])

  // Load the scope's current source whenever the selection changes (depends on the scope/id primitives,
  // not the rebuilt ref object, so it does not re-run every render). Editor edits are owned by `xml`
  // from here on, so a later config reload() (after save) does not clobber what the user is typing.
  useEffect(() => {
    const target = buildScopeRef(scope, effectiveApiId, effectiveOperationId, effectiveProductId)
    if (!target) return
    inflight.current?.abort()
    const ac = new AbortController()
    inflight.current = ac
    setLoadingSource(true)
    setSourceError(null)
    setSaved(false)
    setSaveError(null)
    setResponse(null)
    getPolicySource(target, ac.signal)
      .then((src) => {
        setXml(src.xml)
        setLoadingSource(false)
      })
      .catch((e: unknown) => {
        if (ac.signal.aborted) return
        setSourceError(e instanceof ApiError ? e.message : "Failed to load the policy.")
        setLoadingSource(false)
      })
  }, [scope, effectiveApiId, effectiveOperationId, effectiveProductId])

  const handleSave = async () => {
    if (!ref) return
    inflight.current?.abort()
    const ac = new AbortController()
    inflight.current = ac
    setSaving(true)
    setSaveError(null)
    setSaved(false)
    try {
      await savePolicy(ref, xml, ac.signal)
      setSaved(true)
      // Refresh the config snapshot so hasPolicy/badges reflect the hot-swap. This is read-only and
      // runs on ConfigProvider's own controller (not `inflight`); it can never clobber the editor's
      // `xml` (owned here, never re-sourced on a reload), so the brief overlap is benign by design.
      reload()
    } catch (e) {
      if (ac.signal.aborted) return
      setSaveError(e instanceof ApiError ? e.message : "Save failed.")
    } finally {
      if (!ac.signal.aborted) setSaving(false)
    }
  }

  const handleReplay = async () => {
    const target = allOps.find(({ api, op }) => `${api.id}::${op.id}` === effectiveReplayKey)
    if (!target) return
    inflight.current?.abort()
    const ac = new AbortController()
    inflight.current = ac
    setReplaying(true)
    setReplayError(null)
    try {
      setResponse(await replayRequest(deriveReplayRequest(target.api, target.op), ac.signal))
    } catch (e) {
      if (ac.signal.aborted) return
      setReplayError(e instanceof ApiError ? e.message : "Replay failed.")
    } finally {
      if (!ac.signal.aborted) setReplaying(false)
    }
  }

  const viewInLive = () => {
    if (!response) return
    select(response.requestId)
    navigate("/live")
  }

  return (
    <>
      <SurfaceHeader
        title="Policy authoring"
        blurb="Edit a policy's XML and save to hot-reload it into the live gateway, then replay a request to see the change."
      />

      <SurfaceState loading={loading} error={error} empty={false} onRetry={reload}>
        <div className="space-y-5">
          {/* Scope picker: which policy to edit, with the ids that scope needs. */}
          <div className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4">
            <label className="flex flex-col gap-1 text-xs">
              <span className="text-faint">Scope</span>
              <select
                value={scope}
                aria-label="Policy scope"
                className={selectClass}
                onChange={(e) => setScope(e.target.value as PolicyScope)}
              >
                {SCOPES.map((s) => (
                  <option key={s.value} value={s.value}>
                    {s.label}
                  </option>
                ))}
              </select>
            </label>

            {(scope === "api" || scope === "operation") && (
              <label className="flex flex-col gap-1 text-xs">
                <span className="text-faint">API</span>
                <select
                  value={effectiveApiId}
                  aria-label="API"
                  className={selectClass}
                  onChange={(e) => {
                    // Reset the operation so it cannot carry over to a different API's selection.
                    setApiId(e.target.value)
                    setOperationId("")
                  }}
                >
                  {apis.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.displayName}
                    </option>
                  ))}
                </select>
              </label>
            )}

            {scope === "operation" && (
              <label className="flex flex-col gap-1 text-xs">
                <span className="text-faint">Operation</span>
                <select
                  value={effectiveOperationId}
                  aria-label="Operation"
                  className={selectClass}
                  onChange={(e) => setOperationId(e.target.value)}
                >
                  {(selectedApi?.operations ?? []).map((o) => (
                    <option key={o.id} value={o.id}>
                      {o.method} {o.uriTemplate}
                    </option>
                  ))}
                </select>
              </label>
            )}

            {scope === "product" && (
              <label className="flex flex-col gap-1 text-xs">
                <span className="text-faint">Product</span>
                <select
                  value={effectiveProductId}
                  aria-label="Product"
                  className={selectClass}
                  onChange={(e) => setProductId(e.target.value)}
                >
                  {products.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.displayName}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>

          {/* Editor + save. Validation errors are shown loudly; they never fail silently. */}
          <div className="space-y-3 rounded-xl border border-border bg-card p-4">
            <div className="flex items-center justify-between">
              <span className="text-[11px] uppercase tracking-[0.14em] text-faint">Policy XML</span>
              <div className="flex items-center gap-3">
                {saved ? <span className="text-xs text-green">Saved and hot-reloaded.</span> : null}
                <Button disabled={saving || loadingSource || !ref} onClick={handleSave}>
                  {saving ? "Saving..." : "Save & hot-reload"}
                </Button>
              </div>
            </div>
            <Textarea
              value={xml}
              aria-label="Policy XML"
              spellCheck={false}
              className="min-h-72 font-mono text-xs"
              onChange={(e) => {
                setXml(e.target.value)
                setSaved(false)
              }}
            />
            <p className="text-xs text-muted-foreground">
              Save validates the XML and that every policy is supported, then swaps it into the live
              gateway in memory (no file is written; a restart resets to the on-disk config). Attribute
              and expression errors are not checked here - they surface on the request trace in Live.
            </p>
            {sourceError ? (
              <div className="rounded-lg border border-rose/40 bg-rose/5 p-3 font-mono text-xs text-rose">
                {sourceError}
              </div>
            ) : null}
            {saveError ? (
              <div className="rounded-lg border border-rose/40 bg-rose/5 p-3 font-mono text-xs text-rose">
                {saveError}
              </div>
            ) : null}
          </div>

          {/* Replay a request through the gateway to see the saved policy take effect. */}
          {allOps.length > 0 ? (
            <div className="space-y-3 rounded-xl border border-border bg-card p-4">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-[11px] uppercase tracking-[0.14em] text-faint">Replay</span>
                <select
                  value={effectiveReplayKey}
                  aria-label="Request to replay"
                  className={cn(selectClass, "font-mono text-xs")}
                  onChange={(e) => setReplayKey(e.target.value)}
                >
                  {allOps.map(({ api, op }) => {
                    const key = `${api.id}::${op.id}`
                    return (
                      <option key={key} value={key}>
                        {op.method} {api.path}
                        {op.uriTemplate}
                      </option>
                    )
                  })}
                </select>
                <Button
                  variant="outline"
                  className="ml-auto"
                  disabled={replaying || !effectiveReplayKey}
                  onClick={handleReplay}
                >
                  {replaying ? "Replaying..." : "Replay"}
                </Button>
              </div>

              {replayError ? (
                <div className="rounded-lg border border-rose/40 bg-rose/5 p-3 font-mono text-xs text-rose">
                  {replayError}
                </div>
              ) : null}

              {response ? (
                <div className="space-y-3">
                  <div className="flex items-center gap-2">
                    <span
                      className={cn(
                        "rounded border px-1.5 py-0.5 font-mono text-[11px] tabular-nums",
                        statusTone(response.statusCode),
                      )}
                    >
                      {response.statusCode}
                    </span>
                    <span className="text-[11px] uppercase tracking-[0.14em] text-faint">Response</span>
                    <Button variant="outline" size="sm" className="ml-auto" onClick={viewInLive}>
                      View in Live
                    </Button>
                  </div>
                  <pre
                    aria-label="Response body"
                    className="max-h-72 overflow-auto rounded-lg border border-border bg-background/50 p-3 font-mono text-xs"
                  >
                    {response.body && response.body.length > 0 ? response.body : "(empty body)"}
                  </pre>
                </div>
              ) : null}
            </div>
          ) : null}
        </div>
      </SurfaceState>
    </>
  )
}
