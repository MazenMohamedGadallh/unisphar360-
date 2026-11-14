import { useState } from 'react';
import { Async, ConfirmDialog, ErrorBox, PageHeader } from '../../components/ui';
import { ApiError, api, errorMessage } from '../../lib/api';
import { useApi, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { statusKey } from '../../lib/format';
import type { EnrollmentDto } from '../../types/dto';

export default function Enrollments() {
  usePageTitle('My Enrollments');
  const enrollments = useApi<EnrollmentDto[]>('/api/enrollments');
  const [dropping, setDropping] = useState<EnrollmentDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const drop = async () => {
    if (!dropping) return;
    setBusy(true);
    setError(null);
    try {
      await api.del(`/api/enrollments/${dropping.id}`);
      setDropping(null);
      await enrollments.reload();
    } catch (err) {
      setError(err instanceof ApiError && err.status === 400 ? 'This course already has a grade, so it cannot be dropped.' : errorMessage(err, 'Drop failed.'));
      setDropping(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <PageHeader title="My Enrollments" subtitle="View and manage your course enrollments." />
      <div className="card">
        <div className="card-header">
          <h3>Current Enrollments</h3>
          <Link to="/courses" className="btn btn-primary btn-sm">
            Browse Courses
          </Link>
        </div>
        <ErrorBox error={error} />
        <Async res={enrollments} loadingLabel="Loading enrollments…" emptyIcon="book-open" empty="You are not enrolled in any courses. Browse available courses to get started.">
          {(list) => (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Course</th>
                    <th>Code</th>
                    <th>Semester</th>
                    <th>Grade</th>
                    <th>Attendance</th>
                    <th>Status</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {list.map((e) => (
                    <tr key={e.id}>
                      <td>{e.courseTitle}</td>
                      <td>{e.courseCode}</td>
                      <td>{e.semester}</td>
                      <td>{e.gradeLetter || '—'}</td>
                      <td>{e.attendancePercent}%</td>
                      <td>
                        <span className={`status-badge ${statusKey(e.status)}`}>{e.status}</span>
                      </td>
                      <td>
                        <button
                          className="btn btn-sm btn-danger"
                          onClick={() => setDropping(e)}
                          disabled={busy || !!e.gradeLetter}
                          title={e.gradeLetter ? 'Courses with a grade cannot be dropped' : 'Drop this course'}
                        >
                          Drop
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
      {dropping && (
        <ConfirmDialog
          title="Drop course?"
          message={
            <>
              Are you sure you want to drop <strong>{dropping.courseTitle}</strong>?
            </>
          }
          confirmLabel="Drop"
          busy={busy}
          onConfirm={() => void drop()}
          onCancel={() => setDropping(null)}
        />
      )}
    </>
  );
}
