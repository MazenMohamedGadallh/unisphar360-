import { useState } from 'react';
import { Alert, Async, ErrorBox, Modal, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import type { DoctorProfileCourseDto, DoctorStudentDto } from '../../types/dto';

export function studentStatusClass(status: string): string {
  return status === 'Excellent' ? 'excellent' : status === 'Needs Attention' ? 'needs-attention' : 'good';
}

function Roster({ course, onClose }: { course: DoctorProfileCourseDto; onClose: () => void }) {
  const roster = useApi<DoctorStudentDto[]>(`/api/doctor/courses/${course.courseId}/students`);
  return (
    <Modal title={`Students — ${course.code}`} onClose={onClose} footer={<button className="btn btn-secondary" onClick={onClose}>Close</button>}>
      <p className="rec-track">Enrolled students in your university. Read-only list.</p>
      <Async res={roster} loadingLabel="Loading students…" emptyIcon="users" empty="No students are enrolled in this course yet.">
        {(list) => (
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Year</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {list.map((s) => (
                  <tr key={s.enrollmentId}>
                    <td>{s.fullName}</td>
                    <td>{s.academicYear ?? '—'}</td>
                    <td>
                      <span className={`status-badge ${studentStatusClass(s.status)}`}>{s.status}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Async>
    </Modal>
  );
}

export default function DoctorCourses() {
  usePageTitle('My Courses');
  const courses = useApi<DoctorProfileCourseDto[]>('/api/doctor/my-courses');
  const [editingId, setEditingId] = useState<number | null>(null);
  const [title, setTitle] = useState('');
  const [roster, setRoster] = useState<DoctorProfileCourseDto | null>(null);
  const [flash, setFlash] = useFlash(5000);
  const action = useAction();

  const saveTitle = (c: DoctorProfileCourseDto) => {
    const t = title.trim();
    if (t && t.length < 3) {
      action.setError('Display title must be at least 3 characters.');
      return;
    }
    void action.run(async () => {
      await api.put(`/api/doctor/courses/${c.courseId}/display-title`, { displayTitle: t || null });
      setFlash(t ? `Students now see "${t}".` : 'Display title cleared — students will see the catalog title.');
      setEditingId(null);
      await courses.reload();
    });
  };

  return (
    <>
      <PageHeader title="My Courses" subtitle="Courses you are assigned to teach. Access is verified by the server on every request." />
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <div className="card">
        <div className="card-header">
          <h3>Assigned Courses</h3>
        </div>
        <Async res={courses} loadingLabel="Loading courses…" emptyIcon="book-open" empty="You are not assigned to any course yet. Contact an administrator.">
          {(list) => (
            <div className="course-grid">
              {list.map((c) => (
                <div className="card course-card static" key={c.courseId}>
                  <div className="rec-code">{c.code}</div>
                  {editingId === c.courseId ? (
                    <>
                      <div className="form-group">
                        <label>Display title (3-120 characters, plain text)</label>
                        <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} disabled={action.busy} />
                        <small className="field-hint">Leave empty to fall back to the catalog title “{c.catalogTitle}”.</small>
                      </div>
                      <div className="form-inline">
                        <button className="btn btn-sm btn-primary" onClick={() => saveTitle(c)} disabled={action.busy}>
                          {action.busy ? 'Saving…' : 'Save title'}
                        </button>
                        <button className="btn btn-sm btn-secondary" onClick={() => setEditingId(null)}>
                          Cancel
                        </button>
                      </div>
                    </>
                  ) : (
                    <>
                      <h3 className="course-card-title">
                        {c.displayTitle || c.catalogTitle} {c.displayTitle && <small className="rec-track">(your display title)</small>}
                      </h3>
                      <p className="rec-track">Official title: {c.catalogTitle}</p>
                    </>
                  )}
                  <p className="rec-track">Term: {c.semester}</p>
                  <button type="button" className="count-link" onClick={() => setRoster(c)} title="View the student list">
                    {c.enrolledCount} student{c.enrolledCount === 1 ? '' : 's'}
                  </button>
                  <div className="form-inline">
                    <button
                      className="btn btn-sm btn-secondary"
                      onClick={() => {
                        setEditingId(c.courseId);
                        setTitle(c.displayTitle ?? c.catalogTitle);
                      }}
                    >
                      Rename for students
                    </button>
                    <Link to={`/doctor/courses/${c.courseId}`} className="btn btn-primary btn-sm">
                      Open course
                    </Link>
                  </div>
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>
      {roster && <Roster course={roster} onClose={() => setRoster(null)} />}
    </>
  );
}
