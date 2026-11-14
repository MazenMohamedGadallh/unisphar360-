import { useEffect, useRef, useState } from 'react';
import { Icon } from '../../components/Icon';
import { ErrorBox, Loading, PageHeader } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useDebounced, usePageTitle } from '../../lib/hooks';
import { useNavigate, useSearchParams } from '../../lib/router';
import type { SearchGroupDto, SearchResponseDto } from '../../types/dto';

function Highlight({ text, term }: { text: string; term: string }) {
  const t = term.trim();
  const i = t ? text.toLowerCase().indexOf(t.toLowerCase()) : -1;
  if (i < 0) return <>{text}</>;
  return (
    <>
      {text.slice(0, i)}
      <mark>{text.slice(i, i + t.length)}</mark>
      {text.slice(i + t.length)}
    </>
  );
}

export default function Search() {
  usePageTitle('Search');
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [query, setQuery] = useState(params.get('q') ?? '');
  const term = useDebounced(query.trim(), 300);
  const [groups, setGroups] = useState<SearchGroupDto[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const seq = useRef(0);
  const pushed = useRef<string | null>(null);

  // Keep the box in sync when the top bar navigates here with a new ?q=
  // (ignore URL updates this page made itself while typing).
  const urlQ = params.get('q') ?? '';
  useEffect(() => {
    if (urlQ !== pushed.current) setQuery(urlQ);
  }, [urlQ]);

  useEffect(() => {
    if (term !== urlQ) {
      pushed.current = term;
      setParams({ q: term || null });
    }
    if (term.length < 2) {
      setGroups(null);
      setError(null);
      return;
    }
    const id = ++seq.current;
    setLoading(true);
    setError(null);
    api
      .get<SearchResponseDto>(`/api/search?q=${encodeURIComponent(term)}&take=20`)
      .then((res) => id === seq.current && setGroups(res?.groups ?? []))
      .catch((err) => id === seq.current && setError(errorMessage(err, 'Search failed. Check your connection and try again.')))
      .finally(() => id === seq.current && setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [term, attempt]);

  const total = (groups ?? []).reduce((n, g) => n + g.items.length, 0);

  return (
    <>
      <PageHeader title="Search" subtitle="Search across courses, materials, plans, notifications and pages." />
      <div className="search-section">
        <div className="search-box-large">
          <span className="search-icon">
            <Icon name="search" />
          </span>
          <input type="text" placeholder="Type to search…" value={query} onChange={(e) => setQuery(e.target.value)} autoFocus aria-label="Search" />
        </div>
      </div>
      <ErrorBox error={error} onRetry={() => setAttempt((a) => a + 1)} />
      {!error && query.trim().length > 0 && (
        <div className="card">
          <div className="card-header">
            <h3>
              Results for “{query.trim()}” ({total})
            </h3>
          </div>
          {query.trim().length < 2 ? (
            <div className="empty-state">Type at least 2 characters.</div>
          ) : loading || groups === null ? (
            <Loading label="Searching…" />
          ) : groups.length === 0 || total === 0 ? (
            <div className="empty-state">
              <Icon name="search" /> No results found for “{query.trim()}”.
            </div>
          ) : (
            groups.map((g, gi) => (
              <div key={`${g.type}-${gi}`}>
                <div className="search-group-label">{g.label}</div>
                <div className="search-results">
                  {g.items.map((r) => (
                    <div
                      key={`${r.route}-${r.title}`}
                      className="search-result-item"
                      role="link"
                      tabIndex={0}
                      onClick={() => navigate(r.route)}
                      onKeyDown={(e) => e.key === 'Enter' && navigate(r.route)}
                    >
                      <span className="result-type">
                        <Icon name={r.icon} /> {g.type}
                      </span>
                      <div className="result-info">
                        <h4>
                          <Highlight text={r.title} term={query} />
                        </h4>
                        <p>{r.subtitle}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>
      )}
    </>
  );
}
