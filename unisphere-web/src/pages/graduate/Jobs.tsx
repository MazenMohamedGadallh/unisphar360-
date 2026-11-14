import { useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, PageHeader, Pagination, Tabs } from '../../components/ui';
import { api, qs } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import { useSearchParams } from '../../lib/router';
import { fmtDate } from '../../lib/format';
import type { JobListingDto, PagedJobsDto } from '../../types/dto';

interface SyncResult {
  imported: number;
  throttled: boolean;
  error: string | null;
  lastSyncUtc: string | null;
}

const PAGE_SIZE = 8;

export function JobRow({ job, onToggleSave, onApply, busy }: { job: JobListingDto; onToggleSave: () => void; onApply: () => void; busy: boolean }) {
  return (
    <div className="job-card">
      <div className="job-main">
        <strong className="job-title">{job.title}</strong>
        <div className="muted">
          {job.companyName} · {job.location || 'Worldwide'} {job.remote && <span className="grad-badge">Remote</span>}
        </div>
        <div className="muted">
          {job.jobType}
          {job.category ? ` · ${job.category}` : ''}
          {job.salary ? ` · ${job.salary}` : ''} · posted {fmtDate(job.publishedAtUtc)} · <span className="grad-badge">{job.source}</span>
        </div>
        {job.keySkills.length > 0 && (
          <div className="tag-list">
            {job.keySkills.slice(0, 6).map((s, sIdx) => (
              <span className="grad-badge" key={`${s}-${sIdx}`}>
                {s}
              </span>
            ))}
          </div>
        )}
        {job.snippet && (
          <p className="muted job-snippet">
            {job.snippet.length > 240 ? `${job.snippet.slice(0, 240)}…` : job.snippet}
          </p>
        )}
      </div>
      <div className="job-actions">
        <button className="btn btn-sm btn-secondary" onClick={onToggleSave} disabled={busy}>
          {job.isSaved ? (
            <>
              <Icon name="check" /> Saved
            </>
          ) : (
            'Save'
          )}
        </button>
        {job.isApplied ? (
          <span className="grad-badge">Applied</span>
        ) : (
          <button className="btn btn-sm btn-primary" onClick={onApply} disabled={busy}>
            Mark as applied
          </button>
        )}
        {job.url && (
          <a className="btn btn-sm btn-secondary" href={job.url} target="_blank" rel="noopener noreferrer">
            Open <Icon name="external-link" />
          </a>
        )}
      </div>
    </div>
  );
}

export default function Jobs() {
  usePageTitle('Find Jobs');
  const [params, setParams] = useSearchParams();
  const view = params.get('view') === 'saved' ? 'saved' : 'all';
  const [form, setForm] = useState({ search: '', location: '', remote: '', sort: '' });
  const [filters, setFilters] = useState(form);
  const [page, setPage] = useState(1);
  const action = useAction();
  const [syncMessage, setSyncMessage] = useState<string | null>(null);

  const jobs = useApi<PagedJobsDto>(view === 'all' ? `/api/grad/jobs${qs({ ...filters, page, pageSize: PAGE_SIZE })}` : null);
  const saved = useApi<JobListingDto[]>(view === 'saved' ? '/api/grad/jobs/saved' : null);

  const reload = () => (view === 'all' ? jobs.reload() : saved.reload());

  const search = (e: FormEvent) => {
    e.preventDefault();
    setPage(1);
    setFilters({ ...form });
  };

  const reset = () => {
    const empty = { search: '', location: '', remote: '', sort: '' };
    setForm(empty);
    setFilters(empty);
    setPage(1);
  };

  const toggleSave = (job: JobListingDto) =>
    action.run(async () => {
      if (job.isSaved) await api.del(`/api/grad/jobs/${job.id}/save`);
      else await api.post(`/api/grad/jobs/${job.id}/save`);
      await reload();
    });

  const apply = (job: JobListingDto) =>
    action.run(async () => {
      await api.post('/api/grad/applications', { jobListingId: job.id, status: 'Applied' });
      await reload();
    });

  const sync = () =>
    action.run(async () => {
      setSyncMessage(null);
      const r = await api.get<SyncResult>('/api/grad/jobs/sync');
      setSyncMessage(r.error ? r.error : r.throttled ? 'Synced recently. Next sync soon.' : `Imported ${r.imported} new jobs.`);
      await reload();
    });

  const list = (items: JobListingDto[]) => (
    <div className="job-list">
      {items.map((j) => (
        <JobRow key={j.id} job={j} busy={action.busy} onToggleSave={() => void toggleSave(j)} onApply={() => void apply(j)} />
      ))}
    </div>
  );

  return (
    <>
      <PageHeader
        title="Find Jobs"
        subtitle="Search the catalog and sync fresh remote openings"
        actions={
          <button className="btn btn-sm btn-secondary" onClick={() => void sync()} disabled={action.busy}>
            <Icon name="refresh-cw" /> Sync jobs
          </button>
        }
      />
      <Tabs
        tabs={[
          { key: 'all', label: 'All jobs' },
          { key: 'saved', label: 'Saved jobs' },
        ]}
        value={view}
        onChange={(v) => setParams({ view: v === 'saved' ? 'saved' : null })}
      />
      {syncMessage && (
        <p>
          <span className="grad-badge">{syncMessage}</span>
        </p>
      )}
      <ErrorBox error={action.error} />

      {view === 'all' ? (
        <>
          <form className="form-inline card filter-card" onSubmit={search}>
            <div className="form-group">
              <label htmlFor="grJobSearch">Keyword</label>
              <input id="grJobSearch" value={form.search} onChange={(e) => setForm({ ...form, search: e.target.value })} placeholder="backend, react, data…" />
            </div>
            <div className="form-group">
              <label htmlFor="grJobLocation">Location</label>
              <input id="grJobLocation" value={form.location} onChange={(e) => setForm({ ...form, location: e.target.value })} placeholder="Cairo or Remote" />
            </div>
            <div className="form-group">
              <label htmlFor="grJobRemote">Work mode</label>
              <select id="grJobRemote" value={form.remote} onChange={(e) => setForm({ ...form, remote: e.target.value })}>
                <option value="">Any</option>
                <option value="true">Remote only</option>
                <option value="false">Onsite / hybrid</option>
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="grJobSort">Sort</label>
              <select id="grJobSort" value={form.sort} onChange={(e) => setForm({ ...form, sort: e.target.value })}>
                <option value="">Newest first</option>
                <option value="title">By title</option>
                <option value="company">By company</option>
                <option value="oldest">Oldest first</option>
              </select>
            </div>
            <button className="btn btn-primary" type="submit">
              Search
            </button>
            <button className="btn btn-secondary" type="button" onClick={reset}>
              Reset
            </button>
          </form>
          <Async res={jobs} loadingLabel="Loading jobs…" isEmpty={(p) => p.jobs.length === 0} emptyIcon="briefcase" empty='No jobs match your search. Press "Sync jobs" to pull fresh remote openings.'>
            {(p) => (
              <>
                <p className="muted">{p.total} listings</p>
                {list(p.jobs)}
                <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
              </>
            )}
          </Async>
        </>
      ) : (
        <Async res={saved} loadingLabel="Loading saved jobs…" emptyIcon="bookmark" empty='No saved jobs yet. Browse the "All jobs" tab and press Save to bookmark a listing.'>
          {(items) => list(items)}
        </Async>
      )}
    </>
  );
}
