import { describe, expect, it } from "vitest"
import type { ApiView, OperationView } from "@/lib/api"
import { buildScopeRef, deriveReplayPath, deriveReplayRequest } from "@/lib/policy-authoring"

const op = (overrides: Partial<OperationView> = {}): OperationView => ({
  id: "listItems",
  method: "GET",
  uriTemplate: "/items",
  hasPolicy: false,
  ...overrides,
})

const api = (overrides: Partial<ApiView> = {}): ApiView => ({
  id: "acme",
  displayName: "Acme",
  path: "/acme",
  subscriptionRequired: false,
  serviceUrl: null,
  operations: [],
  productIds: [],
  hasPolicy: false,
  ...overrides,
})

describe("deriveReplayPath", () => {
  it("joins the API path to the operation template", () => {
    expect(deriveReplayPath("/acme", "/items")).toBe("/acme/items")
  })

  it("fills a single {param} placeholder with a sample value", () => {
    expect(deriveReplayPath("/acme", "/items/{id}")).toBe("/acme/items/1")
  })

  it("fills every placeholder in a multi-param template", () => {
    expect(deriveReplayPath("/acme", "/users/{userId}/pets/{petId}")).toBe("/acme/users/1/pets/1")
  })
})

describe("buildScopeRef", () => {
  it("returns the global scope with no ids", () => {
    expect(buildScopeRef("global", "", "", "")).toEqual({ scope: "global" })
  })

  it("carries only the ids each scope needs", () => {
    expect(buildScopeRef("api", "acme", "x", "y")).toEqual({ scope: "api", apiId: "acme" })
    expect(buildScopeRef("operation", "acme", "listItems", "y")).toEqual({
      scope: "operation",
      apiId: "acme",
      operationId: "listItems",
    })
    expect(buildScopeRef("product", "x", "y", "unlimited")).toEqual({
      scope: "product",
      productId: "unlimited",
    })
  })

  it("is null while a required id is missing", () => {
    expect(buildScopeRef("api", "", "", "")).toBeNull()
    expect(buildScopeRef("operation", "acme", "", "")).toBeNull()
    expect(buildScopeRef("product", "", "", "")).toBeNull()
  })
})

describe("deriveReplayRequest", () => {
  it("builds a minimal replay request for one operation", () => {
    const req = deriveReplayRequest(api({ path: "/acme" }), op({ method: "POST", uriTemplate: "/items/{id}" }))

    expect(req.method).toBe("POST")
    expect(req.url).toBe("/acme/items/1")
    expect(req.headers).toEqual([])
    expect(req.body).toBeNull()
  })
})
