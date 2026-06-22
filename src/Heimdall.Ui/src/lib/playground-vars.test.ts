import { describe, expect, it } from "vitest"
import type { PlaygroundRequest } from "@/lib/api"
import { applyVariables, collectVariables, unfilledTokens } from "@/lib/playground-vars"

function mk(overrides: Partial<PlaygroundRequest> = {}): PlaygroundRequest {
  return {
    name: "r",
    method: "GET",
    url: "http://gw/x",
    originalUrl: "",
    headers: [],
    body: null,
    bodyMediaType: null,
    notes: [],
    ...overrides,
  }
}

describe("collectVariables", () => {
  it("collects distinct, trimmed names across url, header values, and body, first-seen order", () => {
    const reqs = [
      mk({
        url: "http://gw/{{ tenant }}/items",
        headers: [{ name: "Authorization", value: "Bearer {{access_token}}" }],
        body: '{"t":"{{tenant}}"}',
      }),
      mk({ headers: [{ name: "X", value: "{{access_token}}" }] }),
    ]
    expect(collectVariables(reqs)).toEqual(["tenant", "access_token"])
  })

  it("returns empty when there are no tokens", () => {
    expect(collectVariables([mk()])).toEqual([])
  })
})

describe("applyVariables", () => {
  it("substitutes filled values into url, header values, and body", () => {
    const req = mk({
      url: "http://gw/{{tenant}}",
      headers: [{ name: "Authorization", value: "Bearer {{access_token}}" }],
      body: "id={{tenant}}",
    })
    const out = applyVariables(req, { tenant: "acme", access_token: "abc" })
    expect(out.url).toBe("http://gw/acme")
    expect(out.headers[0].value).toBe("Bearer abc")
    expect(out.body).toBe("id=acme")
  })

  it("leaves a blank or missing value as the literal token, and keeps a null body null", () => {
    const req = mk({ headers: [{ name: "Authorization", value: "Bearer {{access_token}}" }] })
    expect(applyVariables(req, { access_token: "" }).headers[0].value).toBe("Bearer {{access_token}}")
    expect(applyVariables(req, {}).headers[0].value).toBe("Bearer {{access_token}}")
    expect(applyVariables(mk(), { a: "1" }).body).toBeNull()
  })

  it("substitutes every occurrence and trims the token name", () => {
    expect(applyVariables(mk({ url: "{{ h }}/{{ h }}" }), { h: "H" }).url).toBe("H/H")
  })

  it("does not mutate the input request", () => {
    const req = mk({ headers: [{ name: "A", value: "{{x}}" }] })
    applyVariables(req, { x: "1" })
    expect(req.headers[0].value).toBe("{{x}}")
  })
})

describe("unfilledTokens", () => {
  it("reports the tokens still literal after applying partial values", () => {
    const req = mk({
      url: "http://{{host}}/{{path}}",
      headers: [{ name: "A", value: "{{access_token}}" }],
    })
    expect(unfilledTokens(req, { host: "h" })).toEqual(["path", "access_token"])
  })
})
