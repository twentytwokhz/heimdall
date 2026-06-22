// Typed client for the read-only console admin API under /_apim. The .NET host serializes with
// JsonSerializerDefaults.Web (camelCase), so these types mirror the camelCase wire shape of the
// ConfigView / EffectivePolicy DTOs (Heimdall.Api/Console/ConsoleViews.cs, Application/Pipeline).
// URLs are relative so the same code works in dev (Vite proxies /_apim/* to the host) and in prod
// (the SPA is served from /_apim by the host itself).

export type OperationView = {
  id: string
  method: string
  uriTemplate: string
  hasPolicy: boolean
}

export type ApiView = {
  id: string
  displayName: string
  path: string
  subscriptionRequired: boolean
  serviceUrl: string | null
  operations: OperationView[]
  productIds: string[]
  hasPolicy: boolean
}

export type ProductView = {
  id: string
  displayName: string
  requiresSubscription: boolean
  apiIds: string[]
  hasPolicy: boolean
}

export type SubscriptionView = {
  id: string
  displayName: string | null
  scope: string
  productId: string | null
  apiId: string | null
  state: string
}

// value is server-masked to "***" for secrets; the key is never sent. See NamedValueView in C#.
export type NamedValueView = {
  name: string
  secret: boolean
  value: string
}

export type BackendView = {
  id: string
  url: string
}

export type ConfigView = {
  apis: ApiView[]
  products: ProductView[]
  subscriptions: SubscriptionView[]
  namedValues: NamedValueView[]
  backends: BackendView[]
  fragments: string[]
  hasGlobalPolicy: boolean
}

// One parsed policy element, kept faithful: name, attributes, children, inner text.
export type PolicyNode = {
  name: string
  attributes: Record<string, string>
  children: PolicyNode[]
  rawText: string | null
}

// The flattened effective policy for one operation: the four pipeline sections after <base/> splicing.
export type EffectivePolicy = {
  inbound: PolicyNode[]
  backend: PolicyNode[]
  outbound: PolicyNode[]
  onError: PolicyNode[]
  requiresBodyBuffering: boolean
}

// --- Live trace feed (the SignalR hub and /_apim/traces serve byte-identical JSON; see
// Heimdall.Application/Tracing/RequestTrace.cs + Heimdall.Api/Console/ConsoleJson.cs). The host's
// JsonStringEnumConverter renders the outcome as one of these strings, so the union is exact. ---

/** How a traced request ended (mirrors the C# TraceOutcome enum, serialized as a string). */
export type TraceOutcome = "Completed" | "ShortCircuited" | "Rejected" | "Error"

/** One policy element that ran inside a stage, with how long it took. */
export type PolicyTrace = {
  name: string
  durationMs: number
}

/** One canvas stage (Frontend / Inbound / Backend / Outbound / OnError) and the policies it ran. */
export type TraceStage = {
  section: string
  durationMs: number
  policies: PolicyTrace[]
}

/** The last-error detail attached to a faulted trace (mirrors C# LastErrorInfo: context.LastError). */
export type TraceError = {
  source: string
  reason: string
  message: string
}

/** A full record of one request through the gateway, as pushed on the hub and served by /_apim/traces. */
export type RequestTrace = {
  requestId: string
  timestamp: string
  method: string
  path: string
  apiId: string
  apiName: string
  operationId: string
  operationMethod: string
  subscriptionId: string | null
  productId: string | null
  statusCode: number
  durationMs: number
  outcome: TraceOutcome
  error: TraceError | null
  stages: TraceStage[]
}

// --- Playground (import + replay). The host serializes these with the same web defaults; see
// Heimdall.Application/Playground/PlaygroundRequest.cs and Heimdall.Api/Console/ConsoleEndpoints.cs.
// body / bodyMediaType are nullable on the wire (System.Text.Json writes null), matching the
// `string | null` convention used above. ---

/** One request header, as parsed from a collection and sent back on replay. */
export type PlaygroundHeader = {
  name: string
  value: string
}

/**
 * One multipart/form-data field. A text field carries `textValue`; a file field is a slot whose
 * `fileBase64` is filled with the chosen file's base64 bytes before replay (the two are mutually
 * exclusive). Mirrors C# PlaygroundFormField.
 */
export type PlaygroundFormField = {
  name: string
  textValue: string | null
  fileBase64: string | null
}

/** A structured multipart/form-data body: the replay client assembles the wire multipart from these. */
export type PlaygroundFormDataBody = {
  fields: PlaygroundFormField[]
}

/** A replayable request: what the importer parsed, editable in the playground before replay. */
export type PlaygroundRequest = {
  name: string
  method: string
  url: string
  /** The request URL as it appeared in the source (variables unresolved), kept for display. */
  originalUrl: string
  headers: PlaygroundHeader[]
  body: string | null
  bodyMediaType: string | null
  /** Fail-loud caveats from import (e.g. "script not run"); must be surfaced, never hidden. */
  notes: string[]
  /** Structured multipart body when the request is multipart/form-data; null otherwise. */
  formData: PlaygroundFormDataBody | null
}

/** The result of importing a Postman v2.1 export or a .http file. */
export type CollectionImportResult = {
  source: string
  requests: PlaygroundRequest[]
  notes: string[]
}

