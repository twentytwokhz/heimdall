import { useEffect, useRef, useState, type DragEvent } from "react"
import { useNavigate } from "react-router-dom"
import { Upload, X } from "lucide-react"
import { cn } from "@/lib/utils"
import { statusTone } from "@/lib/status-tone"
import {
  ApiError,
  importCollection,
  replayRequest,
  type CollectionImportResult,
  type PlaygroundRequest,
  type PlaygroundResponse,
} from "@/lib/api"
import {
  addHeader,
  fromDraft,
  removeHeader,
  toDraft,
  updateFormField,
  updateHeader,
  type FormFieldDraft,
  type RequestDraft,
} from "@/lib/playground-request"
import { applyVariables, collectVariables, UNRESOLVED_NOTE_PREFIX } from "@/lib/playground-vars"
import { useTraceFeed } from "@/lib/use-trace-feed"
import { SurfaceHeader } from "@/components/data/SurfaceHeader"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"

// The methods offered in the editor dropdown. An imported request can carry any verb, so the
// selected draft's method is always kept as an option even if it is outside this common set.
const METHODS = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"]

// One uploaded file is capped at 10 MB, matching the server guard in LoopbackReplayClient. Enforced
// here too so an oversized file is rejected before it is read into memory.
const MAX_FILE_BYTES = 10 * 1024 * 1024

// Read a File into the base64 the replay payload carries. FileReader's data URL is "data:...;base64,<b64>";
// strip the prefix so only the raw base64 remains.
function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(reader.error ?? new Error("Could not read the file."))
    reader.onload = () => {
      const result = reader.result
      if (typeof result !== "string") {
        reject(new Error("Could not read the file."))
        return
      }
      const comma = result.indexOf(",")
      resolve(comma >= 0 ? result.slice(comma + 1) : result)
    }
    reader.readAsDataURL(file)
  })
}

// A panel listing fail-loud import caveats (collection-level or per-request). Never hidden: these
// tell the user what the importer could not honor (scripts skipped, variables unresolved, etc.).
function NotesPanel({ notes }: { notes: string[] }) {
  if (notes.length === 0) return null
  return (
    <div className="rounded-lg border border-amber/40 bg-amber/5 p-3 text-xs text-amber">
      <ul className="space-y-1">
        {notes.map((note, i) => (
          <li key={i}>{note}</li>
        ))}
      </ul>
    </div>
  )
}

function MethodBadge({ method }: { method: string }) {
  return (
    <span className="w-12 shrink-0 rounded border border-input px-1 py-0.5 text-center font-mono text-[10px] uppercase tracking-wide text-muted-foreground">
      {method}
    </span>
  )
}

