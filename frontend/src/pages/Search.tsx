import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface SearchResultItem {
  id?: number;
  title: string;
  subtitle: string;
  route: string;
  icon?: string;
}

interface SearchGroup {
  type: string;
  label: string;
  items: SearchResultItem[];
}

export function Search() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const urlQuery = searchParams.get("q") ?? "";

  const [input, setInput] = useState(urlQuery);
  const [groups, setGroups] = useState<SearchGroup[]>([]);
  const [activeFilter, setActiveFilter] = useState<string>("all");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    document.title = "Search — UniSphere 360+";
  }, []);

  useEffect(() => {
    setInput(urlQuery);
  }, [urlQuery]);

  const runSearch = useCallback(
    async (queryText: string) => {
      const q = queryText.trim();
      if (q.length < 2) {
        setGroups([]);
        setError(null);
        return;
      }
      setLoading(true);
      setError(null);
      try {
        const res = await fetch(`${API_BASE_URL}/api/search?q=${encodeURIComponent(q)}&take=50`, {
          headers: authHeaders(),
        });
        if (res.status === 401) {
          clearToken();
          navigate("/login?expired=1");
          return;
        }
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const json = (await res.json()) as { groups?: SearchGroup[] };
        setGroups(json.groups ?? []);
      } catch {
        setError("Could not search across UniSphere right now.");
      } finally {
        setLoading(false);
      }
    },
    [navigate],
  );

  useEffect(() => {
    void runSearch(urlQuery);
  }, [urlQuery, runSearch]);

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = input.trim();
    if (trimmed) {
      setSearchParams({ q: trimmed });
    } else {
      setSearchParams({});
      setGroups([]);
    }
  }

  const totalMatches = groups.reduce((acc, g) => acc + g.items.length, 0);
  const visibleGroups = activeFilter === "all" ? groups : groups.filter((g) => g.type === activeFilter);

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Global Search</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Search courses, lecture notes, assignments, and workspaces across your UniSphere account.
          </p>
        </div>
        <Button size="sm" variant="secondary" to="/chat">
          <UiIcon name="sparkles" />
          Ask AI Assistant Instead
        </Button>
      </div>

      <Card className="mb-6 p-4">
        <form onSubmit={onSubmit} className="flex flex-wrap items-center gap-3">
          <div className="relative flex-1 min-w-[220px]">
            <span className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-text-secondary">
              <UiIcon name="search" />
            </span>
            <input
              type="search"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Search by course code (CS350), lecture topic, exam, or workspace…"
              className="w-full rounded-control border border-border bg-bg py-2.5 pr-4 pl-10 text-sm text-text outline-none focus:border-primary"
              autoFocus
            />
          </div>
          <button
            type="submit"
            className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-5 py-2.5 text-sm font-semibold"
          >
            <UiIcon name="search" />
            Search
          </button>
        </form>

        {groups.length > 0 && (
          <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-border pt-3">
            <button
              type="button"
              onClick={() => setActiveFilter("all")}
              className={`cursor-pointer rounded-md border px-3 py-1 text-xs font-semibold transition ${
                activeFilter === "all"
                  ? "border-primary bg-primary text-white"
                  : "border-border bg-bg text-text-secondary hover:text-text"
              }`}
            >
              All ({totalMatches})
            </button>
            {groups.map((g) => (
              <button
                key={g.type}
                type="button"
                onClick={() => setActiveFilter(g.type)}
                className={`cursor-pointer rounded-md border px-3 py-1 text-xs font-semibold transition ${
                  activeFilter === g.type
                    ? "border-primary bg-primary text-white"
                    : "border-border bg-bg text-text-secondary hover:text-text"
                }`}
              >
                {g.label} ({g.items.length})
              </button>
            ))}
          </div>
        )}
      </Card>

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void runSearch(urlQuery)} />
        </Card>
      ) : loading ? (
        <LoadingBlock label={`Searching for “${urlQuery}”…`} />
      ) : urlQuery.trim().length < 2 ? (
        <Card>
          <EmptyState
            icon="search"
            message="Type at least 2 characters above to search courses, lecture materials, and UniSphere workspaces."
          />
        </Card>
      ) : visibleGroups.length === 0 ? (
        <Card>
          <EmptyState
            icon="search"
            message={`No matches found for “${urlQuery}”. Try a course code like CS350, “exam”, “algorithms”, or “interview”.`}
          />
        </Card>
      ) : (
        <div className="flex flex-col gap-5">
          {visibleGroups.map((group) => (
            <Card key={group.type} className="overflow-hidden">
              <CardHeader
                title={`${group.label} (${group.items.length})`}
                action={<span className="text-xs text-text-secondary">Click any result to open</span>}
              />
              <div className="divide-y divide-border">
                {group.items.map((item, idx) => (
                  <button
                    key={`${group.type}-${item.id ?? idx}-${item.title}`}
                    type="button"
                    onClick={() => navigate(item.route)}
                    className="flex w-full cursor-pointer items-center justify-between gap-4 border-0 bg-transparent px-5 py-3.5 text-left transition hover:bg-primary-light/40"
                  >
                    <div className="flex min-w-0 items-center gap-3">
                      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary-light text-primary">
                        <UiIcon name={item.icon || "file-text"} />
                      </span>
                      <div className="min-w-0">
                        <div className="truncate text-sm font-semibold text-text">{item.title}</div>
                        <div className="truncate text-xs text-text-secondary">{item.subtitle}</div>
                      </div>
                    </div>
                    <span className="inline-flex shrink-0 items-center gap-1 text-xs font-medium text-primary">
                      Open
                      <UiIcon name="chevron-right" />
                    </span>
                  </button>
                ))}
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
