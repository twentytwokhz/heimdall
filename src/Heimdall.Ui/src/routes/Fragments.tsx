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

// The config exposes fragment names only (the bodies are spliced into effective policies at build
// time); the effective-policy drill-down on the APIs surface shows where they land.
export function Fragments() {
  const { data, error, loading, reload } = useConfig()
  const fragments = data?.fragments ?? []

  return (
    <>
      <SurfaceHeader
        title="Policy fragments"
        blurb="Shared policy fragments included across APIs and operations."
        count={data ? fragments.length : null}
      />
      <SurfaceState
        loading={loading}
        error={error}
        empty={fragments.length === 0}
        emptyLabel="No policy fragments are loaded."
        onRetry={reload}
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fragment</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {fragments.map((name) => (
              <TableRow key={name}>
                <TableCell className="font-mono">{name}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </SurfaceState>
    </>
  )
}
