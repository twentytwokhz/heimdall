import type { ReactElement } from "react"
import { Route, Routes } from "react-router-dom"
import { AppShell } from "@/components/layout/AppShell"
import { ConfigProvider } from "@/lib/use-config"
import { TraceFeedProvider } from "@/lib/use-trace-feed"
import { Surface } from "@/routes/Surface"
import { Overview } from "@/routes/Overview"
import { Apis } from "@/routes/Apis"
import { Live } from "@/routes/Live"
import { Playground } from "@/routes/Playground"
import { Authoring } from "@/routes/Authoring"
import { Products } from "@/routes/Products"
import { Subscriptions } from "@/routes/Subscriptions"
import { NamedValues } from "@/routes/NamedValues"
import { Backends } from "@/routes/Backends"
import { Fragments } from "@/routes/Fragments"
import { navItems, type NavItem } from "@/nav"

// The config-explorer surfaces, the live trace canvas, the playground, and policy authoring are live.
// Paths not listed here fall back to the placeholder surface.
const surfaces: Record<string, ReactElement> = {
  "/": <Overview />,
  "/live": <Live />,
  "/playground": <Playground />,
  "/authoring": <Authoring />,
  "/apis": <Apis />,
  "/products": <Products />,
  "/subscriptions": <Subscriptions />,
  "/named-values": <NamedValues />,
  "/backends": <Backends />,
  "/fragments": <Fragments />,
}

const elementFor = (item: NavItem) => surfaces[item.path] ?? <Surface item={item} />

export default function App() {
  return (
    <ConfigProvider>
      <TraceFeedProvider>
        <Routes>
          <Route element={<AppShell />}>
            {navItems.map((item) =>
              item.path === "/" ? (
                <Route key={item.path} index element={elementFor(item)} />
              ) : (
                <Route key={item.path} path={item.path.slice(1)} element={elementFor(item)} />
              ),
            )}
            <Route path="*" element={<Surface notFound />} />
          </Route>
        </Routes>
      </TraceFeedProvider>
    </ConfigProvider>
  )
}
