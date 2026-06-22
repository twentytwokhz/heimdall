import type { PlaygroundFormDataBody, PlaygroundHeader, PlaygroundRequest } from "@/lib/api"

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

// One form-data field in the editor. Text fields are read-only (textValue from import). File fields
// start empty; the surface fills fileBase64 + fileName when the user picks a file. The two stay
// mutually exclusive: a text field never carries a file, a file field never carries text.
export type FormFieldDraft = {
  id: string
  name: string
  textValue: string | null
  fileBase64: string | null
  fileName: string | null
}

export type RequestDraft = {
  name: string
  method: string
  url: string
  originalUrl: string
  headers: HeaderDraft[]
  body: string
  bodyMediaType: string | null
  notes: string[]
  // The multipart fields when the request is multipart/form-data; null for any other body.
  formData: FormFieldDraft[] | null
}

let formFieldSeq = 0
const nextFormFieldId = () => `f${formFieldSeq++}`

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
    formData:
      req.formData?.fields.map((f) => ({
        id: nextFormFieldId(),
        name: f.name,
        textValue: f.textValue,
        fileBase64: f.fileBase64,
        fileName: null,
      })) ?? null,
  }
}

/** Editable draft -> wire request. Blank-name header rows are dropped; an empty body sends as null. */
export function fromDraft(draft: RequestDraft): PlaygroundRequest {
  const headers: PlaygroundHeader[] = draft.headers
    .filter((h) => h.name.trim() !== "")
    .map((h) => ({ name: h.name, value: h.value }))
  const formData: PlaygroundFormDataBody | null =
    draft.formData == null
      ? null
      : {
          fields: draft.formData.map((f) => ({
            name: f.name,
            textValue: f.textValue,
            fileBase64: f.fileBase64,
          })),
        }
  return {
    name: draft.name,
    method: draft.method,
    url: draft.url,
    originalUrl: draft.originalUrl,
    headers,
    body: draft.body.length > 0 ? draft.body : null,
    bodyMediaType: draft.bodyMediaType,
    notes: draft.notes,
    formData,
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

/** Patch one form-data field by id (returns a new draft; the input is untouched). */
export function updateFormField(
  draft: RequestDraft,
  id: string,
  patch: Partial<FormFieldDraft>,
): RequestDraft {
  if (draft.formData == null) return draft
  const formData = draft.formData.map((f) => (f.id === id ? { ...f, ...patch } : f))
  return { ...draft, formData }
}
