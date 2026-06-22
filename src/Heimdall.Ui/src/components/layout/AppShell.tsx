import type { CSSProperties } from "react"
import { Outlet, useLocation } from "react-router-dom"
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar"
import { useIsMobile } from "@/hooks/use-mobile"
import { AppSidebar } from "@/components/layout/AppSidebar"
import { Topbar } from "@/components/layout/Topbar"
import { navItems } from "@/nav"

// Full-height frame: topbar on top, then a row of [static rail | content]. The content area carries
// a route-driven breadcrumb above the routed surface (<Outlet/>). On narrow screens the rail is
// dropped and the command palette (the topbar search) becomes the navigation.
export function AppShell() {
  const { pathname } = useLocation()
  const isMobile = useIsMobile()
  const current = navItems.find((i) =>
    i.path === "/" ? pathname === "/" : pathname === i.path || pathname.startsWith(`${i.path}/`),
  )

  return (
    <div className="flex h-svh flex-col bg-background text-foreground">
      <Topbar />
      <SidebarProvider
        className="min-h-0 flex-1"
        style={{ "--sidebar-width": "15rem" } as CSSProperties}
      >
        {isMobile ? null : <AppSidebar />}
        <SidebarInset className="min-w-0 bg-background">
          <main className="min-w-0 flex-1 overflow-y-auto px-4 py-6 sm:px-6 lg:px-7">
            <div className="mb-2 text-xs tracking-[0.04em] text-faint">
              <span className="font-semibold text-muted-foreground">Heimdall</span>
              {current ? <> · {current.title}</> : null}
            </div>
            <Outlet />
          </main>
        </SidebarInset>
      </SidebarProvider>
    </div>
  )
}
