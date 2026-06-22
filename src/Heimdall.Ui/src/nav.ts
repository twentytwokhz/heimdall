import {
  Activity,
  FileCode2,
  FlaskConical,
  Gauge,
  KeyRound,
  Package,
  Puzzle,
  Server,
  Variable,
  Workflow,
  type LucideIcon,
} from "lucide-react"

/** A ConfigView list field whose length is shown as the nav badge count. */
export type ConfigCountKey =
  | "apis"
  | "products"
  | "subscriptions"
  | "namedValues"
  | "backends"
  | "fragments"

export type NavItem = {
  title: string
  path: string
  icon: LucideIcon
  /** The config list this surface counts; the sidebar shows its live length as a badge. */
  countKey?: ConfigCountKey
  live?: boolean
  /** One-line description shown on the surface header (and on the placeholder until the view lands). */
  blurb: string
}

export type NavGroup = { label: string; items: NavItem[] }

// Single source of truth for both the sidebar and the route table, so they cannot drift.
export const navGroups: NavGroup[] = [
  {
    label: "Gateway",
    items: [
      {
        title: "Overview",
        path: "/",
        icon: Gauge,
        blurb: "Gateway health, loaded config counts, and recent activity at a glance.",
      },
      {
        title: "Live traffic",
        path: "/live",
        icon: Activity,
        live: true,
        blurb: "Watch requests stream across the Frontend, Inbound, Backend, and Outbound stages in real time.",
      },
      {
        title: "Playground",
        path: "/playground",
        icon: FlaskConical,
        blurb: "Replay requests against the local gateway, or import a Postman or .http collection.",
      },
    ],
  },
  {
    label: "Design",
    items: [
      {
        title: "APIs",
        path: "/apis",
        icon: Workflow,
        countKey: "apis",
        blurb: "Browse loaded APIs, their operations, and the effective policy on each stage.",
      },
      {
        title: "Products",
        path: "/products",
        icon: Package,
        countKey: "products",
        blurb: "Products that group APIs and gate access through subscriptions.",
      },
      {
        title: "Subscriptions",
        path: "/subscriptions",
        icon: KeyRound,
        countKey: "subscriptions",
        blurb: "Subscription keys and the products they unlock.",
      },
      {
        title: "Named values",
        path: "/named-values",
        icon: Variable,
        countKey: "namedValues",
        blurb: "Reusable named values and secrets referenced by policies.",
      },
      {
        title: "Backends",
        path: "/backends",
        icon: Server,
        countKey: "backends",
        blurb: "Backend services that operations forward to.",
      },
      {
        title: "Policy fragments",
        path: "/fragments",
        icon: Puzzle,
        countKey: "fragments",
        blurb: "Shared policy fragments included across APIs and operations.",
      },
      {
        title: "Policy authoring",
        path: "/authoring",
        icon: FileCode2,
        blurb: "Edit a policy's XML and hot-reload it into the live gateway, then replay to see the change.",
      },
    ],
  },
]

export const navItems: NavItem[] = navGroups.flatMap((g) => g.items)
