// Research Hub for doctors (and admins browsing): /api/research-hub
//   mine, filters, browse (q, universityId, field, year, page), {id}, {id}/pdf,
//   POST / PUT {id} as multipart (pdfFile ≤ 20 MB), PUT {id}/publish, DELETE {id}.

import { useState, type FormEvent, type ReactNode } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ConfirmDialog, ErrorBox, Modal, PageHeader, Pagination, Tabs } from '../../components/ui';
import { api, downloadFile, qs } from '../../lib/api';
import { useAuth } from '../../lib/auth';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { Link, useParams } from '../../lib/router';
import { fmtDate, splitList } from '../../lib/format';
import type { ResearchHubFiltersDto, ResearchHubItemDto, ResearchHubPageDto } from '../../types/dto';

const MAX_PDF = 20 * 1024 * 1024;

interface Form {
  title: string;
  abstract: string;
  authors: string;
  keywords: string;
  year: string;
  field: string;
  doiOrUrl: string;
  isPublished: boolean;
}

const blank = (): Form => ({ title: '', abstract: '', authors: '', keywords: '', year: String(new Date().getFullYear()), field: '', doiOrUrl: '', isPublished: true });
const hasHtml = (s: string) => s.includes('<') || s.includes('>');

function ResearchCard({ r, actions }: { r: ResearchHubItemDto; actions?: ReactNode }) {
  return (
    <div className="research-card">
      <div className="research-card-top">
        <span className="research-badge">{r.field || 'General'}</span>
        <span className="muted">{r.year}</span>
        {!r.isPublished && <span className="research-badge draft">Draft</span>}
        {r.hasPdf && (
          <span className="grad-badge">
            <Icon name="file-text" /> PDF
          </span>
        )}
      </div>
      <Link to={`/doctor/research/${r.id}`} className="research-card-title">
        {r.title}
      </Link>
      <div className="research-authors">{r.authors}</div>
      <div className="research-author-line">
        {r.authorName}
        {r.authorUniversity ? ` · ${r.authorUniversity}` : ''}
        {r.authorDepartment ? ` · ${r.authorDepartment}` : ''}
      </div>
      <p className="research-abstract-preview">{r.abstract.length > 260 ? `${r.abstract.slice(0, 260)}…` : r.abstract}</p>
      {r.keywords && (
        <div className="tag-list">
          {splitList(r.keywords)
            .slice(0, 6)
            .map((k, kIdx) => (
              <span className="tag" key={`${k}-${kIdx}`}>
                {k}
              </span>
            ))}
        </div>
      )}
      {actions && <div className="research-actions">{actions}</div>}
    </div>
  );
}

