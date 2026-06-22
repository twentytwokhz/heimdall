import type { ApiView, OperationView, PlaygroundRequest, PolicyScope, PolicyScopeRef } from "@/lib/api"

// Pure helpers for the authoring surface. After saving a policy, the user replays a request to see the
// new behaviour on the trace; for a concrete operation we can derive that request straight from the
// loaded config, so they do not have to hand-type one.

/**
 * Build the scope ref to read/save, or null while a required id is still unavailable (e.g. an `api`
 * scope before any API has loaded). Each scope carries only the ids it needs.
 */
export function buildScopeRef(
  scope: PolicyScope,
  apiId: string,
  operationId: string,
  productId: string,
): PolicyScopeRef | null {
  switch (scope) {
    case "global":
      return { scope }
    case "api":
      return apiId ? { scope, apiId } : null
    case "operation":
      return apiId && operationId ? { scope, apiId, operationId } : null
    case "product":
      return productId ? { scope, productId } : null
  }
}

/**
 * The gateway path one operation routes to: the API path joined to the operation's URI template, with
 * each {param} placeholder filled by a sample value. Returned relative - the replay client rebases it
 * onto the live gateway origin.
 */
export function deriveReplayPath(apiPath: string, uriTemplate: string): string {
  const filled = uriTemplate.replace(/\{[^/}]+\}/g, "1")
  return `${apiPath}${filled}`
}

/**
 * A minimal replay request for one operation: its method and the gateway path it hits, no headers or
 * body. Just enough to exercise the edited scope and produce a fresh trace.
 */
export function deriveReplayRequest(api: ApiView, op: OperationView): PlaygroundRequest {
  const url = deriveReplayPath(api.path, op.uriTemplate)
  return {
    name: `${op.method} ${url}`,
    method: op.method,
    url,
    originalUrl: url,
    headers: [],
    body: null,
    bodyMediaType: null,
    notes: [],
  }
}
