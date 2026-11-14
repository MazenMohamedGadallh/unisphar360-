import { Icon } from '../../components/Icon';
import { Async, ErrorBox, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { NavLink } from '../../lib/router';
import { statusKey } from '../../lib/format';
import type { AcademicStats, GradeRowDto, InsightDto } from '../../types/dto';
import { GraduationCard } from './GraduationCard';

function insightIcon(kind: string): string {
  switch (kind) {
    case 'focus': return 'target';
    case 'progress': return 'trending-up';
    default: return 'lightbulb';
  }
}

export default function AcademicAdvisor() {
  usePageTitle('Academic Advisor');
  const stats = useApi<AcademicStats>('/api/academic/stats');
  const performance = useApi<GradeRowDto[]>('/api/academic/performance');
  const insights = useApi<InsightDto[]>('/api/academic/insights');
  const s = stats.data;

  return (
    <>
      <PageHeader title="Academic Advisor" subtitle="Your academic performance, insights, and progress in one place." />
      <ErrorBox error={stats.error} onRetry={() => void stats.reload()} />

      <div className="summary-cards">
        <div className="card gpa-card">
          <div className="card-icon">
            <Icon name="award" />
          </div>
          <div className="card-info">
            <span className="card-label">Current GPA</span>
            <span className="card-value">{s ? s.gpa.toFixed(2) : '--'}</span>
            {s && <span className="card-delta">out of {s.maxGpa.toFixed(2)}</span>}
          </div>
        </div>
        <div className="card courses-card">
          <div className="card-icon">
            <Icon name="book-open" />
          </div>
          <div className="card-info">
            <span className="card-label">Completed Courses</span>
            <span className="card-value">
              {s?.completedCourses ?? 0}/{s?.totalCourses ?? 0}
            </span>
            <span className="card-delta">{s?.completedPercent ?? 0}% complete</span>
          </div>
        </div>
        <div className="card attendance-card">
          <div className="card-icon">
            <Icon name="calendar-days" />
          </div>
          <div className="card-info">
            <span className="card-label">Attendance Rate</span>
            <span className="card-value">{s?.attendanceRate ?? 0}%</span>
          </div>
        </div>
        <div className="card improve-card">
          <div className="card-icon">
            <Icon name="lightbulb" />
          </div>
          <div className="card-info">
            <span className="card-label">Subjects to Improve</span>
            <span className="card-value">{s?.subjectsToImprove ?? 0}</span>
          </div>
        </div>
        <div className="card recommended-card">
          <div className="card-icon">
            <Icon name="graduation-cap" />
          </div>
          <div className="card-info">
            <span className="card-label">Recommended Courses</span>
            <span className="card-value">{s?.recommendedCourses ?? 0}</span>
            <NavLink to="/courses" className="card-delta">
              Browse courses
            </NavLink>
          </div>
        </div>
      </div>

      <div className="card section-gap">
        <div className="card-header">
          <h3>Academic Performance</h3>
          <NavLink to="/enrollments" className="view-all">
            My enrollments <Icon name="arrow-right" />
          </NavLink>
        </div>
        <Async res={performance} loadingLabel="Loading performance data…" empty="No academic records found.">
          {(rows) => (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Course</th>
                    <th>Grade</th>
                    <th>Attendance</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.courseId}>
                      <td>{r.course}</td>
                      <td>{r.gradeDisplay}</td>
                      <td>{r.attendance}%</td>
                      <td>
                        <span className={`status-badge ${statusKey(r.status)}`}>{r.status}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </div>

      <GraduationCard />

      <div className="card">
        <div className="card-header">
          <h3>Academic Insights</h3>
        </div>
        <Async res={insights} loadingLabel="Loading insights…" empty="No insights available. Keep learning!">
          {(items) => (
            <div className="insights-list">
              {items.map((ins, i) => (
                <div className={`insight-item ${ins.kind}`} key={i}>
                  <div className="insight-icon">
                    <Icon name={insightIcon(ins.kind)} />
                  </div>
                  <div className="insight-content">
                    <span className="insight-title">{ins.title}</span>
                    <span className="insight-message">{ins.message}</span>
                    {ins.actionLabel && ins.actionRoute && (
                      <NavLink to={ins.actionRoute} className="insight-action">
                        {ins.actionLabel} <Icon name="arrow-right" />
                      </NavLink>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>
    </>
  );
}
