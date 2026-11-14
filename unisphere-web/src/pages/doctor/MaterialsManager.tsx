// Doctor material management on /api/doctor/materials:
//   GET  /api/doctor/materials?courseId&type&search&page&pageSize   (paged list)
//   POST /api/doctor/materials                       (create, courseId in body)
//   PUT  /api/doctor/materials/{id}                  (update)
//   DELETE /api/doctor/materials/{id}
//   POST/DELETE /api/doctor/materials/{id}/file      (attachment: pdf/doc/docx/ppt/pptx ≤ 20 MB)
// Used by /doctor/materials (all courses) and the course page (fixed course).

import { useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ConfirmDialog, ErrorBox, Modal, Pagination } from '../../components/ui';
import { api, downloadFile, qs } from '../../lib/api';
import { useAction, useApi, useDebounced, useFlash } from '../../lib/hooks';
import { fmtBytes, fmtDate, fmtDateTime, fromDateTimeInput, toDateTimeInput } from '../../lib/format';
import type { DoctorCourseDto, DoctorMaterialDto, MaterialDto, PagedResultDto } from '../../types/dto';

export const MATERIAL_TYPES = ['Summary', 'Lecture', 'Assignment', 'Exam', 'Announcement'];
const ATTACH_EXT = ['.pdf', '.doc', '.docx', '.ppt', '.pptx'];
const PAGE_SIZE = 10;

interface Draft {
  id: number | null;
  courseId: string;
  title: string;
  type: string;
  description: string;
  content: string;
  fileUrl: string;
  deadline: string;
}

const emptyDraft = (courseId: string): Draft => ({ id: null, courseId, title: '', type: 'Summary', description: '', content: '', fileUrl: '', deadline: '' });

