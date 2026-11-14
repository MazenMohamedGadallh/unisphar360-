import { useMemo, useState, type FormEvent } from 'react';
import { Async, ErrorBox, PageHeader, Progress } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import { fmtDate, fromDateInput } from '../../lib/format';
import type { RoadmapItemDto, RoadmapProgressDto } from '../../types/dto';
import type { RoadmapSuggestionList } from '../../types/models';

export default function Roadmap() {
  usePageTitle('Career Roadmap');
  const items = useApi<RoadmapItemDto[]>('/api/grad/roadmap');
  const progress = useApi<RoadmapProgressDto>('/api/grad/roadmap/progress');
  const action = useAction();
  const [title, setTitle] = useState('');
  const [category, setCategory] = useState('');
  const [targetDate, setTargetDate] = useState('');
  const [notes, setNotes] = useState('');
  const [suggestions, setSuggestions] = useState<RoadmapSuggestionList['suggestions']>([]);
  const [suggestSource, setSuggestSource] = useState('');
  const [picked, setPicked] = useState<Set<number>>(new Set());

  const sorted = useMemo(
    () => [...(items.data ?? [])].sort((a, b) => Number(a.status === 'Completed') - Number(b.status === 'Completed') || a.sortOrder - b.sortOrder),
    [items.data],
  );

  const refresh = async () => {
    await Promise.all([items.reload(), progress.reload()]);
  };

  const add = (e: FormEvent) => {
    e.preventDefault();
    if (!title.trim()) return;
    void action.run(async () => {
      await api.post('/api/grad/roadmap', {
        title: title.trim(),
        category: category.trim() || null,
        status: 'NotStarted',
        notes: notes.trim() || null,
        targetDate: fromDateInput(targetDate),
        sortOrder: null,
      });
      setTitle('');
      setCategory('');
      setTargetDate('');
      setNotes('');
      await refresh();
    });
  };

  const setStatus = (item: RoadmapItemDto, status: string) =>
    action.run(async () => {
      await api.put(`/api/grad/roadmap/${item.id}`, {
        title: item.title,
        category: item.category,
        status,
        notes: item.notes,
        targetDate: item.targetDate,
        sortOrder: item.sortOrder,
      });
      await refresh();
    });

  const remove = (id: number) =>
    action.run(async () => {
      await api.del(`/api/grad/roadmap/${id}`);
      await refresh();
    });

  const suggest = () =>
    action.run(async () => {
      const res = await api.get<RoadmapSuggestionList>('/api/grad/roadmap/suggest');
      const list = res?.suggestions ?? [];
      if (list.length === 0) throw new Error('No AI suggestions available right now.');
      setSuggestions(list);
      setSuggestSource(res.source);
      setPicked(new Set(list.map((_, i) => i)));
    });

  const applySuggestions = () =>
    action.run(async () => {
      const chosen = suggestions.filter((_, i) => picked.has(i)).map((s) => ({ title: s.title, category: s.category, notes: s.notes, why: s.why }));
      if (chosen.length === 0) return;
      await api.post('/api/grad/roadmap/apply-suggestions', chosen);
      setSuggestions([]);
      setPicked(new Set());
      await refresh();
    });

  const p = progress.data;

  return (
    <>
      <PageHeader title="Career Roadmap" subtitle="Your step-by-step plan. Add steps yourself or let AI suggest them." />
      {p && (
        <div className="grad-overview-hero">
          <div className="hero-text">
            <h2>{p.percent}% complete</h2>
            <p>
              {p.completed} done · {p.inProgress} in progress · {p.notStarted} not started
            </p>
          </div>
          <div className="hero-meter">
            <Progress value={p.percent} />
          </div>
        </div>
      )}

      <ErrorBox error={action.error} />

      <form className="card filter-card" onSubmit={add}>
        <div className="form-inline">
          <div className="form-group">
            <label htmlFor="grRoadTitle">New step title</label>
            <input id="grRoadTitle" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Earn an AWS certificate" maxLength={200} />
          </div>
          <div className="form-group">
            <label htmlFor="grRoadCategory">Category</label>
            <input id="grRoadCategory" value={category} onChange={(e) => setCategory(e.target.value)} placeholder="Skill" maxLength={50} />
          </div>
          <div className="form-group">
            <label htmlFor="grRoadTarget">Target date</label>
            <input id="grRoadTarget" type="date" value={targetDate} onChange={(e) => setTargetDate(e.target.value)} />
          </div>
          <div className="form-group">
            <label htmlFor="grRoadNotes">Notes</label>
            <input id="grRoadNotes" value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Optional" maxLength={1000} />
          </div>
          <button className="btn btn-primary" type="submit" disabled={!title.trim() || action.busy}>
            Add step
          </button>
          <button className="btn btn-secondary" type="button" onClick={() => void suggest()} disabled={action.busy}>
            {action.busy ? 'Thinking…' : 'AI suggestions'}
          </button>
        </div>
      </form>

      {suggestions.length > 0 && (
        <div className="card section-gap">
          <div className="card-header">
            <h3>AI suggestions ({suggestions.length})</h3>
            {suggestSource && <span className="muted">source: {suggestSource}</span>}
          </div>
          <p className="muted">Review then add the ones that make sense for you.</p>
          {suggestions.map((s, i) => (
            <label className="check-row" key={i}>
              <input
                type="checkbox"
                checked={picked.has(i)}
                onChange={(e) => {
                  const next = new Set(picked);
                  if (e.target.checked) next.add(i);
                  else next.delete(i);
                  setPicked(next);
                }}
              />
              <span>
                <strong>{s.title}</strong> <span className="grad-badge">{s.category}</span>
                {s.notes && <span className="muted block">{s.notes}</span>}
                {s.why && <span className="muted block">Why: {s.why}</span>}
              </span>
            </label>
          ))}
          <div className="form-actions">
            <button className="btn btn-primary btn-sm" onClick={() => void applySuggestions()} disabled={picked.size === 0 || action.busy}>
              Add selected ({picked.size})
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => setSuggestions([])}>
              Discard
            </button>
          </div>
        </div>
      )}

      <div className="card">
        <Async res={items} loadingLabel="Loading roadmap…" emptyIcon="map" empty='No roadmap steps yet. Add a first step or press "AI suggestions".'>
          {() => (
            <ul className="grad-list">
              {sorted.map((item) => (
                <li key={item.id} className={`row-between ${item.status === 'Completed' ? 'done' : ''}`}>
                  <div>
                    <strong>{item.title}</strong> <span className="grad-badge">{item.category || 'General'}</span>
                    {item.origin === 'AiSuggestion' && <span className="grad-badge warn">AI</span>}
                    {item.targetDate && <span className="muted"> · target {fmtDate(item.targetDate)}</span>}
                    {item.completedAtUtc && <span className="muted"> · completed {fmtDate(item.completedAtUtc)}</span>}
                    {item.notes && <div className="muted">{item.notes}</div>}
                  </div>
                  <div className="row-actions">
                    <select value={item.status} onChange={(e) => void setStatus(item, e.target.value)} disabled={action.busy} aria-label="Status">
                      <option value="NotStarted">Not started</option>
                      <option value="InProgress">In progress</option>
                      <option value="Completed">Completed</option>
                    </select>
                    <button className="btn btn-sm btn-danger" onClick={() => void remove(item.id)} disabled={action.busy}>
                      Remove
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Async>
      </div>
    </>
  );
}
