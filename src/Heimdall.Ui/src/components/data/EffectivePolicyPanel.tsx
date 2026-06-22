import { useEffect, useState } from "react"
import { Skeleton } from "@/components/ui/skeleton"
import { Badge } from "@/components/ui/badge"
import {
  ApiError,
  getEffectivePolicy,
  type EffectivePolicy,
  type PolicyNode,
} from "@/lib/api"

// One policy element and its children, rendered as an indented tree that stays faithful to the
// parsed node: tag name, attributes, inner text, nested children.
function PolicyNodeRow({ node, depth }: { node: PolicyNode; depth: number }) {
  const attrs = Object.entries(node.attributes)
  return (
    <div style={{ paddingLeft: depth * 14 }} className="py-0.5">
      <span className="font-mono text-[12.5px] text-foreground/90">
        <span className="text-cyan">{node.name}</span>
        {attrs.map(([k, v]) => (
          <span key={k} className="text-muted-foreground">
            {" "}
            {k}=<span className="text-foreground">"{v}"</span>
          </span>
        ))}
      </span>
      {node.rawText?.trim() ? (
        <span className="ml-2 font-mono text-[12px] text-faint">{node.rawText.trim()}</span>
      ) : null}
      {node.children.map((child, i) => (
        <PolicyNodeRow key={`${child.name}-${i}`} node={child} depth={depth + 1} />
      ))}
    </div>
  )
}

function Section({ label, nodes }: { label: string; nodes: PolicyNode[] }) {
  return (
    <div>
      <div className="mb-1 text-[11px] uppercase tracking-[0.14em] text-faint">{label}</div>
      {nodes.length ? (
        nodes.map((n, i) => <PolicyNodeRow key={`${n.name}-${i}`} node={n} depth={0} />)
      ) : (
        <div className="font-mono text-[12px] text-faint">(empty)</div>
      )}
    </div>
  )
}

// The flattened effective policy for one operation, fetched lazily when an operation is selected.
// A 404 means the api/operation id is unknown to the gateway; any other failure shows its message.
export function EffectivePolicyPanel({ apiId, operationId }: { apiId: string; operationId: string }) {
  const [policy, setPolicy] = useState<EffectivePolicy | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(null)
    setPolicy(null)
    getEffectivePolicy(apiId, operationId, controller.signal)
      .then((p) => {
        setPolicy(p)
        setLoading(false)
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        setError(
          err instanceof ApiError && err.status === 404
            ? "No effective policy for this operation."
            : err instanceof Error
              ? err.message
              : "Failed to load the effective policy.",
        )
        setLoading(false)
      })
    return () => controller.abort()
  }, [apiId, operationId])

  if (loading) return <Skeleton className="h-24 w-full" />
  if (error) return <div className="font-mono text-xs text-muted-foreground">{error}</div>
  if (!policy) return null

  const sections: [string, PolicyNode[]][] = [
    ["Inbound", policy.inbound],
    ["Backend", policy.backend],
    ["Outbound", policy.outbound],
    ["On error", policy.onError],
  ]
  const empty = sections.every(([, nodes]) => nodes.length === 0)

  return (
    <div className="space-y-4 rounded-lg border border-border bg-[rgba(8,11,20,0.5)] p-4">
      {empty ? (
        <div className="font-mono text-xs text-faint">No policy applies to this operation.</div>
      ) : (
        sections.map(([label, nodes]) => <Section key={label} label={label} nodes={nodes} />)
      )}
      {policy.requiresBodyBuffering ? (
        <Badge variant="outline">requires body buffering</Badge>
      ) : null}
    </div>
  )
}