function ImportZone({
  importing,
  onImport,
}: {
  importing: boolean
  onImport: (collection: File, environment?: File) => void
}) {
  const [collection, setCollection] = useState<File | null>(null)
  const [environment, setEnvironment] = useState<File | null>(null)
  const [dragOver, setDragOver] = useState(false)

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setDragOver(false)
    const file = e.dataTransfer.files[0]
    if (file) setCollection(file)
  }

  // .http files belong in the collection slot (which accepts them); the environment slot is .json
  // only, so the two pickers are labeled and split explicitly to avoid putting a file in the wrong one.
  const fileInputClass =
    "text-xs file:mr-2 file:rounded file:border file:border-input file:bg-transparent file:px-2 file:py-1 file:text-foreground"

  return (
    <div className="space-y-3 rounded-xl border border-border bg-card p-4">
      <div
        onDragOver={(e) => {
          e.preventDefault()
          setDragOver(true)
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
        className={cn(
          "flex flex-col items-center justify-center gap-2 rounded-lg border border-dashed px-6 py-8 text-center transition-colors",
          dragOver ? "border-cyan/60 bg-cyan/5" : "border-input",
        )}
      >
        <Upload className="size-5 text-muted-foreground" />
        <div className="text-sm">
          {collection ? (
            <span className="font-mono text-xs">{collection.name}</span>
          ) : (
            <>
              Drag a <span className="font-medium">Postman v2.1</span> collection or a{" "}
              <span className="font-medium">.http</span> file here, or choose one below.
            </>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <label className="flex items-center gap-2 text-xs">
          <span className="shrink-0 font-medium">Collection (required)</span>
          <input
            type="file"
            aria-label="Collection file"
            accept=".json,.http,application/json,text/plain"
            className={fileInputClass}
            onChange={(e) => setCollection(e.target.files?.[0] ?? null)}
          />
        </label>
        <label className="flex items-center gap-2 text-xs text-muted-foreground">
          <span className="shrink-0">Environment (optional)</span>
          <input
            type="file"
            aria-label="Environment file"
            accept=".json,application/json"
            className={fileInputClass}
            onChange={(e) => setEnvironment(e.target.files?.[0] ?? null)}
          />
          {environment ? (
            <button
              type="button"
              onClick={() => setEnvironment(null)}
              aria-label="Clear environment file"
              className="text-muted-foreground hover:text-foreground"
            >
              <X className="size-3.5" />
            </button>
          ) : null}
        </label>
        <Button
          className="ml-auto"
          disabled={!collection || importing}
          onClick={() => collection && onImport(collection, environment ?? undefined)}
        >
          {importing ? "Importing..." : "Import"}
        </Button>
      </div>
    </div>
  )
}

function RequestList({
  requests,
  selectedIndex,
  onSelect,
}: {
  requests: PlaygroundRequest[]
  selectedIndex: number | null
  onSelect: (index: number) => void
}) {
  return (
    <aside className="min-w-0">
      <div className="mb-2 text-[11px] uppercase tracking-[0.14em] text-faint">
        Requests · {requests.length}
      </div>
      <div className="max-h-[70vh] space-y-0.5 overflow-y-auto rounded-xl border border-border bg-card/50 p-1.5">
        {requests.map((req, i) => (
          <button
            key={i}
            onClick={() => onSelect(i)}
            aria-current={i === selectedIndex ? "true" : undefined}
            className={cn(
              "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs transition-colors hover:bg-accent/40",
              i === selectedIndex && "bg-accent/60",
            )}
          >
            <MethodBadge method={req.method} />
            <span className="truncate">{req.name}</span>
            {req.notes.length > 0 ? (
              <span className="ml-auto shrink-0 rounded-full bg-amber/15 px-1.5 text-[10px] text-amber">
                {req.notes.length}
              </span>
            ) : null}
          </button>
        ))}
      </div>
    </aside>
  )
}

function HeadersEditor({
  draft,
  onChange,
}: {
  draft: RequestDraft
  onChange: (next: RequestDraft) => void
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <span className="text-[11px] uppercase tracking-[0.14em] text-faint">Headers</span>
        <Button variant="outline" size="sm" onClick={() => onChange(addHeader(draft))}>
          Add header
        </Button>
      </div>
      {draft.headers.length === 0 ? (
        <p className="text-xs text-faint">No headers.</p>
      ) : (
        draft.headers.map((header, i) => (
          <div key={header.id} className="flex items-center gap-2">
            <Input
              value={header.name}
              placeholder="Header"
              className="h-8 font-mono text-xs"
              onChange={(e) => onChange(updateHeader(draft, i, { name: e.target.value }))}
            />
            <Input
              value={header.value}
              placeholder="Value"
              className="h-8 font-mono text-xs"
              onChange={(e) => onChange(updateHeader(draft, i, { value: e.target.value }))}
            />
            <button
              type="button"
              onClick={() => onChange(removeHeader(draft, i))}
              aria-label={`Remove header ${header.name || i + 1}`}
              className="shrink-0 text-muted-foreground hover:text-foreground"
            >
              <X className="size-4" />
            </button>
          </div>
        ))
      )}
    </div>
  )
}

// The multipart/form-data fields of an imported request. Text fields are read-only (their value came
// from the import); file fields are slots the user fills with a real local file, read to base64 and
// sent through the gateway on replay. A file over 10 MB is rejected with a visible error (the server
// enforces the same cap). The Content-Type/boundary is owned by the server, so there is no body box.
function FormDataEditor({
  draft,
  onChange,
}: {
  draft: RequestDraft
  onChange: (next: RequestDraft) => void
}) {
  const [errors, setErrors] = useState<Record<string, string>>({})
  const fields = draft.formData ?? []

  const pickFile = async (field: FormFieldDraft, file: File | null) => {
    if (!file) return
    if (file.size > MAX_FILE_BYTES) {
      setErrors((prev) => ({
        ...prev,
        [field.id]: `'${file.name}' is ${Math.ceil(file.size / (1024 * 1024))} MB; the limit is 10 MB per file.`,
      }))
      return
    }
    try {
      const fileBase64 = await readFileAsBase64(file)
      setErrors((prev) => {
        const next = { ...prev }
        delete next[field.id]
        return next
      })
      onChange(updateFormField(draft, field.id, { fileBase64, fileName: file.name }))
    } catch {
      setErrors((prev) => ({ ...prev, [field.id]: `Could not read '${file.name}'.` }))
    }
  }

  return (
    <div className="space-y-2">
      <span className="text-[11px] uppercase tracking-[0.14em] text-faint">
        Form data · multipart/form-data
      </span>
      {fields.map((field) => {
        const isFile = field.textValue == null
        return (
          <div key={field.id} className="flex items-center gap-2">
            <span className="w-32 shrink-0 truncate font-mono text-xs text-muted-foreground">
              {field.name}
            </span>
            {isFile ? (
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <input
                  type="file"
                  aria-label={`File for ${field.name}`}
                  className="text-xs file:mr-2 file:rounded file:border file:border-input file:bg-transparent file:px-2 file:py-1 file:text-foreground"
                  onChange={(e) => pickFile(field, e.target.files?.[0] ?? null)}
                />
                {field.fileName ? (
                  <span className="font-mono text-[11px] text-green">{field.fileName} attached</span>
                ) : (
                  <span className="font-mono text-[11px] text-amber">No file chosen.</span>
                )}
                {errors[field.id] ? (
                  <span className="font-mono text-[11px] text-rose">{errors[field.id]}</span>
                ) : null}
              </div>
            ) : (
              <Input
                value={field.textValue ?? ""}
                readOnly
                aria-label={`Value for ${field.name}`}
                className="h-8 font-mono text-xs"
              />
            )}
          </div>
        )
      })}
    </div>
  )
}

function RequestEditor({
  draft,
  replaying,
  replayError,
  onChange,
  onReplay,
}: {
  draft: RequestDraft
  replaying: boolean
  replayError: string | null
  onChange: (next: RequestDraft) => void
  onReplay: () => void
}) {
  const methodOptions = METHODS.includes(draft.method) ? METHODS : [draft.method, ...METHODS]

  return (
    <div className="space-y-4 rounded-xl border border-border bg-card p-4">
      {/* Unresolved-variable notes are owned by the Variables panel (which stays live as you fill
          them); keep only the other import caveats here so the editor note does not go stale. */}
      <NotesPanel notes={draft.notes.filter((n) => !n.startsWith(UNRESOLVED_NOTE_PREFIX))} />

      <div className="flex gap-2">
        <select
          value={draft.method}
          onChange={(e) => onChange({ ...draft, method: e.target.value })}
          aria-label="HTTP method"
          className="h-9 shrink-0 rounded-md border border-input bg-transparent px-2 font-mono text-sm outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30"
        >
          {methodOptions.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
        <Input
          value={draft.url}
          aria-label="Request URL"
          className="font-mono text-xs"
          onChange={(e) => onChange({ ...draft, url: e.target.value })}
        />
        <Button className="shrink-0" disabled={replaying} onClick={onReplay}>
          {replaying ? "Replaying..." : "Replay"}
        </Button>
      </div>

      <HeadersEditor draft={draft} onChange={onChange} />

      {draft.formData ? (
        <FormDataEditor draft={draft} onChange={onChange} />
      ) : (
        <div className="space-y-1.5">
          <span className="text-[11px] uppercase tracking-[0.14em] text-faint">
            Body{draft.bodyMediaType ? ` · ${draft.bodyMediaType}` : ""}
          </span>
          <Textarea
            value={draft.body}
            placeholder="No request body."
            className="min-h-24 font-mono text-xs"
            onChange={(e) => onChange({ ...draft, body: e.target.value })}
          />
        </div>
      )}

      {replayError ? (
        <div className="rounded-lg border border-rose/40 bg-rose/5 p-3 font-mono text-xs text-rose">
          {replayError}
        </div>
      ) : null}
    </div>
  )
}

function ResponsePanel({
  response,
  onViewInLive,
}: {
  response: PlaygroundResponse
  onViewInLive: () => void
}) {
  return (
    <div className="space-y-3 rounded-xl border border-border bg-card p-4">
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
        <Button variant="outline" size="sm" className="ml-auto" onClick={onViewInLive}>
          View in Live
        </Button>
      </div>

      {response.headers.length > 0 ? (
        <div className="space-y-0.5 font-mono text-xs">
          {response.headers.map((h, i) => (
            <div key={i} className="flex gap-2">
              <span className="shrink-0 text-cyan">{h.name}:</span>
              <span className="truncate text-muted-foreground">{h.value}</span>
            </div>
          ))}
        </div>
      ) : null}

      <pre className="max-h-80 overflow-auto rounded-lg border border-border bg-background/50 p-3 font-mono text-xs">
        {response.body && response.body.length > 0 ? response.body : "(empty body)"}
      </pre>
    </div>
  )
}

// Collection-wide variables. The importer leaves a token it could not resolve (e.g. an {{access_token}}
// a script would set at runtime) as a literal; this panel lets you supply those once and they
// substitute into any request on replay. A blank value is left as the literal, so a deliberately
// token-less request still replays as-is. Set rows read green, unset rows amber (the surface's
// "needs attention" colour).
function VariablesPanel({
  names,
  values,
  onChange,
}: {
  names: string[]
  values: Record<string, string>
  onChange: (name: string, value: string) => void
}) {
  const set = names.filter((n) => (values[n] ?? "").trim() !== "").length
  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="mb-1 text-[11px] uppercase tracking-[0.14em] text-faint">
        Variables · {set}/{names.length} set
      </div>
      <p className="mb-3 text-xs text-muted-foreground">
        Values a script would set at runtime (e.g. a token). Blank ones are sent as the literal{" "}
        <span className="font-mono text-faint">{"{{name}}"}</span>.
      </p>
      <div className="grid gap-2 sm:grid-cols-2">
        {names.map((name) => {
          const isSet = (values[name] ?? "").trim() !== ""
          return (
            <label key={name} className="flex items-center gap-2">
              <span
                aria-hidden
                className={cn("size-1.5 shrink-0 rounded-full", isSet ? "bg-green" : "bg-amber")}
              />
              <span className="w-32 shrink-0 truncate font-mono text-xs text-muted-foreground">
                {name}
              </span>
              <Input
                value={values[name] ?? ""}
                placeholder="unset"
                aria-label={`Value for ${name}`}
                className={cn("h-8 font-mono text-xs", !isSet && "border-amber/40")}
                onChange={(e) => onChange(name, e.target.value)}
              />
            </label>
          )
        })}
      </div>
    </div>
  )
}

export function Playground() {
  const navigate = useNavigate()
  const { select } = useTraceFeed()

  const [imported, setImported] = useState<CollectionImportResult | null>(null)
  const [importing, setImporting] = useState(false)
  const [importError, setImportError] = useState<string | null>(null)
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)
  const [draft, setDraft] = useState<RequestDraft | null>(null)
  const [response, setResponse] = useState<PlaygroundResponse | null>(null)
  const [replaying, setReplaying] = useState(false)
  const [replayError, setReplayError] = useState<string | null>(null)
  // Collection-wide variable names (the unresolved tokens found at import) and the values the user
  // supplies for them; both seeded fresh on each import.
  const [variableNames, setVariableNames] = useState<string[]>([])
  const [variables, setVariables] = useState<Record<string, string>>({})

  // Abort the in-flight import/replay if the surface unmounts (or a new request supersedes it), so
  // no fetch resolves onto a gone component. Aborted calls reject and are swallowed via signal.aborted.
  const inflight = useRef<AbortController | null>(null)
  useEffect(() => () => inflight.current?.abort(), [])

  const selectRequest = (result: CollectionImportResult, index: number) => {
    setSelectedIndex(index)
    setDraft(toDraft(result.requests[index]))
    setResponse(null)
    setReplayError(null)
  }

  const handleImport = async (collection: File, environment?: File) => {
    inflight.current?.abort()
    const ac = new AbortController()
    inflight.current = ac
    setImporting(true)
    setImportError(null)
    try {
      const result = await importCollection(collection, environment, ac.signal)
      setImported(result)
      setVariableNames(collectVariables(result.requests))
      setVariables({})
      if (result.requests.length > 0) selectRequest(result, 0)
      else {
        setSelectedIndex(null)
        setDraft(null)
      }
    } catch (e) {
      if (ac.signal.aborted) return
      setImportError(e instanceof ApiError ? e.message : "Import failed.")
    } finally {
      if (!ac.signal.aborted) setImporting(false)
    }
  }

  const handleReplay = async () => {
    if (!draft) return
    inflight.current?.abort()
    const ac = new AbortController()
    inflight.current = ac
    setReplaying(true)
    setReplayError(null)
    try {
      setResponse(await replayRequest(applyVariables(fromDraft(draft), variables), ac.signal))
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
        title="Playground"
        blurb="Replay requests against the local gateway, or import a Postman or .http collection."
        count={imported ? imported.requests.length : null}
      />

      <div className="space-y-5">
        <ImportZone importing={importing} onImport={handleImport} />

        {importError ? (
          <div className="rounded-lg border border-rose/40 bg-rose/5 p-3 font-mono text-xs text-rose">
            {importError}
          </div>
        ) : null}

        {imported ? <NotesPanel notes={imported.notes} /> : null}

        {imported && imported.requests.length === 0 ? (
          <div className="rounded-xl border border-border bg-card/50 px-6 py-12 text-center text-sm text-muted-foreground">
            The collection imported, but held no replayable requests.
          </div>
        ) : null}

        {imported && variableNames.length > 0 ? (
          <VariablesPanel
            names={variableNames}
            values={variables}
            onChange={(name, value) => setVariables((prev) => ({ ...prev, [name]: value }))}
          />
        ) : null}

        {imported && draft ? (
          <div className="grid gap-5 lg:grid-cols-[260px_1fr]">
            <RequestList
              requests={imported.requests}
              selectedIndex={selectedIndex}
              onSelect={(i) => selectRequest(imported, i)}
            />
            <div className="min-w-0 space-y-5">
              <RequestEditor
                draft={draft}
                replaying={replaying}
                replayError={replayError}
                onChange={setDraft}
                onReplay={handleReplay}
              />
              {response ? <ResponsePanel response={response} onViewInLive={viewInLive} /> : null}
            </div>
          </div>
        ) : null}
      </div>
    </>
  )
}
