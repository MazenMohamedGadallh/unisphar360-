import { Fragment, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { API_BASE_URL, authHeaders } from "../lib/api";

/*
 * Topbar live search — React port of the search block in MainLayout.razor.
 * Same API (GET /api/search?q=...&take=20), same 300ms debounce, same
 * recent-searches behavior (top 5, stored in "unisphere.recentSearches"),
 * same keyboard support (Escape / ArrowUp / ArrowDown / Enter).
 */

interface SearchItemDto {
  id?: number | null;
  title: string;
  subtitle: string;
  route: string;
  icon: string;
}

interface SearchGroupDto {
  type: string;
  label: string;
  items: SearchItemDto[];
}

interface SearchResponseDto {
  groups: SearchGroupDto[];
}

const RECENTS_KEY = "unisphere.recentSearches";
const MAX_RECENTS = 5;

const SEARCH_ITEM =
  "flex cursor-pointer items-center gap-2.5 rounded-control px-3 py-2";
const SEARCH_ITEM_SELECTED = `${SEARCH_ITEM} bg-bg`;

function loadRecents(): string[] {
  try {
    const stored = localStorage.getItem(RECENTS_KEY);
    const parsed: unknown = stored ? JSON.parse(stored) : [];
    return Array.isArray(parsed) ? (parsed as string[]).slice(0, MAX_RECENTS) : [];
  } catch {
    return [];
  }
}

function saveRecents(recents: string[]) {
  try {
    localStorage.setItem(RECENTS_KEY, JSON.stringify(recents));
  } catch {
    // Recents are best-effort.
  }
}

/** Shows the search term with the match highlighted (like the Razor search). */
function Highlight({ text, term }: { text: string; term: string }) {
  const trimmed = term.trim();
  const index = trimmed ? text.toLowerCase().indexOf(trimmed.toLowerCase()) : -1;
  if (index < 0) return <>{text}</>;
  return (
    <>
      {text.slice(0, index)}
      <mark className="bg-primary-light text-primary">{text.slice(index, index + trimmed.length)}</mark>
      {text.slice(index + trimmed.length)}
    </>
  );
}

export function TopbarSearch() {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [focused, setFocused] = useState(false);
  // The user closed the dropdown (overlay click / Escape / result picked).
  // Typing or refocusing the input opens it again.
  const [dismissed, setDismissed] = useState(false);
  const [expanded, setExpanded] = useState(false); // mobile: full-width panel
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [groups, setGroups] = useState<SearchGroupDto[]>([]);
  const [selectedIndex, setSelectedIndex] = useState(-1);
  const [recents, setRecents] = useState<string[]>(loadRecents);

  // Mirror for event handlers that need the newest list (addRecent can fire
  // twice in a row before the state update lands).
  const recentsRef = useRef(recents);
  useEffect(() => {
    recentsRef.current = recents;
  }, [recents]);

  const term = query.trim();
  const flat = groups.flatMap((group) =>
    group.items.map((item) => ({ group: group.label, item })),
  );
  const showDropdown =
    !dismissed && focused && (term.length > 0 || recents.length > 0);

  // Debounced lookup against /api/search (same as the Blazor topbar).
  useEffect(() => {
    const currentTerm = query.trim();
    if (currentTerm.length < 2) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      void (async () => {
        setLoading(true);
        setError(null);
        try {
          const response = await fetch(
            `${API_BASE_URL}/api/search?q=${encodeURIComponent(currentTerm)}&take=20`,
            { headers: authHeaders(), signal: controller.signal },
          );
          if (response.status === 401) {
            // Quietly close: the layout owns session expiry.
            setDismissed(true);
            return;
          }
          if (!response.ok) throw new Error("search failed");
          const data = (await response.json()) as SearchResponseDto;
          setGroups(data.groups ?? []);
        } catch (thrown) {
          if ((thrown as Error).name === "AbortError") return;
          setError("Search failed. Please try again.");
          setGroups([]);
        } finally {
          setLoading(false);
        }
      })();
    }, 300);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query]);

  function addRecent(term: string) {
    if (!term) return;
    const next = recentsRef.current
      .filter((recent) => recent.toLowerCase() !== term.toLowerCase());
    next.unshift(term);
    const capped = next.slice(0, MAX_RECENTS);
    recentsRef.current = capped;
    setRecents(capped);
    saveRecents(capped);
  }

  function closeSearch() {
    setDismissed(true);
    setExpanded(false);
  }

  function selectResult(item: SearchItemDto) {
    addRecent(query.trim());
    closeSearch();
    navigate(item.route);
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (!showDropdown) return;
    if (event.key === "Escape") {
      closeSearch();
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      setSelectedIndex(flat.length === 0 ? -1 : Math.min(selectedIndex + 1, flat.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setSelectedIndex(Math.max(selectedIndex - 1, -1));
    } else if (event.key === "Enter") {
      if (selectedIndex >= 0 && selectedIndex < flat.length) {
        selectResult(flat[selectedIndex].item);
      } else if (term.length >= 2) {
        addRecent(term);
        closeSearch();
        navigate("/search?q=" + encodeURIComponent(term));
      }
    }
  }

  // Dropdown content, built as a row list so group headers stay simple.
  const rows: ReactNode[] = [];
  if (error) {
    rows.push(
      <div key="error" className="m-1 rounded-control border border-danger bg-danger-light px-4 py-3 text-sm text-danger">
        {error}
      </div>,
    );
  } else if (term.length === 0 && recents.length > 0) {
    rows.push(
      <div key="recents-title" className="px-3 pb-1 pt-2 text-[11px] font-bold uppercase text-text-secondary">
        Recent searches
      </div>,
    );
    recents.forEach((recent) => {
      rows.push(
        <div
          key={`recent-${recent}`}
          className={SEARCH_ITEM}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => {
            setQuery(recent);
            setDismissed(false);
          }}
        >
          <span className="flex flex-none items-center justify-center text-base text-text-secondary"><UiIcon name="history" /></span>
          <span className="flex min-w-0 flex-col text-[13px]">{recent}</span>
        </div>,
      );
    });
  } else if (term.length < 2) {
    rows.push(
      <div key="hint" className="p-10 text-center text-sm text-text-secondary">
        Type at least 2 characters to search.
      </div>,
    );
  } else if (flat.length === 0 && !loading) {
    rows.push(
      <div key="empty" className="p-10 text-center text-sm text-text-secondary">
        No results for &quot;{query}&quot;.
      </div>,
    );
  } else {
    let lastGroup = "";
    flat.forEach((entry, index) => {
      if (entry.group !== lastGroup) {
        lastGroup = entry.group;
        rows.push(
          <div key={`group-${lastGroup}`} className="px-3 pb-1 pt-2 text-[11px] font-bold uppercase text-text-secondary">
            {lastGroup}
          </div>,
        );
      }
      rows.push(
        <Fragment key={`${index}-${entry.item.route}`}>
          <div
            role="option"
            aria-selected={index === selectedIndex}
            className={index === selectedIndex ? SEARCH_ITEM_SELECTED : SEARCH_ITEM}
            onMouseDown={(event) => event.preventDefault()}
            onClick={() => selectResult(entry.item)}
          >
            <span className="flex flex-none items-center justify-center text-base text-text-secondary"><UiIcon name={entry.item.icon} /></span>
            <span className="flex min-w-0 flex-col text-[13px]">
              <Highlight text={entry.item.title} term={query} />
              <small className="truncate text-xs text-text-secondary">{entry.item.subtitle}</small>
            </span>
          </div>
        </Fragment>,
      );
    });
  }

  return (
    <>
      {/* Small screens: icon that expands the search into a panel below the topbar. */}
      <button
        type="button"
        className="inline-flex min-h-11 min-w-11 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent p-2.5 text-xl text-text-secondary transition-colors hover:bg-bg hover:text-text sm:hidden"
        onClick={() => setExpanded((value) => !value)}
        aria-label={expanded ? "Close search" : "Open search"}
      >
        <UiIcon name={expanded ? "x" : "search"} />
      </button>

      <div
        className={expanded
          ? "fixed inset-x-3 top-[var(--height-topbar)] z-40 flex flex-col"
          : "relative hidden min-w-0 max-w-[500px] flex-1 sm:flex"}
        onKeyDown={handleKeyDown}
      >
        <div className="flex min-w-0 flex-1 items-center gap-2 rounded-control border border-border bg-bg px-4 py-2">
          <span className="flex flex-none items-center justify-center text-base text-text-secondary"><UiIcon name="search" /></span>
          <input
            id="topbar-search"
            type="text"
            placeholder="Search courses, events, modules..."
            className="min-w-0 flex-1 border-0 bg-transparent text-sm text-text outline-none"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setDismissed(false);
              setSelectedIndex(-1);
            }}
            onFocus={() => {
              setFocused(true);
              setDismissed(false);
            }}
            autoComplete="off"
            role="combobox"
            aria-expanded={showDropdown}
            aria-label="Search"
            aria-controls="topbar-search-list"
          />
          {loading && (
            <span className="h-3.5 w-3.5 flex-none animate-search-spin rounded-full border-2 border-border border-t-primary" aria-hidden="true" />
          )}
        </div>

        {showDropdown && (
          <>
            <div
              className="absolute inset-x-0 top-full z-50 mt-2 max-h-[60vh] overflow-y-auto rounded-card bg-surface p-2 border border-border shadow-card-lg"
              id="topbar-search-list"
              role="listbox"
              aria-label="Search results"
            >
              {rows}
            </div>
            {/* Click-away layer keeps the dropdown open while the input has focus. */}
            <div className="fixed inset-0 z-40 bg-transparent" onClick={closeSearch} aria-hidden="true" />
          </>
        )}
      </div>
    </>
  );
}
