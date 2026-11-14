// Research Hub for students and graduates (GET /api/research/trending,
// /api/research/papers?q=, /api/research/summarize/{id}).

import { useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, Modal, PageHeader } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useApi, usePageTitle } from '../../lib/hooks';
import { fmtDate } from '../../lib/format';
import type { PaperSummary, ResearchPaper } from '../../types/models';

function PaperList({ papers, onOpen }: { papers: ResearchPaper[]; onOpen: (p: ResearchPaper) => void }) {
  return (
    <div className="paper-list">
      {papers.map((p) => (
        <div className="paper-item clickable" key={p.id} role="button" tabIndex={0} onClick={() => onOpen(p)} onKeyDown={(e) => e.key === 'Enter' && onOpen(p)}>
          <div className="paper-title">{p.title}</div>
          <div className="paper-authors">{p.authors}</div>
          {p.abstract && (
            <div className="paper-abstract">
              {p.abstract.slice(0, 200)}
              {p.abstract.length > 200 ? '…' : ''}
            </div>
          )}
          <div className="paper-meta">
            {p.source && <span className="paper-source">{p.source}</span>}
            {p.publishedDate && <span className="paper-date">{fmtDate(p.publishedDate, { month: 'short', year: 'numeric' })}</span>}
          </div>
        </div>
      ))}
    </div>
  );
}

export default function PaperHub() {
  usePageTitle('Research Hub');
  const trending = useApi<ResearchPaper[]>('/api/research/trending');
  const [input, setInput] = useState('');
  const [query, setQuery] = useState('');
  const results = useApi<ResearchPaper[]>(query ? `/api/research/papers?q=${encodeURIComponent(query)}` : null);
  const [selected, setSelected] = useState<ResearchPaper | null>(null);
  const [summary, setSummary] = useState<string | null>(null);
  const [summarizing, setSummarizing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const search = (e: FormEvent) => {
    e.preventDefault();
    setQuery(input.trim());
  };

  const open = (p: ResearchPaper) => {
    setSelected(p);
    setSummary(null);
    setError(null);
  };

  const summarize = async () => {
    if (!selected) return;
    setSummarizing(true);
    setError(null);
    try {
      const res = await api.get<PaperSummary>(`/api/research/summarize/${selected.id}`);
      setSummary(res.summary);
    } catch (err) {
      setError(errorMessage(err, 'Could not summarize this paper.'));
    } finally {
      setSummarizing(false);
    }
  };

  return (
    <>
      <PageHeader title="Research Hub" subtitle="Discover, search, and summarize academic papers." />

      <form className="search-section filter-row" onSubmit={search}>
        <div className="search-box-large">
          <span className="search-icon">
            <Icon name="search" />
          </span>
          <input
            type="text"
            placeholder="Search research papers… (press Enter)"
            value={input}
            onChange={(e) => {
              setInput(e.target.value);
              if (!e.target.value.trim()) setQuery('');
            }}
          />
        </div>
        <button type="submit" className="btn btn-primary">
          Search
        </button>
      </form>

      {!query ? (
        <div className="card section-gap">
          <div className="card-header">
            <h3>Trending Papers</h3>
          </div>
          <Async res={trending} loadingLabel="Loading trending papers…" emptyIcon="microscope" empty="No trending papers available.">
            {(list) => <PaperList papers={list} onOpen={open} />}
          </Async>
        </div>
      ) : (
        <div className="card section-gap">
          <div className="card-header">
            <h3>Search Results ({results.data?.length ?? 0})</h3>
            <button
              className="btn btn-sm btn-secondary"
              onClick={() => {
                setQuery('');
                setInput('');
              }}
            >
              Clear
            </button>
          </div>
          <Async res={results} loadingLabel="Searching…" emptyIcon="search" empty={`No papers found matching "${query}".`}>
            {(list) => <PaperList papers={list} onOpen={open} />}
          </Async>
        </div>
      )}

      {selected && (
        <Modal title={selected.title} onClose={() => setSelected(null)} wide>
          <div className="paper-authors">{selected.authors}</div>
          {selected.abstract && <div className="paper-full-abstract">{selected.abstract}</div>}
          <ErrorBox error={error} />
          <div className="paper-actions">
            <button className="btn btn-primary" onClick={() => void summarize()} disabled={summarizing}>
              <Icon name="file-text" /> {summarizing ? 'Summarizing…' : 'Summarize'}
            </button>
            {selected.url && (
              <a href={selected.url} target="_blank" rel="noopener noreferrer" className="btn btn-secondary">
                View Original
              </a>
            )}
          </div>
          {summary && (
            <div className="summary-result">
              <h4>Summary</h4>
              <p>{summary}</p>
            </div>
          )}
        </Modal>
      )}
    </>
  );
}
