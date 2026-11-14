import { useMemo, useState, type FormEvent } from 'react';
import { Async, ConfirmDialog, ErrorBox, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import { fmtDate, fromDateInput, toDateInput } from '../../lib/format';
import type { ApplicationDto, ApplicationSummaryDto } from '../../types/dto';

const STATUSES = ['Applied', 'Interview', 'Offer', 'Rejected', 'Withdrawn'];

export default function Applications() {
  usePageTitle('My Applications');
  const items = useApi<ApplicationDto[]>('/api/grad/applications');
  const summary = useApi<ApplicationSummaryDto>('/api/grad/applications/summary');
  const action = useAction();
  const [form, setForm] = useState({ company: '', jobTitle: '', sourceUrl: '', status: 'Applied', followUp: '', notes: '' });
  const [deleting, setDeleting] = useState<ApplicationDto | null>(null);
  const [filter, setFilter] = useState('');

  const sorted = useMemo(
    () =>
      [...(items.data ?? [])]
        .filter((a) => !filter || a.status === filter)
        .sort(
          (a, b) =>
            Number(a.status === 'Rejected' || a.status === 'Withdrawn') - Number(b.status === 'Rejected' || b.status === 'Withdrawn') ||
            b.updatedAtUtc.localeCompare(a.updatedAtUtc),
        ),
    [items.data, filter],
  );

  const refresh = () => Promise.all([items.reload(), summary.reload()]);

  const add = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      await api.post('/api/grad/applications', {
        jobListingId: null,
        companyName: form.company.trim(),
        jobTitle: form.jobTitle.trim(),
        sourceUrl: form.sourceUrl.trim() || null,
        status: form.status,
        notes: form.notes.trim() || null,
        followUpDate: fromDateInput(form.followUp),
        appliedAtUtc: null,
      });
      setForm({ company: '', jobTitle: '', sourceUrl: '', status: 'Applied', followUp: '', notes: '' });
      await refresh();
    });
  };

  const update = (app: ApplicationDto, patch: Partial<{ status: string; followUpDate: string | null; notes: string | null }>) =>
    action.run(async () => {
      await api.put(`/api/grad/applications/${app.id}`, {
        status: patch.status ?? app.status,
        notes: patch.notes !== undefined ? patch.notes : app.notes,
        followUpDate: patch.followUpDate !== undefined ? patch.followUpDate : app.followUpDate,
      });
      await refresh();
    });

  const remove = () =>
    action.run(async () => {
      if (!deleting) return;
      await api.del(`/api/grad/applications/${deleting.id}`);
      setDeleting(null);
      await refresh();
    });

  const s = summary.data;

  return (
    <>
      <PageHeader title="My Applications" subtitle="Track every application, interview and follow-up in one place" />
      {s && (
        <div className="grad-stat-grid">
          <div className="card stat-card">
            <div className="stat-value">{s.total}</div>
            <div className="stat-label">Total applications</div>
          </div>
          {s.byStatus.map((sc, i) => (
            <div className="card stat-card" key={`${sc.status}-${i}`}>
              <div className="stat-value">{sc.count}</div>
              <div className="stat-label">{sc.status}</div>
            </div>
          ))}
          <div className="card stat-card">
            <div className="stat-value">{s.upcomingFollowUps}</div>
            <div className="stat-label">Follow-ups due</div>
          </div>
        </div>
      )}

      <form className="card section-gap" onSubmit={add}>
        <div className="card-header">
          <h3>Add an application</h3>
        </div>
        <p className="muted">Link a catalog job from Find Jobs (“Mark as applied”), or enter it manually for companies you applied to outside UniSphere.</p>
        <div className="form-inline">
          <div className="form-group">
            <label htmlFor="grAppCompany">
              Company<span className="required-mark">*</span>
            </label>
            <input id="grAppCompany" value={form.company} onChange={(e) => setForm({ ...form, company: e.target.value })} placeholder="Acme Inc." maxLength={200} />
          </div>
          <div className="form-group">
            <label htmlFor="grAppTitle">
              Job title<span className="required-mark">*</span>
            </label>
            <input id="grAppTitle" value={form.jobTitle} onChange={(e) => setForm({ ...form, jobTitle: e.target.value })} placeholder="Backend Developer" maxLength={200} />
          </div>
          <div className="form-group">
            <label htmlFor="grAppUrl">Source URL</label>
            <input id="grAppUrl" value={form.sourceUrl} onChange={(e) => setForm({ ...form, sourceUrl: e.target.value })} placeholder="https://jobs.example.com/321" />
          </div>
        </div>
        <div className="form-inline">
          <div className="form-group">
            <label htmlFor="grAppStatus">Status</label>
            <select id="grAppStatus" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })}>
              {STATUSES.map((st, stIdx) => (
                <option key={`${st}-${stIdx}`}>{st}</option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="grAppFollow">Follow-up date</label>
            <input id="grAppFollow" type="date" value={form.followUp} onChange={(e) => setForm({ ...form, followUp: e.target.value })} />
          </div>
          <div className="form-group">
            <label htmlFor="grAppNotes">Notes</label>
            <input id="grAppNotes" value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} placeholder="Recruiter: Sara, phone screen Friday" />
          </div>
          <button className="btn btn-primary" type="submit" disabled={!form.company.trim() || !form.jobTitle.trim() || action.busy}>
            Add
          </button>
        </div>
      </form>

      <ErrorBox error={action.error} />

      <div className="card">
        <div className="card-header">
          <h3>Applications</h3>
          <select value={filter} onChange={(e) => setFilter(e.target.value)} aria-label="Filter by status">
            <option value="">All statuses</option>
            {STATUSES.map((st, stIdx) => (
              <option key={`${st}-${stIdx}`}>{st}</option>
            ))}
          </select>
        </div>
        <Async res={items} loadingLabel="Loading applications…" emptyIcon="briefcase" empty='No applications yet. Add one above or "Mark as applied" from Find Jobs.'>
          {() => (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Role</th>
                    <th>Company</th>
                    <th>Status</th>
                    <th>Applied</th>
                    <th>Follow-up</th>
                    <th>Updated</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {sorted.map((app) => (
                    <tr key={app.id}>
                      <td>
                        <strong>{app.jobTitle}</strong>
                        {app.sourceUrl?.startsWith('http') && (
                          <div>
                            <a href={app.sourceUrl} target="_blank" rel="noopener noreferrer">
                              Posting
                            </a>
                          </div>
                        )}
                        {app.notes && <div className="muted">{app.notes}</div>}
                      </td>
                      <td>{app.companyName}</td>
                      <td>
                        <select value={app.status} onChange={(e) => void update(app, { status: e.target.value })} disabled={action.busy} aria-label="Status">
                          {STATUSES.map((st, stIdx) => (
                            <option key={`${st}-${stIdx}`}>{st}</option>
                          ))}
                        </select>
                      </td>
                      <td className="muted">{fmtDate(app.appliedAtUtc)}</td>
                      <td>
                        <input
                          type="date"
                          className="input-compact"
                          value={toDateInput(app.followUpDate)}
                          onChange={(e) => void update(app, { followUpDate: fromDateInput(e.target.value) })}
                          aria-label="Follow-up date"
                        />
                      </td>
                      <td className="muted">{fmtDate(app.updatedAtUtc)}</td>
                      <td>
                        <button className="btn btn-sm btn-danger" onClick={() => setDeleting(app)}>
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </div>

      {deleting && (
        <ConfirmDialog
          title="Delete application?"
          message={
            <>
              Remove <strong>{deleting.jobTitle}</strong> at {deleting.companyName} from your tracker?
            </>
          }
          busy={action.busy}
          onConfirm={() => void remove()}
          onCancel={() => setDeleting(null)}
        />
      )}
    </>
  );
}
