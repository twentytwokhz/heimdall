import type { PlaygroundHeader, PlaygroundRequest } from "@/lib/api"

// The editor's mutable model for one imported request. Headers are an ordered, individually-editable
// array; we keep blank rows while the user types and drop them only when building the replay payload.
// The provenance fields (name / originalUrl / bodyMediaType / notes) ride along untouched so a
// round-trip through the editor preserves what the importer reported.
//
// id is a stable per-session row key so React keeps focus and input identity when a middle row is
// removed (index keys would re-bind the wrong row). A monotonic counter avoids crypto.randomUUID,
// which throws outside a secure context (the console can be served over plain http to a LAN host).
let headerSeq = 0
const nextHeaderId = () => `h${headerSeq++}`

export type HeaderDraft = { id: string; name: string; value: string }

export type RequestDraft = {
  name: string
  method: string
  url: string
  originalUrl: string
  headers: HeaderDraft[]
  body: string
  bodyMediaType: string | null
  notes: string[]
}

/** Wire request -> editable draft. A null body becomes "" so the textarea stays controlled. */
export function toDraft(req: PlaygroundRequest): RequestDraft {
  return {
    name: req.name,
    method: req.method,
    url: req.url,
    originalUrl: req.originalUrl,
    headers: req.headers.map((h) => ({ id: nextHeaderId(), name: h.name, value: h.value })),
    body: req.body ?? "",
    bodyMediaType: req.bodyMediaType,
    notes: req.notes,
  }
}

/** Editable draft -> wire request. Blank-name header rows are dropped; an empty body sends as null. */
export function fromDraft(draft: RequestDraft): PlaygroundRequest {
  const headers: PlaygroundHeader[] = draft.headers
    .filter((h) => h.name.trim() !== "")
    .map((h) => ({ name: h.name, value: h.value }))
  return {
    name: draft.name,
    method: draft.method,
    url: draft.url,
    originalUrl: draft.originalUrl,
    headers,
    body: draft.body.length > 0 ? draft.body : null,
    bodyMediaType: draft.bodyMediaType,
    notes: draft.notes,
  }
}

/** Append a blank header row (returns a new draft; the input is untouched). */
export function addHeader(draft: RequestDraft): RequestDraft {
  return { ...draft, headers: [...draft.headers, { id: nextHeaderId(), name: "", value: "" }] }
}

/** Patch one header row by index (returns a new draft; the input is untouched). */
export function updateHeader(
  draft: RequestDraft,
  index: number,
  patch: Partial<HeaderDraft>,
): RequestDraft {
  const headers = draft.headers.map((h, i) => (i === index ? { ...h, ...patch } : h))
  return { ...draft, headers }
}

/** Drop the header row at index (returns a new draft; the input is untouched). */
export function removeHeader(draft: RequestDraft, index: number): RequestDraft {
  return { ...draft, headers: draft.headers.filter((_, i) => i !== index) }
}