function PaperForm({ initial, editId, onDone, onCancel }: { initial: Form; editId: number | null; onDone: (msg: string) => void; onCancel: () => void }) {
  const [f, setF] = useState<Form>(initial);
  const [pdf, setPdf] = useState<File | null>(null);
  const action = useAction();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const problems: string[] = [];
    if (f.title.trim().length < 3) problems.push('Title must be at least 3 characters.');
    if (!f.abstract.trim()) problems.push('Abstract is required.');
    if (!f.authors.trim()) problems.push('Authors are required.');
    if (!f.keywords.trim()) problems.push('Keywords are required.');
    if (!f.field.trim()) problems.push('Field is required.');
    if ([f.title, f.abstract, f.authors, f.keywords, f.field].some(hasHtml)) problems.push('Use plain text only (no < or > characters).');
    const year = Number(f.year);
    if (!year || year < 1900 || year > new Date().getFullYear() + 1) problems.push('Enter a valid year.');
    if (pdf && (pdf.size > MAX_PDF || !pdf.name.toLowerCase().endsWith('.pdf'))) problems.push('The paper must be a PDF up to 20 MB.');
    if (problems.length) return action.setError(problems.join(' '));

    void action.run(async () => {
      const fd = new FormData();
      fd.append('title', f.title.trim());
      fd.append('abstract', f.abstract.trim());
      fd.append('authors', f.authors.trim());
      fd.append('keywords', f.keywords.trim());
      fd.append('year', String(year));
      fd.append('field', f.field.trim());
      fd.append('doiOrUrl', f.doiOrUrl.trim());
      fd.append('isPublished', f.isPublished ? 'true' : 'false');
      if (pdf) fd.append('pdfFile', pdf, pdf.name);
      if (editId) await api.upload(`/api/research-hub/${editId}`, fd, 'PUT');
      else await api.upload('/api/research-hub', fd);
      onDone(editId ? 'Paper updated.' : 'Paper uploaded.');
    });
  };

  return (
    <Modal title={editId ? 'Edit paper' : 'New paper'} onClose={onCancel} wide>
      <form onSubmit={submit}>
        <ErrorBox error={action.error} />
        <div className="form-group">
          <label>Title</label>
          <input value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} maxLength={300} required />
        </div>
        <div className="form-group">
          <label>Abstract</label>
          <textarea rows={5} value={f.abstract} onChange={(e) => setF({ ...f, abstract: e.target.value })} required />
        </div>
        <div className="form-row">
          <div className="form-group">
            <label>Authors</label>
            <input value={f.authors} onChange={(e) => setF({ ...f, authors: e.target.value })} placeholder="A. Hassan, M. Ali" required />
          </div>
          <div className="form-group">
            <label>Keywords</label>
            <input value={f.keywords} onChange={(e) => setF({ ...f, keywords: e.target.value })} placeholder="nlp, transformers" required />
          </div>
        </div>
        <div className="form-row">
          <div className="form-group">
            <label>Field</label>
            <input value={f.field} onChange={(e) => setF({ ...f, field: e.target.value })} placeholder="Artificial Intelligence" required />
          </div>
          <div className="form-group">
            <label>Year</label>
            <input type="number" value={f.year} onChange={(e) => setF({ ...f, year: e.target.value })} required />
          </div>
        </div>
        <div className="form-group">
          <label>DOI or URL (optional)</label>
          <input value={f.doiOrUrl} onChange={(e) => setF({ ...f, doiOrUrl: e.target.value })} placeholder="10.1000/xyz or https://…" />
        </div>
        <div className="form-group">
          <label>PDF {editId ? '(optional — replaces the current file)' : '(optional, max 20 MB)'}</label>
          <input type="file" accept="application/pdf,.pdf" onChange={(e) => setPdf(e.target.files?.[0] ?? null)} />
        </div>
        <label className="check-row">
          <input type="checkbox" checked={f.isPublished} onChange={(e) => setF({ ...f, isPublished: e.target.checked })} /> Publish (visible to other doctors)
        </label>
        <div className="modal-actions flush">
          <button type="button" className="btn btn-secondary" onClick={onCancel}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={action.busy}>
            {action.busy ? 'Uploading…' : editId ? 'Save changes' : 'Upload paper'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function Browse() {
  const filters = useApi<ResearchHubFiltersDto>('/api/research-hub/filters');
  const [form, setForm] = useState({ q: '', universityId: '', field: '', year: '' });
  const [applied, setApplied] = useState(form);
  const [page, setPage] = useState(1);
  const list = useApi<ResearchHubPageDto>(`/api/research-hub${qs({ ...applied, page })}`);

  return (
    <>
      <form
        className="card research-filter-card"
        onSubmit={(e) => {
          e.preventDefault();
          setPage(1);
          setApplied({ ...form });
        }}
      >
        <div className="research-filter-grid">
          <div className="form-group">
            <label>Search</label>
            <input value={form.q} onChange={(e) => setForm({ ...form, q: e.target.value })} placeholder="Title, author, keyword…" />
          </div>
          <div className="form-group">
            <label>University</label>
            <select value={form.universityId} onChange={(e) => setForm({ ...form, universityId: e.target.value })}>
              <option value="">All universities</option>
              {(filters.data?.universities ?? []).map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label>Field</label>
            <select value={form.field} onChange={(e) => setForm({ ...form, field: e.target.value })}>
              <option value="">All fields</option>
              {Array.from(new Set(filters.data?.fields ?? [])).map((f, fIdx) => (
                <option key={`${f}-${fIdx}`}>{f}</option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label>Year</label>
            <select value={form.year} onChange={(e) => setForm({ ...form, year: e.target.value })}>
              <option value="">Any year</option>
              {Array.from(new Set(filters.data?.years ?? [])).map((y, yIdx) => (
                <option key={`${y}-${yIdx}`}>{y}</option>
              ))}
            </select>
          </div>
        </div>
        <div className="research-filter-actions">
          <button className="btn btn-primary" type="submit">
            Search
          </button>
          <button
            className="btn btn-secondary"
            type="button"
            onClick={() => {
              const empty = { q: '', universityId: '', field: '', year: '' };
              setForm(empty);
              setApplied(empty);
              setPage(1);
            }}
          >
            Reset
          </button>
        </div>
      </form>
      <Async res={list} loadingLabel="Loading research…" isEmpty={(p) => p.items.length === 0} emptyIcon="microscope" empty="No published research matches these filters.">
        {(p) => (
          <>
            <p className="muted">{p.total} paper(s)</p>
            <div className="research-grid">
              {p.items.map((r) => (
                <ResearchCard key={r.id} r={r} />
              ))}
            </div>
            <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
          </>
        )}
      </Async>
    </>
  );
}

export default function DoctorResearch() {
  usePageTitle('Research Hub');
  const { role } = useAuth();
  const isDoctor = role === 'Doctor';
  const [tab, setTab] = useState<'mine' | 'browse'>(isDoctor ? 'mine' : 'browse');
  const mine = useApi<ResearchHubItemDto[]>(isDoctor ? '/api/research-hub/mine' : null);
  const [editing, setEditing] = useState<{ form: Form; id: number | null } | null>(null);
  const [deleting, setDeleting] = useState<ResearchHubItemDto | null>(null);
  const [flash, setFlash] = useFlash(5000);
  const action = useAction();

  const togglePublish = (r: ResearchHubItemDto) =>
    action.run(async () => {
      await api.put(`/api/research-hub/${r.id}/publish`, { isPublished: !r.isPublished });
      await mine.reload();
    });

  const remove = () =>
    action.run(async () => {
      if (!deleting) return;
      await api.del(`/api/research-hub/${deleting.id}`);
      setDeleting(null);
      await mine.reload();
    });

  return (
    <>
      <PageHeader
        title="Research Hub"
        subtitle={isDoctor ? 'Upload your papers (PDF up to 20 MB) and browse published research from every university.' : 'Browse published research from every university.'}
        actions={
          isDoctor && (
            <button className="btn btn-primary btn-sm" onClick={() => setEditing({ form: blank(), id: null })}>
              <Icon name="plus" /> New paper
            </button>
          )
        }
      />
      {isDoctor && (
        <Tabs
          tabs={[
            { key: 'mine', label: 'My Research' },
            { key: 'browse', label: 'Browse Research' },
          ]}
          value={tab}
          onChange={setTab}
        />
      )}
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}

      {tab === 'mine' && isDoctor ? (
        <Async res={mine} loadingLabel="Loading your papers…" emptyIcon="upload" empty="You haven't uploaded any research yet. Press “New paper” to add your first one.">
          {(list) => (
            <div className="research-grid">
              {list.map((r) => (
                <ResearchCard
                  key={r.id}
                  r={r}
                  actions={
                    <>
                      <button
                        className="btn btn-sm btn-secondary"
                        onClick={() =>
                          setEditing({
                            id: r.id,
                            form: { title: r.title, abstract: r.abstract, authors: r.authors, keywords: r.keywords, year: String(r.year), field: r.field, doiOrUrl: r.doiOrUrl ?? '', isPublished: r.isPublished },
                          })
                        }
                      >
                        Edit
                      </button>
                      <button className="btn btn-sm btn-secondary" onClick={() => void togglePublish(r)} disabled={action.busy}>
                        {r.isPublished ? 'Unpublish' : 'Publish'}
                      </button>
                      {r.hasPdf && (
                        <button className="btn btn-sm btn-secondary" onClick={() => void action.run(() => downloadFile(`/api/research-hub/${r.id}/pdf`, `${r.title}.pdf`))}>
                          <Icon name="download" /> PDF
                        </button>
                      )}
                      <button className="btn btn-sm btn-danger" onClick={() => setDeleting(r)}>
                        Delete
                      </button>
                    </>
                  }
                />
              ))}
            </div>
          )}
        </Async>
      ) : (
        <Browse />
      )}

      {editing && (
        <PaperForm
          initial={editing.form}
          editId={editing.id}
          onCancel={() => setEditing(null)}
          onDone={(msg) => {
            setEditing(null);
            setFlash(msg);
            void mine.reload();
          }}
        />
      )}
      {deleting && <ConfirmDialog title="Delete paper?" message={`Delete “${deleting.title}” and its PDF?`} busy={action.busy} onConfirm={() => void remove()} onCancel={() => setDeleting(null)} />}
    </>
  );
}

/** /doctor/research/:id */
export function ResearchDetails() {
  const { id } = useParams<{ id: string }>();
  const item = useApi<ResearchHubItemDto>(`/api/research-hub/${id}`);
  const action = useAction();
  usePageTitle(item.data?.title ?? 'Research');
  return (
    <>
      <div className="page-header">
        <Link to="/doctor/research" className="back-link">
          <Icon name="arrow-left" /> Back to Research Hub
        </Link>
      </div>
      <ErrorBox error={action.error} />
      <Async res={item} loadingLabel="Loading paper…">
        {(r) => (
          <div className="card research-detail">
            <div className="research-detail-head">
              <div className="research-card-top">
                <span className="research-badge">{r.field}</span>
                <span className="muted">{r.year}</span>
                {!r.isPublished && <span className="research-badge draft">Draft</span>}
              </div>
              <h1>{r.title}</h1>
              <div className="research-detail-authors">{r.authors}</div>
              <div className="research-detail-author">
                Uploaded by {r.authorName}
                {r.authorDepartment ? ` · ${r.authorDepartment}` : ''}
                {r.authorUniversity ? ` · ${r.authorUniversity}` : ''} · {fmtDate(r.createdAtUtc)}
              </div>
            </div>
            <div className="research-detail-section">
              <h3>Abstract</h3>
              <p className="research-full-abstract">{r.abstract}</p>
            </div>
            {r.keywords && (
              <div className="research-detail-section">
                <h3>Keywords</h3>
                <div className="tag-list">
                  {splitList(r.keywords).map((k, kIdx) => (
                    <span className="tag" key={`${k}-${kIdx}`}>
                      {k}
                    </span>
                  ))}
                </div>
              </div>
            )}
            <div className="research-actions">
              {r.hasPdf && (
                <button className="btn btn-primary" onClick={() => void action.run(() => downloadFile(`/api/research-hub/${r.id}/pdf`, `${r.title}.pdf`))} disabled={action.busy}>
                  <Icon name="download" /> {action.busy ? 'Downloading…' : 'Download PDF'}
                </button>
              )}
              {r.doiOrUrl && (
                <a className="btn btn-secondary" href={/^https?:/i.test(r.doiOrUrl) ? r.doiOrUrl : `https://doi.org/${r.doiOrUrl}`} target="_blank" rel="noopener noreferrer">
                  <Icon name="external-link" /> {r.doiOrUrl}
                </a>
              )}
            </div>
          </div>
        )}
      </Async>
    </>
  );
}
