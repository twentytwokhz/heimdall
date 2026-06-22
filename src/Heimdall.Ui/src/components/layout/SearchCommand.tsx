import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import { Search } from "lucide-react"
import { navGroups } from "@/nav"

// Topbar search built on the shadcn Command palette: a search-field-looking trigger that opens a
// CommandDialog (also reachable via Cmd/Ctrl+K). For now it navigates between console surfaces; it
// grows to search APIs/operations/traces when those surfaces wire to the admin API.
export function SearchCommand() {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "k" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        setOpen((o) => !o)
      }
    }
    document.addEventListener("keydown", onKey)
    return () => document.removeEventListener("keydown", onKey)
  }, [])

  const go = (path: string) => {
    setOpen(false)
    navigate(path)
  }

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-label="Search"
        className="flex h-9 w-9 min-w-0 items-center justify-center gap-2 rounded-[10px] border border-border bg-[rgba(8,11,20,0.6)] text-[13px] text-faint transition-colors hover:border-input focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring sm:w-[200px] sm:justify-start sm:px-3 lg:w-[280px]"
      >
        <Search className="size-[15px] shrink-0" />
        <span className="hidden flex-1 truncate text-left sm:block">Search APIs, operations, policies…</span>
        <kbd className="pointer-events-none hidden shrink-0 rounded border border-border bg-card px-1.5 text-[10px] text-faint lg:inline-block">
          ⌘K
        </kbd>
      </button>

      <CommandDialog
        open={open}
        onOpenChange={setOpen}
        title="Search"
        description="Jump to a console surface"
      >
        <CommandInput placeholder="Search APIs, operations, policies…" />
        <CommandList>
          <CommandEmpty>No results found.</CommandEmpty>
          {navGroups.map((group) => (
            <CommandGroup key={group.label} heading={group.label}>
              {group.items.map((item) => (
                <CommandItem
                  key={item.path}
                  value={`${group.label} ${item.title}`}
                  onSelect={() => go(item.path)}
                >
                  <item.icon />
                  <span>{item.title}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          ))}
        </CommandList>
      </CommandDialog>
    </>
  )
}
