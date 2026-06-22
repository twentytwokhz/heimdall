import { useConfig } from "@/lib/use-config"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { SurfaceState } from "@/components/data/SurfaceState"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"

export function Backends() {
  const { data, error, loading, reload } = useConfig()
  const backends = data?.backends ?? []

  return (
    <>
      <SurfaceHeader
        title="Backends"
        blurb="Backend services that operations forward to."
        count={data ? backends.length : null}
      />
      <SurfaceState
        loading={loading}
        error={error}
        empty={backends.length === 0}
        emptyLabel="No backends are loaded."
        onRetry={reload}
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Backend</TableHead>
              <TableHead>URL</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {backends.map((b) => (
              <TableRow key={b.id}>
                <TableCell className="font-mono">{b.id}</TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">{b.url}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </SurfaceState>
    </>
  )
}
