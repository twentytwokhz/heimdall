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

export function Products() {
  const { data, error, loading, reload } = useConfig()
  const products = data?.products ?? []

  return (
    <>
      <SurfaceHeader
        title="Products"
        blurb="Products that group APIs and gate access through subscriptions."
        count={data ? products.length : null}
      />
      <SurfaceState
        loading={loading}
        error={error}
        empty={products.length === 0}
        emptyLabel="No products are loaded."
        onRetry={reload}
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Product</TableHead>
              <TableHead>Subscription</TableHead>
              <TableHead>APIs</TableHead>
              <TableHead>Policy</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {products.map((p) => (
              <TableRow key={p.id}>
                <TableCell>
                  <div className="font-medium">{p.displayName}</div>
                  <div className="font-mono text-xs text-faint">{p.id}</div>
                </TableCell>
                <TableCell>
                  {p.requiresSubscription ? (
                    <Badge variant="secondary">required</Badge>
                  ) : (
                    <span className="text-muted-foreground">open</span>
                  )}
                </TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {p.apiIds.length ? p.apiIds.join(", ") : "-"}
                </TableCell>
                <TableCell>
                  {p.hasPolicy ? (
                    <Badge variant="outline">policy</Badge>
                  ) : (
                    <span className="text-faint">-</span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </SurfaceState>
    </>
  )
}
