// Course roster with grading (GET /api/doctor/courses/{id}/students,
// PUT /api/enrollments/{enrollmentId}/grade). Shared by the course page,
// /doctor/students and /doctor/courses/:courseId/students.

import { useMemo, useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ErrorBox, Modal, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { Link, useParams, useSearchParams } from '../../lib/router';
import type { DoctorCourseDetailDto, DoctorCourseDto, DoctorStudentDto } from '../../types/dto';

// Standard 4.0 scale. The API stores letter and points separately and does
// not derive one from the other, so the editor fills points from the letter.
const GRADE_POINTS: Record<string, number> = { A: 4.0, 'A-': 3.7, 'B+': 3.3, B: 3.0, 'B-': 2.7, 'C+': 2.3, C: 2.0, 'C-': 1.7, 'D+': 1.3, D: 1.0, F: 0 };
const GRADE_LETTERS = Object.keys(GRADE_POINTS);

function gradeClass(g: string): string {
  if (!g) return '';
  if (g === 'A' || g === 'A-') return 'excellent';
  if (g === 'F') return 'needs-attention';
  return 'good';
}

function GradeEditor({ student, onClose, onSaved }: { student: DoctorStudentDto; onClose: () => void; onSaved: (msg: string) => void }) {
  const [letter, setLetter] = useState(student.gradeLetter || '');
  const [points, setPoints] = useState(student.gradeLetter ? String(student.gradePoints) : '');
  const [attendance, setAttendance] = useState(String(student.attendancePercent));
  const action = useAction();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!letter) {
      action.setError('Pick a letter grade first (or Cancel).');
      return;
    }
    const p = points.trim() === '' ? null : Number(points);
    const a = attendance.trim() === '' ? null : Number(attendance);
    if (p !== null && (Number.isNaN(p) || p < 0 || p > 4)) return action.setError('Grade points must be between 0 and 4.0.');
    if (a !== null && (Number.isNaN(a) || a < 0 || a > 100)) return action.setError('Attendance must be between 0 and 100.');
    void action.run(async () => {
      await api.put(`/api/enrollments/${student.enrollmentId}/grade`, { gradeLetter: letter, gradePoints: p, attendancePercent: a, status: null });
      onSaved(`Grade updated for ${student.fullName}.`);
    });
  };

  return (
    <Modal title={`Grade ${student.fullName}`} onClose={onClose}>
      <form onSubmit={submit}>
        <ErrorBox error={action.error} />
        <div className="form-row">
          <div className="form-group">
            <label>Letter grade</label>
            <select
              value={letter}
              onChange={(e) => {
                setLetter(e.target.value);
                if (e.target.value in GRADE_POINTS) setPoints(String(GRADE_POINTS[e.target.value]));
              }}
            >
              <option value="">— no grade assigned</option>
              {GRADE_LETTERS.map((g, gIdx) => (
                <option key={`${g}-${gIdx}`}>{g}</option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label>Grade points (0–4)</label>
            <input type="number" step="0.1" min={0} max={4} value={points} onChange={(e) => setPoints(e.target.value)} placeholder="e.g. 3.3" />
          </div>
        </div>
        <div className="form-group">
          <label>Attendance %</label>
          <input type="number" min={0} max={100} value={attendance} onChange={(e) => setAttendance(e.target.value)} />
        </div>
        <p className="rec-track small-print">Points are filled from the letter on the standard 4.0 scale; adjust them if your course uses a different scale.</p>
        <div className="modal-actions flush">
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={action.busy}>
            {action.busy ? 'Saving…' : 'Save Grade'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export function StudentsTable({ courseId }: { courseId: number }) {
  const students = useApi<DoctorStudentDto[]>(`/api/doctor/courses/${courseId}/students`);
  const [search, setSearch] = useState('');
  const [grading, setGrading] = useState<DoctorStudentDto | null>(null);
  const [flash, setFlash] = useFlash();

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (students.data ?? []).filter((s) => !q || s.fullName.toLowerCase().includes(q) || s.email.toLowerCase().includes(q));
  }, [students.data, search]);

  return (
    <div className="card">
      <div className="card-header">
        <h3>Enrolled Students ({students.data?.length ?? 0})</h3>
        <div className="search-box compact">
          <span className="search-icon">
            <Icon name="search" />
          </span>
          <input placeholder="Search name or email…" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search students" />
        </div>
      </div>
      {flash && <Alert kind="success">{flash}</Alert>}
      <Async res={students} loadingLabel="Loading students…" emptyIcon="users" empty="No students are enrolled in this course yet.">
        {() =>
          filtered.length === 0 ? (
            <div className="empty-state">No students match the current filters.</div>
          ) : (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Department</th>
                    <th>Year</th>
                    <th>Grade</th>
                    <th>Points</th>
                    <th>Attendance</th>
                    <th>Status</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((s) => (
                    <tr key={s.enrollmentId}>
                      <td>{s.fullName}</td>
                      <td className="muted">{s.email}</td>
                      <td>{s.department || s.major || '—'}</td>
                      <td>{s.academicYear ?? '—'}</td>
                      <td>
                        <span className={`status-badge ${gradeClass(s.gradeLetter)}`}>{s.gradeLetter || 'Ungraded'}</span>
                      </td>
                      <td>{s.gradePoints.toFixed(2)}</td>
                      <td>{s.attendancePercent}%</td>
                      <td>{s.status}</td>
                      <td>
                        <button className="btn btn-sm btn-secondary" onClick={() => setGrading(s)}>
                          Grade
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        }
      </Async>
      {grading && (
        <GradeEditor
          student={grading}
          onClose={() => setGrading(null)}
          onSaved={(msg) => {
            setGrading(null);
            setFlash(msg);
            void students.reload();
          }}
        />
      )}
    </div>
  );
}

/** /doctor/students — pick one of your courses, then grade its students. */
export function MyStudents() {
  usePageTitle('My Students');
  const courses = useApi<DoctorCourseDto[]>('/api/doctor/courses');
  const [params, setParams] = useSearchParams();
  const selected = params.get('courseId') ?? (courses.data?.[0] ? String(courses.data[0].id) : '');

  return (
    <>
      <PageHeader title="My Students" subtitle="Students enrolled in the courses you teach — profile summaries only, no private contact data." />
      <Async res={courses} loadingLabel="Loading your courses…" emptyIcon="users" empty="Student lists will appear once the administrator assigns you to courses.">
        {(list) => (
          <>
            <div className="form-inline card filter-card">
              <div className="form-group">
                <label htmlFor="studentCourse">Course</label>
                <select id="studentCourse" value={selected} onChange={(e) => setParams({ courseId: e.target.value })}>
                  {list.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.code} — {c.displayTitle || c.title} ({c.studentsCount})
                    </option>
                  ))}
                </select>
              </div>
            </div>
            {selected && <StudentsTable key={selected} courseId={Number(selected)} />}
          </>
        )}
      </Async>
    </>
  );
}

/** /doctor/courses/:courseId/students */
export function CourseStudentsPage() {
  const { courseId } = useParams<{ courseId: string }>();
  const course = useApi<DoctorCourseDetailDto>(`/api/doctor/courses/${courseId}`);
  usePageTitle(course.data ? `${course.data.code} students` : 'Course students');
  return (
    <>
      <div className="page-header">
        <Link to={`/doctor/courses/${courseId}`} className="back-link">
          <Icon name="arrow-left" /> Back to course
        </Link>
        <h1>{course.data ? `${course.data.code} — ${course.data.title}` : 'Course students'}</h1>
      </div>
      <ErrorBox error={course.error} />
      <StudentsTable courseId={Number(courseId)} />
    </>
  );
}
