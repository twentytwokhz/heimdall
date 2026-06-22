import { Link, useLocation } from "react-router-dom"
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"
import { useConfig } from "@/lib/use-config"
import { navGroups } from "@/nav"

// Static left rail (collapsible="none" renders the plain, non-fixed sidebar column). Active item is
// driven by the router; the .nav-rail class adds the signature spectrum bar on the active row.
export function AppSidebar() {
  const { pathname } = useLocation()
  const { data } = useConfig()
  const isActive = (path: string) =>
    path === "/" ? pathname === "/" : pathname === path || pathname.startsWith(`${path}/`)

  return (
    <Sidebar collapsible="none" className="nav-rail border-r border-sidebar-border">
      <SidebarContent className="px-2 py-3">
        {navGroups.map((group) => (
          <SidebarGroup key={group.label}>
            <SidebarGroupLabel className="text-[10px] uppercase tracking-[0.24em] text-faint">
              {group.label}
            </SidebarGroupLabel>
            <SidebarMenu>
              {group.items.map((item) => (
                <SidebarMenuItem key={item.path}>
                  <SidebarMenuButton asChild isActive={isActive(item.path)} tooltip={item.title}>
                    <Link to={item.path}>
                      <item.icon />
                      <span>{item.title}</span>
                    </Link>
                  </SidebarMenuButton>
                  {item.live ? (
                    <SidebarMenuBadge className="font-mono text-green">live</SidebarMenuBadge>
                  ) : item.countKey && data ? (
                    <SidebarMenuBadge className="font-mono">
                      {data[item.countKey].length}
                    </SidebarMenuBadge>
                  ) : null}
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroup>
        ))}
      </SidebarContent>
    </Sidebar>
  )
}
