import { Lock } from "lucide-react"
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

// Secret values arrive already masked to "***" from the server (NamedValueView.From); the UI only
// labels them. The value is always present so the console can show that a value exists.
export function NamedValues() {
  const { data, error, loading, reload } = useConfig()
  const namedValues = data?.namedValues ?? []

  return (
    <>
      <SurfaceHeader
        title="Named values"
        blurb="Reusable named values and secrets referenced by policies. Secret values are masked."
        count={data ? namedValues.length : null}
      />
      <SurfaceState
        loading={loading}
        error={error}
        empty={namedValues.length === 0}
        emptyLabel="No named values are loaded."
        onRetry={reload}
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Value</TableHead>
              <TableHead>Kind</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {namedValues.map((nv) => (
              <TableRow key={nv.name}>
                <TableCell className="font-mono">{nv.name}</TableCell>
                <TableCell className="font-mono text-muted-foreground">{nv.value}</TableCell>
                <TableCell>
                  {nv.secret ? (
                    <Badge variant="outline" className="gap-1">
                      <Lock className="size-3" /> secret
                    </Badge>
                  ) : (
                    <span className="text-muted-foreground">plain</span>
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
