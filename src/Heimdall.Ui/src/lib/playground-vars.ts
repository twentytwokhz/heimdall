import type { PlaygroundRequest } from "@/lib/api"

// Runtime-variable substitution for the playground. The importer already resolves {{vars}} it can
// (from the uploaded environment + collection variables), so the only {{...}} left in an imported
// request are the unresolved ones - typically a token a Postman script would have set at runtime.
// These helpers let the UI collect those tokens, let the user supply values, and substitute them
// into a request before replay. Pure and framework-free so the substitution is unit-tested in
// isolation; the surface holds the values and renders the panel.
//
// Prefix of the per-request note the C# importers emit for a token they could not resolve
// (HttpFileImporter / PostmanV21Importer: "Unresolved variable: {{name}}"). The surface filters
// these out of the request editor because this panel owns them; kept here, next to the token logic,
// as the single TS source for the string. Must match the importer wording.
export const UNRESOLVED_NOTE_PREFIX = "Unresolved variable:"

// Token syntax mirrors the C# PlaceholderResolver: {{name}}, name trimmed. A fresh regex per call:
// String.replace/matchAll are immune to a shared `/g` instance, but exec/test in a loop would not be,
// so a factory keeps every caller safe regardless.
const tokenRe = () => /\{\{([^{}]+)\}\}/g

function substitute(input: string, values: Record<string, string>): string {
  return input.replace(tokenRe(), (whole, rawName: string) => {
    const value = values[rawName.trim()]
    return value != null && value !== "" ? value : whole
  })
}

/** Distinct, trimmed {{token}} names across every request's url, header values, and body (first-seen). */
export function collectVariables(requests: readonly PlaygroundRequest[]): string[] {
  const seen = new Set<string>()
  const order: string[] = []
  const scan = (text: string | null) => {
    if (!text) return
    for (const match of text.matchAll(tokenRe())) {
      const name = match[1].trim()
      if (name && !seen.has(name)) {
        seen.add(name)
        order.push(name)
      }
    }
  }
  for (const req of requests) {
    scan(req.url)
    for (const header of req.headers) scan(header.value)
    scan(req.body)
  }
  return order
}

/**
 * Return a copy of the request with filled (non-empty) values substituted into the url, header
 * values, and body. A token with no value (or a blank one) is left as the literal {{token}}, so a
 * deliberately token-less request still replays as-is (and the backend rejects it).
 */
export function applyVariables(
  req: PlaygroundRequest,
  values: Record<string, string>,
): PlaygroundRequest {
  return {
    ...req,
    url: substitute(req.url, values),
    // Header names are structural identifiers, never runtime tokens; only values are substituted.
    headers: req.headers.map((h) => ({ name: h.name, value: substitute(h.value, values) })),
    body: req.body == null ? req.body : substitute(req.body, values),
  }
}

/** The tokens still literal in the request after applying values (for the "still unresolved" hint). */
export function unfilledTokens(req: PlaygroundRequest, values: Record<string, string>): string[] {
  return collectVariables([applyVariables(req, values)])
}
