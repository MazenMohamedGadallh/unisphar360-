import { Icon } from '../../components/Icon';
import { Async, Empty, PageHeader, StatCard } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { NavLink } from '../../lib/router';
import { fmtDate, fmtDateTime } from '../../lib/format';
import type { DoctorDashboardDto, DoctorSummaryDto } from '../../types/dto';

export function AlertsTable({ alerts }: { alerts: DoctorDashboardDto['alerts'] }) {
  if (alerts.length === 0) return <Empty icon="users">No students currently need additional support.</Empty>;
  return (
    <>
      <div className="table-wrapper">
        <table className="data-table">
          <thead>
            <tr>
              <th>Student</th>
              <th>Reason</th>
              <th>Severity</th>
            </tr>
          </thead>
          <tbody>
            {alerts.map((a, i) => (
              <tr key={`${a.studentId}-${i}`}>
                <td>{a.studentName}</td>
                <td>{a.reason}</td>
                <td>
                  <span className={`status-badge ${a.severity === 'High' ? 'needs-attention' : 'good'}`}>{a.severity}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="rec-track small-print">Advisory only — based on recorded grades and attendance. These flags never affect a student automatically.</p>
    </>
  );
}

export default function DoctorDashboard() {
  usePageTitle('Doctor Dashboard');
  const dash = useApi<DoctorDashboardDto>('/api/doctor/dashboard');
  const summary = useApi<DoctorSummaryDto>('/api/doctor/summary');

  return (
    <>
      <PageHeader title="Doctor Dashboard" subtitle={`Welcome back${dash.data?.doctorName ? `, ${dash.data.doctorName}` : ''} — here is your teaching overview.`} />
      <Async res={dash} loadingLabel="Loading your dashboard…">
        {(d) => (
          <>
            <div className="summary-cards summary-cards-4">
              <StatCard icon="book-open" label="My Courses" value={d.courseCount} />
              <StatCard icon="users" label="Enrolled Students" value={d.studentCount} />
              <StatCard icon="award" label="Average Grade" value={d.averageGradePoints?.toFixed(2) ?? '—'} delta="out of 4.00" />
              <StatCard icon="calendar-days" label="Average Attendance" value={d.averageAttendance !== null ? `${d.averageAttendance.toFixed(1)}%` : '—'} />
            </div>

            <div className="two-column">
              <div className="card">
                <div className="card-header">
                  <h3>My Courses</h3>
                  <NavLink to="/doctor/courses" className="view-all">
                    Manage <Icon name="arrow-right" />
                  </NavLink>
                </div>
                {d.courses.length === 0 ? (
                  <Empty icon="book-open">You are not assigned to any course yet. Contact an administrator.</Empty>
                ) : (
                  <div className="table-wrapper">
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th>Course</th>
                          <th>Students</th>
                          <th>Avg Grade</th>
                          <th>Attendance</th>
                          <th />
                        </tr>
                      </thead>
                      <tbody>
                        {d.courses.map((c) => (
                          <tr key={c.courseId}>
                            <td>
                              <strong>{c.code}</strong> — {c.title}
                            </td>
                            <td>{c.studentCount}</td>
                            <td>{c.averageGradePoints?.toFixed(2) ?? '—'}</td>
                            <td>{c.averageAttendance !== null ? `${c.averageAttendance.toFixed(1)}%` : '—'}</td>
                            <td>
                              <NavLink to={`/doctor/courses/${c.courseId}`} className="btn btn-sm btn-secondary">
                                Open
                              </NavLink>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>Recent Course Announcements</h3>
                  <NavLink to="/doctor/announcements" className="view-all">
                    All <Icon name="arrow-right" />
                  </NavLink>
                </div>
                {d.recentAnnouncements.length === 0 ? (
                  <Empty icon="megaphone">No course announcements yet.</Empty>
                ) : (
                  <div className="recommendation-list">
                    {d.recentAnnouncements.map((a) => (
                      <div className="recommendation-item" key={a.id}>
                        <div className="rec-info">
                          <span className="rec-title">{a.title}</span>
                          <span className="rec-track">
                            {a.courseName} · {fmtDate(a.createdAtUtc)}
                          </span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            {summary.data && (
              <div className="two-column">
                <div className="card">
                  <div className="card-header">
                    <h3>Upcoming exams &amp; deadlines</h3>
                  </div>
                  {summary.data.upcoming.length === 0 ? (
                    <Empty icon="calendar-days">No upcoming exams or assignment deadlines.</Empty>
                  ) : (
                    <div className="recommendation-list">
                      {summary.data.upcoming.map((m) => (
                        <div className="recommendation-item" key={m.id}>
                          <div className="rec-info">
                            <span className="rec-code">{m.type}</span>
                            <span className="rec-title">{m.title}</span>
                            <span className="rec-track">{m.courseName}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
                <div className="card">
                  <div className="card-header">
                    <h3>Latest materials</h3>
                    <NavLink to="/doctor/materials" className="view-all">
                      Materials <Icon name="arrow-right" />
                    </NavLink>
                  </div>
                  {summary.data.latestMaterials.length === 0 ? (
                    <Empty icon="file-text">No materials uploaded yet.</Empty>
                  ) : (
                    <div className="recommendation-list">
                      {summary.data.latestMaterials.map((m) => (
                        <div className="recommendation-item" key={m.id}>
                          <div className="rec-info">
                            <span className="rec-code">{m.type}</span>
                            <span className="rec-title">{m.title}</span>
                            <span className="rec-track">
                              {m.courseName} · {fmtDateTime(m.createdAt)}
                            </span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}

            <div className="card section-gap-top">
              <div className="card-header">
                <h3>Academic Alerts</h3>
              </div>
              <AlertsTable alerts={d.alerts} />
            </div>
          </>
        )}
      </Async>
    </>
  );
}
