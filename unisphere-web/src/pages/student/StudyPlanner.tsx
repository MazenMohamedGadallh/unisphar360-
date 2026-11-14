import { useMemo, useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import type { StudyDayDto, StudyPlannerResponse } from '../../types/dto';

function pad(n: number) {
  return String(n).padStart(2, '0');
}
function ymd(d: Date) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
/** Sunday of the current week (same as DateTime.Today.AddDays(-(int)DayOfWeek)). */
function startOfWeek(d: Date): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  x.setDate(x.getDate() - x.getDay());
  return x;
}

export default function StudyPlanner() {
  usePageTitle('Study Planner');
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date()));
  const planner = useApi<StudyPlannerResponse>(`/api/study-planner?weekStart=${ymd(weekStart)}`);
  const [date, setDate] = useState(() => ymd(new Date()));
  const [title, setTitle] = useState('');
  const [hours, setHours] = useState('1');
  const [note, setNote] = useState('');
  const action = useAction();

  const shiftWeek = (days: number) =>
    setWeekStart((w) => {
      const n = new Date(w);
      n.setDate(n.getDate() + days);
      return n;
    });

  const toggle = (item: StudyDayDto) =>
    action.run(async () => {
      const updated = await api.post<StudyDayDto>(`/api/study-planner/${item.id}/toggle`);
      if (updated) planner.setData((p) => (p ? { ...p, days: p.days.map((d) => (d.id === item.id ? updated : d)) } : p));
      else await planner.reload();
    });

  const remove = (item: StudyDayDto) =>
    action.run(async () => {
      await api.del(`/api/study-planner/${item.id}`);
      await planner.reload();
    });

  const add = (e: FormEvent) => {
    e.preventDefault();
    if (!title.trim()) return;
    void action.run(async () => {
      await api.post('/api/study-planner', {
        date: `${date}T00:00:00`,
        title: title.trim(),
        hours: Number(hours) || 0,
        note: note.trim() || null,
      });
      setTitle('');
      setNote('');
      // Jump to the week that contains the new item so it is visible.
      setWeekStart(startOfWeek(new Date(`${date}T00:00:00`)));
      await planner.reload();
    });
  };

  const totalHours = useMemo(() => (planner.data?.days ?? []).reduce((sum, d) => sum + d.hours, 0), [planner.data]);
  const doneCount = (planner.data?.days ?? []).filter((d) => d.isDone).length;

  return (
    <>
      <PageHeader title="Study Planner" subtitle="Plan and track your weekly study activities." />
      <ErrorBox error={action.error} />

      <div className="card">
        <div className="card-header">
          <h3>Weekly Study Plan</h3>
          <div className="planner-nav">
            <button className="btn btn-sm btn-secondary" onClick={() => shiftWeek(-7)}>
              <Icon name="arrow-left" /> Prev
            </button>
            <span className="week-label">{planner.data?.weekLabel ?? 'Loading…'}</span>
            <button className="btn btn-sm btn-secondary" onClick={() => shiftWeek(7)}>
              Next <Icon name="arrow-right" />
            </button>
          </div>
        </div>
        <Async res={planner} loadingLabel="Loading study plan…" isEmpty={(p) => p.days.length === 0} emptyIcon="calendar-days" empty="Nothing planned this week. Add a study item below.">
          {(p) => (
            <>
              <p className="muted planner-summary">
                {doneCount} of {p.days.length} done · {totalHours} hours planned. Click a day to mark it done.
              </p>
              <div className="planner-grid">
                {p.days.map((item) => (
                  <div
                    key={item.id}
                    className={`planner-day ${item.isDone ? 'completed' : ''}`}
                    role="button"
                    tabIndex={0}
                    onClick={() => void toggle(item)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        void toggle(item);
                      }
                    }}
                  >
                    <div className="day-header">
                      <span className="day-name">{item.dayLabel}</span>
                      <span className="day-date">{item.dayNumber}</span>
                    </div>
                    <div className="day-content">
                      <h4>{item.title}</h4>
                      <p>{item.hours} hours</p>
                      {item.note && <small>{item.note}</small>}
                    </div>
                    <div className="day-status">
                      <span className={`status-icon ${item.isDone ? 'completed' : ''}`}>
                        <Icon name={item.isDone ? 'check' : 'circle'} />
                      </span>
                      <button
                        type="button"
                        className="icon-btn icon-btn-danger"
                        title="Delete item"
                        aria-label="Delete item"
                        onClick={(e) => {
                          e.stopPropagation();
                          void remove(item);
                        }}
                      >
                        <Icon name="trash-2" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </Async>
      </div>

      <div className="card section-gap-top">
        <div className="card-header">
          <h3>Add Study Item</h3>
        </div>
        <form onSubmit={add} className="form-inline">
          <div className="form-group">
            <label>Date</label>
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
          </div>
          <div className="form-group">
            <label>Title</label>
            <input type="text" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Study topic…" maxLength={200} required />
          </div>
          <div className="form-group">
            <label>Hours</label>
            <input type="number" value={hours} onChange={(e) => setHours(e.target.value)} min={0} max={12} required />
          </div>
          <div className="form-group">
            <label>Note</label>
            <input type="text" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Optional note…" maxLength={500} />
          </div>
          <button type="submit" className="btn btn-primary" disabled={action.busy}>
            {action.busy ? 'Saving…' : 'Add'}
          </button>
        </form>
      </div>
    </>
  );
}
