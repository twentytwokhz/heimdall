import { useConfig } from "@/lib/use-config"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { SurfaceState } from "@/components/data/SurfaceState"
import { Badge } from "@/components/ui/badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"

// Keys are never shown: the SubscriptionView DTO drops primary/secondary keys server-side, so the
// console cannot leak them regardless of what is rendered here.
export function Subscriptions() {
  const { data, error, loading, reload } = useConfig()
  const subscriptions = data?.subscriptions ?? []

  return (
    <>
      <SurfaceHeader
        title="Subscriptions"
        blurb="Subscription keys and the products they unlock. Keys are never exposed by the console."
        count={data ? subscriptions.length : null}
      />
      <SurfaceState
        loading={loading}
        error={error}
        empty={subscriptions.length === 0}
        emptyLabel="No subscriptions are loaded."
        onRetry={reload}
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Subscription</TableHead>
              <TableHead>Scope</TableHead>
              <TableHead>Target</TableHead>
              <TableHead>State</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {subscriptions.map((s) => (
              <TableRow key={s.id}>
                <TableCell>
                  <div className="font-medium">{s.displayName ?? s.id}</div>
                  {s.displayName ? (
                    <div className="font-mono text-xs text-faint">{s.id}</div>
                  ) : null}
                </TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">{s.scope}</TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {s.productId ?? s.apiId ?? "-"}
                </TableCell>
                <TableCell>
                  <Badge variant={s.state === "Active" ? "secondary" : "outline"}>{s.state}</Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </SurfaceState>
    </>
  )
}