/** The gateway's response to a replayed request, with the correlated trace's requestId. */
export type PlaygroundResponse = {
  requestId: string
  statusCode: number
  headers: PlaygroundHeader[]
  body: string | null
}

/** Thrown for any non-2xx response so callers can show the status; carries the HTTP code. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = "ApiError"
    this.status = status
  }
}

// Turn a non-2xx response into an ApiError. The playground endpoints answer failures with a JSON
// { error } body (fail-loud); prefer that human message, falling back to the status line for the
// read endpoints that have no body. The message is shown to the user as-is, so it carries no URL.
async function toApiError(res: Response): Promise<ApiError> {
  let detail = `${res.status} ${res.statusText}`
  try {
    const body = (await res.json()) as { error?: string }
    if (body?.error) detail = body.error
  } catch {
    // No/!JSON body (e.g. the GET endpoints): the status line is the best we have.
  }
  return new ApiError(res.status, detail)
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(url, { signal, headers: { Accept: "application/json" } })
  if (!res.ok) {
    throw await toApiError(res)
  }
  return (await res.json()) as T
}

export function getConfig(signal?: AbortSignal): Promise<ConfigView> {
  return getJson<ConfigView>("/_apim/config", signal)
}

/** Effective policy for one operation. The endpoint 404s when the api or operation id is unknown. */
export function getEffectivePolicy(
  apiId: string,
  operationId: string,
  signal?: AbortSignal,
): Promise<EffectivePolicy> {
  return getJson<EffectivePolicy>(
    `/_apim/policies/${encodeURIComponent(apiId)}/${encodeURIComponent(operationId)}`,
    signal,
  )
}

/** Recent traces, newest first. Used to backfill the live feed on connect and after a reconnect. */
export function getRecentTraces(limit = 100, signal?: AbortSignal): Promise<RequestTrace[]> {
  return getJson<RequestTrace[]>(`/_apim/traces?limit=${limit}`, signal)
}

/**
 * Import a Postman v2.1 export or a .http file into replayable requests. The collection is required;
 * an optional environment file resolves {{variables}}. The endpoint fails loud (400 { error }) on an
 * unsupported format/version or an unparseable collection; toApiError surfaces that message.
 */
export async function importCollection(
  collection: File,
  environment?: File,
  signal?: AbortSignal,
): Promise<CollectionImportResult> {
  const form = new FormData()
  form.append("collection", collection)
  if (environment) form.append("environment", environment)
  const res = await fetch("/_apim/playground/import", {
    method: "POST",
    body: form,
    signal,
    headers: { Accept: "application/json" },
  })
  if (!res.ok) {
    throw await toApiError(res)
  }
  return (await res.json()) as CollectionImportResult
}

// --- Policy authoring (read source + save). Mirrors Heimdall.Api/Console/ConsoleEndpoints.cs
// (/_apim/authoring/policy). A save validates + hot-swaps the policy in-memory; it never writes to
// disk, and fails loud (400 { error }) on malformed XML or an unsupported policy. ---

/** Which policy a scope ref addresses; the ids carried depend on the scope. */
export type PolicyScope = "global" | "api" | "operation" | "product"

/** Identifies one editable policy: the scope plus whatever ids that scope needs. */
export type PolicyScopeRef = {
  scope: PolicyScope
  apiId?: string
  operationId?: string
  productId?: string
}

/** A scope's current source XML, as serialized from the live config. */
export type PolicySource = PolicyScopeRef & { xml: string }

function scopeQuery(ref: PolicyScopeRef): string {
  const params = new URLSearchParams({ scope: ref.scope })
  if (ref.apiId) params.set("apiId", ref.apiId)
  if (ref.operationId) params.set("operationId", ref.operationId)
  if (ref.productId) params.set("productId", ref.productId)
  return params.toString()
}

/** Current source XML for one scope. 404 for an unknown id, 400 for a bad/incomplete scope. */
export function getPolicySource(ref: PolicyScopeRef, signal?: AbortSignal): Promise<PolicySource> {
  return getJson<PolicySource>(`/_apim/authoring/policy?${scopeQuery(ref)}`, signal)
}

/** Validate + hot-swap a scope's policy in-memory. Throws ApiError (400 with a message) on bad input. */
export async function savePolicy(ref: PolicyScopeRef, xml: string, signal?: AbortSignal): Promise<void> {
  const res = await fetch("/_apim/authoring/policy", {
    method: "POST",
    body: JSON.stringify({ ...ref, xml }),
    signal,
    headers: { "Content-Type": "application/json", Accept: "application/json" },
  })
  if (!res.ok) {
    throw await toApiError(res)
  }
}

/** Replay one request through the live gateway, returning its response and the correlated trace id. */
export async function replayRequest(
  request: PlaygroundRequest,
  signal?: AbortSignal,
): Promise<PlaygroundResponse> {
  const res = await fetch("/_apim/playground", {
    method: "POST",
    body: JSON.stringify(request),
    signal,
    headers: { "Content-Type": "application/json", Accept: "application/json" },
  })
  if (!res.ok) {
    throw await toApiError(res)
  }
  return (await res.json()) as PlaygroundResponse
}
