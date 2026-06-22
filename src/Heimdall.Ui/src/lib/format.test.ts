import { describe, expect, it } from "vitest"
import { fmtRequestCount } from "@/lib/format"

describe("fmtRequestCount", () => {
  it("renders zero", () => {
    expect(fmtRequestCount(0, 200)).toBe("0 requests")
  })

  it("renders one as singular", () => {
    expect(fmtRequestCount(1, 200)).toBe("1 request")
  })

  it("renders a count below the cap", () => {
    expect(fmtRequestCount(47, 200)).toBe("47 requests")
  })

  it("renders the cap label at exactly the cap", () => {
    expect(fmtRequestCount(200, 200)).toBe("200+ requests")
  })

  it("renders the cap label beyond the cap", () => {
    expect(fmtRequestCount(250, 200)).toBe("200+ requests")
  })
})