export function MaterialsManager({ fixedCourseId, courses }: { fixedCourseId?: number; courses?: DoctorCourseDto[] }) {
  const [courseId, setCourseId] = useState(fixedCourseId ? String(fixedCourseId) : '');
  const [type, setType] = useState('');
  const [search, setSearch] = useState('');
  const term = useDebounced(search, 350);
  const [page, setPage] = useState(1);
  const list = useApi<PagedResultDto<MaterialDto>>(`/api/doctor/materials${qs({ courseId, type, search: term.trim(), page, pageSize: PAGE_SIZE })}`);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [deleting, setDeleting] = useState<MaterialDto | null>(null);
  const [flash, setFlash] = useFlash();
  const action = useAction();

  const openCreate = () => {
    setFile(null);
    setDraft(emptyDraft(courseId || (courses?.[0] ? String(courses[0].id) : '')));
  };

  const openEdit = (m: MaterialDto) =>
    action.run(async () => {
      // The course endpoint carries the deadline, which the paged list omits.
      const full = (await api.get<DoctorMaterialDto[]>(`/api/doctor/courses/${m.courseId}/materials`)).find((x) => x.id === m.id);
      setFile(null);
      setDraft({
        id: m.id,
        courseId: String(m.courseId),
        title: m.title,
        type: m.type,
        description: m.description ?? '',
        content: m.content ?? '',
        fileUrl: m.fileUrl ?? '',
        deadline: toDateTimeInput(full?.deadlineUtc ?? null),
      });
    });

  const save = (e: FormEvent) => {
    e.preventDefault();
    if (!draft) return;
    if (file && !ATTACH_EXT.some((x) => file.name.toLowerCase().endsWith(x))) {
      action.setError('Attachments must be PDF, Word (.doc/.docx) or PowerPoint (.ppt/.pptx).');
      return;
    }
    if (file && file.size > 20 * 1024 * 1024) {
      action.setError('Attachments are limited to 20 MB.');
      return;
    }
    void action.run(async () => {
      const body = {
        title: draft.title.trim(),
        type: draft.type,
        description: draft.description.trim() || null,
        content: draft.content.trim() || null,
        fileUrl: draft.fileUrl.trim() || null,
        deadlineUtc: fromDateTimeInput(draft.deadline),
      };
      let id = draft.id;
      if (id) await api.put(`/api/doctor/materials/${id}`, body);
      else {
        const created = await api.post<MaterialDto>('/api/doctor/materials', { ...body, courseId: Number(draft.courseId) });
        id = created.id;
      }
      if (file && id) {
        const fd = new FormData();
        fd.append('file', file, file.name);
        await api.upload(`/api/doctor/materials/${id}/file`, fd);
      }
      setDraft(null);
      setFlash(draft.id ? 'Material updated.' : 'Material published.');
      await list.reload();
    });
  };

  const removeFile = (m: MaterialDto) =>
    action.run(async () => {
      await api.del(`/api/doctor/materials/${m.id}/file`);
      setFlash('Attachment removed.');
      await list.reload();
    });

  const remove = () =>
    action.run(async () => {
      if (!deleting) return;
      await api.del(`/api/doctor/materials/${deleting.id}`);
      setDeleting(null);
      setFlash('Material deleted.');
      await list.reload();
    });

  const download = (m: MaterialDto) => action.run(() => downloadFile(`/api/materials/${m.id}/file`, m.uploadedFileName ?? m.title));

  return (
    <div className="card">
      <div className="card-header">
        <h3>Materials &amp; Assignments</h3>
        <button className="btn btn-primary btn-sm" onClick={openCreate} disabled={!fixedCourseId && (courses?.length ?? 0) === 0}>
          <Icon name="plus" /> New material
        </button>
      </div>
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <div className="form-inline">
        {!fixedCourseId && (
          <div className="form-group">
            <label htmlFor="matCourse">Course</label>
            <select
              id="matCourse"
              value={courseId}
              onChange={(e) => {
                setCourseId(e.target.value);
                setPage(1);
              }}
            >
              <option value="">All my courses</option>
              {(courses ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} — {c.displayTitle || c.title} ({c.materialsCount})
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="form-group">
          <label htmlFor="matType">Type</label>
          <select
            id="matType"
            value={type}
            onChange={(e) => {
              setType(e.target.value);
              setPage(1);
            }}
          >
            <option value="">All types</option>
            {MATERIAL_TYPES.map((t, tIdx) => (
              <option key={`${t}-${tIdx}`}>{t}</option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="matSearch">Search</label>
          <input
            id="matSearch"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder="Title or description…"
          />
        </div>
      </div>
      <Async res={list} loadingLabel="Loading materials…" isEmpty={(p) => p.items.length === 0} emptyIcon="file-text" empty="No materials yet. Add a lecture summary, assignment or exam.">
        {(p) => (
          <>
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Title</th>
                    {!fixedCourseId && <th>Course</th>}
                    <th>Type</th>
                    <th>Attachment</th>
                    <th>Created</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {p.items.map((m) => (
                    <tr key={m.id}>
                      <td>
                        <strong>{m.title}</strong>
                        {m.description && <div className="rec-track">{m.description}</div>}
                        {m.fileUrl && (
                          <div>
                            <a href={m.fileUrl} target="_blank" rel="noopener noreferrer">
                              Link <Icon name="external-link" />
                            </a>
                          </div>
                        )}
                      </td>
                      {!fixedCourseId && <td>{m.courseName}</td>}
                      <td>
                        <span className="status-badge good">{m.type}</span>
                      </td>
                      <td>
                        {m.hasUploadedFile ? (
                          <div className="row-actions">
                            <button className="btn-link" onClick={() => void download(m)} title="Download">
                              <Icon name="download" /> {m.uploadedFileName} {m.uploadedFileSizeBytes ? `(${fmtBytes(m.uploadedFileSizeBytes)})` : ''}
                            </button>
                            <button className="icon-btn icon-btn-danger" title="Remove attachment" aria-label="Remove attachment" onClick={() => void removeFile(m)}>
                              <Icon name="x" />
                            </button>
                          </div>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td>{fmtDate(m.createdAt)}</td>
                      <td>
                        <div className="row-actions">
                          <button className="btn btn-sm btn-secondary" onClick={() => void openEdit(m)} disabled={action.busy}>
                            Edit
                          </button>
                          <button className="btn btn-sm btn-danger" onClick={() => setDeleting(m)}>
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
          </>
        )}
      </Async>

      {draft && (
        <Modal title={draft.id ? 'Edit material' : 'New material'} onClose={() => setDraft(null)} wide>
          <form onSubmit={save}>
            {!fixedCourseId && !draft.id && (
              <div className="form-group">
                <label>Course</label>
                <select value={draft.courseId} onChange={(e) => setDraft({ ...draft, courseId: e.target.value })} required>
                  <option value="">Select a course…</option>
                  {(courses ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.code} — {c.displayTitle || c.title}
                    </option>
                  ))}
                </select>
              </div>
            )}
            <div className="form-row">
              <div className="form-group">
                <label>Title</label>
                <input value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} maxLength={200} required />
              </div>
              <div className="form-group">
                <label>Type</label>
                <select value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value })}>
                  {MATERIAL_TYPES.map((t, tIdx) => (
                    <option key={`${t}-${tIdx}`}>{t}</option>
                  ))}
                </select>
              </div>
            </div>
            <div className="form-group">
              <label>Description</label>
              <input value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} maxLength={2000} />
            </div>
            <div className="form-group">
              <label>Content (for summaries / lectures — also used by the AI assistant)</label>
              <textarea rows={6} value={draft.content} onChange={(e) => setDraft({ ...draft, content: e.target.value })} />
            </div>
            <div className="form-row">
              <div className="form-group">
                <label>Deadline (assignments / exams)</label>
                <input type="datetime-local" value={draft.deadline} onChange={(e) => setDraft({ ...draft, deadline: e.target.value })} />
              </div>
              <div className="form-group">
                <label>External link (optional)</label>
                <input value={draft.fileUrl} onChange={(e) => setDraft({ ...draft, fileUrl: e.target.value })} placeholder="https://…" maxLength={500} />
              </div>
            </div>
            <div className="form-group">
              <label>Attach a file (PDF, Word or PowerPoint, max 20 MB)</label>
              <input type="file" accept={ATTACH_EXT.join(',')} onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
              {file && <span className="field-hint">Selected: {file.name}</span>}
            </div>
            <div className="modal-actions flush">
              <button type="button" className="btn btn-secondary" onClick={() => setDraft(null)}>
                Cancel
              </button>
              <button type="submit" className="btn btn-primary" disabled={action.busy || !draft.title.trim() || !draft.courseId}>
                {action.busy ? 'Saving…' : draft.id ? 'Save changes' : 'Publish'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete material?"
          message={
            <>
              Delete “{deleting.title}”? This cannot be undone. Created {fmtDateTime(deleting.createdAt)}.
            </>
          }
          busy={action.busy}
          onConfirm={() => void remove()}
          onCancel={() => setDeleting(null)}
        />
      )}
    </div>
  );
}
