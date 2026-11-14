// Course announcements: GET/POST /api/doctor/courses/{id}/announcements,
// PUT/DELETE /api/doctor/announcements/{id}. With no fixed course, lists the
// announcements of every assigned course (same as Pages/Doctor/Announcements.razor).

import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, ConfirmDialog, Empty, ErrorBox, Loading, Modal } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useAction, useFlash } from '../../lib/hooks';
import { fmtDateTime } from '../../lib/format';
import type { DoctorAnnouncementDto, DoctorCourseDto } from '../../types/dto';

interface Draft {
  id: number | null;
  courseId: string;
  title: string;
  body: string;
  category: string;
}

export function AnnouncementsManager({ fixedCourseId, courses }: { fixedCourseId?: number; courses?: DoctorCourseDto[] }) {
  const [courseFilter, setCourseFilter] = useState('');
  const [items, setItems] = useState<DoctorAnnouncementDto[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleting, setDeleting] = useState<DoctorAnnouncementDto | null>(null);
  const [flash, setFlash] = useFlash();
  const action = useAction();

  const courseIds = fixedCourseId ? [fixedCourseId] : courseFilter ? [Number(courseFilter)] : (courses ?? []).map((c) => c.id);
  const key = courseIds.join(',');

  const load = useCallback(async () => {
    setLoadError(null);
    const ids = key ? key.split(',').map(Number) : [];
    try {
      const lists = await Promise.all(ids.map((id) => api.get<DoctorAnnouncementDto[]>(`/api/doctor/courses/${id}/announcements`)));
      const seen = new Set<number>();
      setItems(
        lists
          .flat()
          .filter((a) => (seen.has(a.id) ? false : (seen.add(a.id), true)))
          .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc)),
      );
    } catch (err) {
      setLoadError(errorMessage(err));
      setItems([]);
    }
  }, [key]);

  useEffect(() => {
    void load();
  }, [load]);

  const save = (e: FormEvent) => {
    e.preventDefault();
    if (!draft) return;
    void action.run(async () => {
      const body = { title: draft.title.trim(), body: draft.body.trim(), category: draft.category.trim() || null };
      if (draft.id) await api.put(`/api/doctor/announcements/${draft.id}`, body);
      else await api.post(`/api/doctor/courses/${draft.courseId}/announcements`, body);
      setFlash(draft.id ? 'Announcement updated.' : 'Announcement published to enrolled students.');
      setDraft(null);
      await load();
    });
  };

  const remove = () =>
    action.run(async () => {
      if (!deleting) return;
      await api.del(`/api/doctor/announcements/${deleting.id}`);
      setDeleting(null);
      await load();
    });

  return (
    <div className="card">
      <div className="card-header">
        <h3>Course Announcements</h3>
        <button
          className="btn btn-primary btn-sm"
          disabled={!fixedCourseId && (courses?.length ?? 0) === 0}
          onClick={() => setDraft({ id: null, courseId: String(fixedCourseId ?? (courseFilter || (courses?.[0]?.id ?? ''))), title: '', body: '', category: 'general' })}
        >
          <Icon name="plus" /> New Announcement
        </button>
      </div>
      <ErrorBox error={action.error || loadError} onRetry={loadError ? () => void load() : undefined} />
      {flash && <Alert kind="success">{flash}</Alert>}
      {!fixedCourseId && (
        <div className="form-inline">
          <div className="form-group">
            <label htmlFor="annCourse">Course</label>
            <select id="annCourse" value={courseFilter} onChange={(e) => setCourseFilter(e.target.value)}>
              <option value="">All my courses</option>
              {(courses ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} — {c.displayTitle || c.title}
                </option>
              ))}
            </select>
          </div>
        </div>
      )}
      {items === null ? (
        <Loading label="Loading announcements…" />
      ) : items.length === 0 ? (
        <Empty icon="megaphone">No announcements yet.</Empty>
      ) : (
        <div className="announcement-list">
          {items.map((a) => (
            <div className="announcement-item" key={a.id}>
              <div className="announcement-header">
                <h4>{a.title}</h4>
                <span className="announcement-category">
                  {a.category || 'general'}
                  {!fixedCourseId && a.courseName ? ` - ${a.courseName}` : ''}
                </span>
              </div>
              <p>{a.body}</p>
              <div className="row-between">
                <span className="announcement-date">{fmtDateTime(a.createdAtUtc)}</span>
                {a.isMine && (
                  <div className="row-actions">
                    <button
                      className="btn btn-sm btn-secondary"
                      onClick={() => setDraft({ id: a.id, courseId: String(a.courseId ?? ''), title: a.title, body: a.body, category: a.category ?? '' })}
                    >
                      Edit
                    </button>
                    <button className="btn btn-sm btn-danger" onClick={() => setDeleting(a)}>
                      Delete
                    </button>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {draft && (
        <Modal title={draft.id ? 'Edit announcement' : 'New announcement'} onClose={() => setDraft(null)}>
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
            <div className="form-group">
              <label>Title</label>
              <input value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} maxLength={200} required />
            </div>
            <div className="form-group">
              <label>Body</label>
              <textarea rows={5} value={draft.body} onChange={(e) => setDraft({ ...draft, body: e.target.value })} maxLength={5000} required />
            </div>
            <div className="form-group">
              <label>Category</label>
              <input value={draft.category} onChange={(e) => setDraft({ ...draft, category: e.target.value })} placeholder="general" maxLength={50} />
            </div>
            <div className="modal-actions flush">
              <button type="button" className="btn btn-secondary" onClick={() => setDraft(null)}>
                Cancel
              </button>
              <button type="submit" className="btn btn-primary" disabled={action.busy || !draft.title.trim() || !draft.body.trim() || !draft.courseId}>
                {action.busy ? 'Saving…' : draft.id ? 'Save changes' : 'Publish Announcement'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {deleting && (
        <ConfirmDialog title="Delete announcement?" message={`Delete “${deleting.title}”? Students will no longer see it.`} busy={action.busy} onConfirm={() => void remove()} onCancel={() => setDeleting(null)} />
      )}
    </div>
  );
}
