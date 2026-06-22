import { Fragment, useState } from "react"
import { ChevronDown, ChevronRight } from "lucide-react"
import { cn } from "@/lib/utils"
import { useConfig } from "@/lib/use-config"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { SurfaceState } from "@/components/data/SurfaceState"
import { EffectivePolicyPanel } from "@/components/data/EffectivePolicyPanel"
import { Badge } from "@/components/ui/badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"

// APIs drill down two levels: expand a row to its operations, then select an operation to fetch and
// show its effective policy (global + api + op scopes flattened by the gateway).
export function Apis() {
  const { data, error, loading, reload } = useConfig()
  const apis = data?.apis ?? []
  const [expanded, setExpanded] = useState<string | null>(null)
  const [selected, setSelected] = useState<{ apiId: string; operationId: string } | null>(null)

  const toggle = (apiId: string) => {
    setExpanded((cur) => (cur === apiId ? null : apiId))
    setSelected(null)
  }

  return (
    <>
      <SurfaceHeader
        title="APIs"
        blurb="Browse loaded APIs, their operations, and the effective policy on each operation."
        count={data ? apis.length : null}
      />
      <SurfaceState
        loading={loading}
        error={error}
        empty={apis.length === 0}
        emptyLabel="No APIs are loaded."
        onRetry={reload}
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-8" />
              <TableHead>API</TableHead>
              <TableHead>Path</TableHead>
              <TableHead>Subscription</TableHead>
              <TableHead>Operations</TableHead>
              <TableHead>Products</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {apis.map((api) => {
              const open = expanded === api.id
              return (
                <Fragment key={api.id}>
                  <TableRow
                    className="cursor-pointer"
                    data-state={open ? "selected" : undefined}
                    role="button"
                    tabIndex={0}
                    aria-expanded={open}
                    onClick={() => toggle(api.id)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault()
                        toggle(api.id)
                      }
                    }}
                  >
                    <TableCell>
                      {open ? (
                        <ChevronDown className="size-4 text-muted-foreground" />
                      ) : (
                        <ChevronRight className="size-4 text-muted-foreground" />
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="font-medium">{api.displayName}</div>
                      <div className="font-mono text-xs text-faint">{api.id}</div>
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">{api.path}</TableCell>
                    <TableCell>
                      {api.subscriptionRequired ? (
                        <Badge variant="secondary">required</Badge>
                      ) : (
                        <span className="text-muted-foreground">open</span>
                      )}
                    </TableCell>
                    <TableCell className="text-muted-foreground">{api.operations.length}</TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {api.productIds.length ? api.productIds.join(", ") : "-"}
                    </TableCell>
                  </TableRow>

                  {open ? (
                    <TableRow className="hover:bg-transparent">
                      <TableCell colSpan={6} className="bg-card/40">
                        {api.serviceUrl ? (
                          <div className="mb-3 font-mono text-xs text-faint">
                            backend {api.serviceUrl}
                          </div>
                        ) : null}
                        <div className="space-y-1">
                          {api.operations.map((op) => {
                            const active =
                              selected?.apiId === api.id && selected.operationId === op.id
                            return (
                              <div key={op.id}>
                                <button
                                  className={cn(
                                    "flex w-full items-center gap-3 rounded-md px-2 py-1.5 text-left transition-colors hover:bg-accent/40",
                                    active && "bg-accent/60",
                                  )}
                                  onClick={() =>
                                    setSelected(
                                      active ? null : { apiId: api.id, operationId: op.id },
                                    )
                                  }
                                >
                                  <Badge variant="outline" className="font-mono">
                                    {op.method}
                                  </Badge>
                                  <span className="font-mono text-xs text-muted-foreground">
                                    {op.uriTemplate}
                                  </span>
                                  <span className="ml-auto font-mono text-[11px] text-faint">
                                    {op.id}
                                  </span>
                                </button>
                                {active ? (
                                  <div className="mt-2 mb-3 pl-2">
                                    <EffectivePolicyPanel apiId={api.id} operationId={op.id} />
                                  </div>
                                ) : null}
                              </div>
                            )
                          })}
                          {api.operations.length === 0 ? (
                            <div className="px-2 py-1 text-xs text-faint">No operations.</div>
                          ) : null}
                        </div>
                      </TableCell>
                    </TableRow>
                  ) : null}
                </Fragment>
              )
            })}
          </TableBody>
        </Table>
      </SurfaceState>
    </>
  )
}
