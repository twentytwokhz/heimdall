import { describe, expect, it } from "vitest"
import type { PlaygroundRequest } from "@/lib/api"
import {
  addHeader,
  fromDraft,
  removeHeader,
  toDraft,
  updateFormField,
  updateHeader,
  type HeaderDraft,
  type RequestDraft,
} from "@/lib/playground-request"

// Project header drafts to their wire fields; the id is an internal row key, not asserted by value.
const pairs = (headers: HeaderDraft[]) => headers.map(({ name, value }) => ({ name, value }))

// A faithful imported request: body + media type present, two headers, one provenance note.
function mk(overrides: Partial<PlaygroundRequest> = {}): PlaygroundRequest {
  return {
    name: "Create pet",
    method: "POST",
    url: "http://localhost:8080/acme/pets",
    originalUrl: "{{baseUrl}}/acme/pets",
    headers: [
      { name: "Content-Type", value: "application/json" },
      { name: "Ocp-Apim-Subscription-Key", value: "secret" },
    ],
    body: '{"name":"Rex"}',
    bodyMediaType: "application/json",
    notes: ["Pre-request script not run."],
    formData: null,
    ...overrides,
  }
}

describe("toDraft", () => {
  it("maps a request into the editable model", () => {
    const d = toDraft(mk())
    expect(d.method).toBe("POST")
    expect(d.url).toBe("http://localhost:8080/acme/pets")
    expect(d.originalUrl).toBe("{{baseUrl}}/acme/pets")
    expect(pairs(d.headers)).toEqual([
      { name: "Content-Type", value: "application/json" },
      { name: "Ocp-Apim-Subscription-Key", value: "secret" },
    ])
    // Each row carries a unique stable id (React key); the wire mapping drops it.
    expect(new Set(d.headers.map((h) => h.id)).size).toBe(d.headers.length)
    expect(d.body).toBe('{"name":"Rex"}')
    expect(d.bodyMediaType).toBe("application/json")
    expect(d.notes).toEqual(["Pre-request script not run."])
  })

  it("normalizes a null body to an empty string so the textarea is controlled", () => {
    const d = toDraft(mk({ body: null, bodyMediaType: null }))
    expect(d.body).toBe("")
    expect(d.bodyMediaType).toBeNull()
  })
})

describe("fromDraft", () => {
  it("round-trips a request unchanged (toDraft then fromDraft)", () => {
    const original = mk()
    expect(fromDraft(toDraft(original))).toEqual(original)
  })

  it("drops blank-name header rows but keeps blank values", () => {
    const draft = toDraft(mk({ headers: [] }))
    draft.headers = [
      { id: "a", name: "X-Real", value: "" },
      { id: "b", name: "  ", value: "ignored" },
      { id: "c", name: "", value: "ignored" },
    ]
    expect(fromDraft(draft).headers).toEqual([{ name: "X-Real", value: "" }])
  })

  it("sends an empty body as null, not an empty string", () => {
    const draft = toDraft(mk({ body: null }))
    expect(fromDraft(draft).body).toBeNull()
  })

  it("round-trips a multipart formData request (text field + file slot) unchanged", () => {
    const original = mk({
      body: null,
      bodyMediaType: "multipart/form-data",
      formData: {
        fields: [
          { name: "title", textValue: "Hi", fileBase64: null },
          { name: "file", textValue: null, fileBase64: null },
        ],
      },
    })
    expect(fromDraft(toDraft(original))).toEqual(original)
  })

  it("carries an uploaded file's base64 into the wire formData", () => {
    const draft = toDraft(
      mk({
        body: null,
        bodyMediaType: "multipart/form-data",
        formData: { fields: [{ name: "file", textValue: null, fileBase64: null }] },
      }),
    )
    // The surface fills fileBase64 + fileName on the file slot before replay.
    draft.formData = updateFormField(draft, draft.formData![0].id, {
      fileBase64: "QUJD",
      fileName: "a.png",
    }).formData
    const wire = fromDraft(draft)
    expect(wire.formData).toEqual({ fields: [{ name: "file", textValue: null, fileBase64: "QUJD" }] })
  })
})

describe("header helpers (immutable)", () => {
  const base: RequestDraft = toDraft(mk({ headers: [{ name: "A", value: "1" }] }))

  it("addHeader appends a blank row with a fresh id, without mutating the input", () => {
    const next = addHeader(base)
    expect(pairs(next.headers)).toEqual([
      { name: "A", value: "1" },
      { name: "", value: "" },
    ])
    expect(next.headers[1].id).not.toBe(next.headers[0].id)
    expect(base.headers).toHaveLength(1)
  })

  it("updateHeader patches one row by index, keeping its id", () => {
    const next = updateHeader(base, 0, { value: "2" })
    expect(pairs(next.headers)[0]).toEqual({ name: "A", value: "2" })
    expect(next.headers[0].id).toBe(base.headers[0].id)
    expect(base.headers[0].value).toBe("1")
  })

  it("removeHeader drops the row at index", () => {
    const next = removeHeader(addHeader(base), 0)
    expect(pairs(next.headers)).toEqual([{ name: "", value: "" }])
  })
})
