import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useApi, usePageTitle } from '../../lib/hooks';
import { NavLink, useSearchParams } from '../../lib/router';
import { fmtDate, parseDate, statusKey, timeAgo } from '../../lib/format';
import type {
  DashboardActivityDto,
  DashboardInsightDto,
  DashboardPerformanceRowDto,
  DashboardRecommendedCourseDto,
  DashboardStudyDayDto,
  DashboardStudyPlanDto,
  DashboardSummaryDto,
  MaterialDto,
  MyUniversityDto,
  PagedResultDto,
  UniversityCourseDto,
} from '../../types/dto';

function insightIcon(type: string): string {
  switch (type.toLowerCase()) {
    case 'warning': return 'triangle-alert';
    case 'progress': return 'trending-up';
    default: return 'lightbulb';
  }
}

function signed(n: number, digits = 0): string {
  return `${n >= 0 ? '+' : ''}${n.toFixed(digits)}`;
}

export default function Dashboard() {
  usePageTitle('Dashboard');
  const [params] = useSearchParams();
  const summary = useApi<DashboardSummaryDto>('/api/dashboard/summary');
  const performance = useApi<DashboardPerformanceRowDto[]>('/api/dashboard/performance?semester=current');
  const planner = useApi<DashboardStudyPlanDto>('/api/dashboard/study-plan');
  const recommended = useApi<DashboardRecommendedCourseDto[]>('/api/dashboard/recommended-courses');
  const activity = useApi<DashboardActivityDto[]>('/api/dashboard/recent-activity?take=4');
  const insights = useApi<DashboardInsightDto[]>('/api/dashboard/insights');
  const university = useApi<MyUniversityDto>('/api/university/me');
  const hasUniversity = !!university.data?.universityName;
  const uniCourses = useApi<UniversityCourseDto[]>(hasUniversity ? '/api/university/me/courses' : null);
  const uniMaterials = useApi<PagedResultDto<MaterialDto>>(hasUniversity ? '/api/university/me/materials?page=1&pageSize=5' : null);
  const [plannerError, setPlannerError] = useState<string | null>(null);

  const s = summary.data;

  const toggleDay = async (day: DashboardStudyDayDto) => {
    if (!day.id) return;
    setPlannerError(null);
    try {
      const updated = await api.patch<DashboardStudyDayDto>(`/api/dashboard/study-plan/${day.id}/toggle`);
      if (updated) planner.setData((p) => (p ? { ...p, days: p.days.map((d) => (d.id === day.id ? updated : d)) } : p));
    } catch (err) {
      setPlannerError(errorMessage(err, 'Could not update the study plan. Please try again.'));
    }
  };

  return (
    <div className="dashboard">
      <div className="welcome-banner">
        <div className="welcome-content">
          <h1>
            {s?.greeting ?? 'Hello'}, {s?.firstName || 'Student'}!
          </h1>
          <p>Your academic journey matters. Let&apos;s make it better together.</p>
        </div>
        <div className="welcome-decoration">
          <Icon name="graduation-cap" />
        </div>
      </div>

      <ErrorBox error={summary.error} onRetry={() => void summary.reload()} />
      {params.get('denied') === '1' && <div className="alert alert-error">Access denied: you don&apos;t have permission to view that page.</div>}

      <div className="summary-cards">
        <div className="card gpa-card">
          <div className="card-icon">
            <Icon name="award" />
          </div>
          <div className="card-info">
            <span className="card-label">Current GPA</span>
            <span className="card-value">{s ? s.currentGpa.toFixed(2) : '--'}</span>
            {s && (
              <span className={`card-delta ${s.gpaDeltaVsLastSemester >= 0 ? 'positive' : 'negative'}`}>
                {signed(s.gpaDeltaVsLastSemester, 2)} from last semester
              </span>
            )}
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
            <span className="card-delta">{s?.completionPercent ?? 0}% complete</span>
          </div>
        </div>
        <div className="card attendance-card">
          <div className="card-icon">
            <Icon name="calendar-days" />
          </div>
          <div className="card-info">
            <span className="card-label">Attendance Rate</span>
            <span className="card-value">{s?.attendanceRate ?? 0}%</span>
            {s && <span className={`card-delta ${s.attendanceDelta >= 0 ? 'positive' : 'negative'}`}>{signed(s.attendanceDelta)}% from last semester</span>}
          </div>
        </div>
        <div className="card improve-card">
          <div className="card-icon">
            <Icon name="lightbulb" />
          </div>
          <div className="card-info">
            <span className="card-label">Subjects to Improve</span>
            <span className="card-value">{s?.subjectsToImprove ?? 0}</span>
            <span className="card-delta">Need attention</span>
          </div>
        </div>
        <div className="card recommended-card">
          <div className="card-icon">
            <Icon name="graduation-cap" />
          </div>
          <div className="card-info">
            <span className="card-label">Recommended Courses</span>
            <span className="card-value">{s?.recommendedCoursesCount ?? 0}</span>
            <span className="card-delta">For you</span>
          </div>
        </div>
      </div>

      <div className="card performance-card">
        <div className="card-header">
          <h3>Academic Performance</h3>
          <NavLink to="/academic-advisor" className="view-all">
            View All <Icon name="arrow-right" />
          </NavLink>
        </div>
        <Async res={performance} loadingLabel="Loading performance data…" emptyIcon="chart-no-axes-combined" empty="No academic records found. Enroll in courses to see your performance.">
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
                      <td>{r.courseName}</td>
                      <td>
                        {r.gradeLetter} ({r.gradePoints.toFixed(1)})
                      </td>
                      <td>{r.attendancePercent}%</td>
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

      <div className="two-column">
        <div className="card planner-card">
          <div className="card-header">
            <h3>Study Planner</h3>
            <NavLink to="/study-planner" className="view-all">
              Edit <Icon name="arrow-right" />
            </NavLink>
          </div>
          <ErrorBox error={plannerError} />
          <Async res={planner} loadingLabel="Loading study plan…">
            {(p) => (
              <>
                <div className="planner-week">{p.weekLabel}</div>
                <div className="planner-items">
                  {p.days.map((item, i) => (
                    <div key={item.id || `free-${i}`} className={`planner-item ${item.isCompleted ? 'done' : ''} ${item.isFreeDay ? 'free' : ''}`}>
                      <div className="planner-date">
                        <span className="day-label">{item.dayLabel}</span>
                        <span className="day-number">{parseDate(item.date)?.getUTCDate() ?? ''}</span>
                      </div>
                      <div className="planner-details">
                        <span className="planner-title">{item.subject}</span>
                        <span className="planner-hours">{item.hours} hrs</span>
                      </div>
                      <div className="planner-status">
                        {!item.isFreeDay && (
                          <button className="check-btn" title="Toggle done" onClick={() => void toggleDay(item)}>
                            {item.isCompleted ? (
                              <span className="check-icon">
                                <Icon name="check" />
                              </span>
                            ) : (
                              <span className="check-empty">
                                <Icon name="circle" />
                              </span>
                            )}
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </>
            )}
          </Async>
        </div>

        <div className="card recommendations-card">
          <div className="card-header">
            <h3>Recommended Courses</h3>
            <NavLink to="/courses" className="view-all">
              View All <Icon name="arrow-right" />
            </NavLink>
          </div>
          <Async res={recommended} loadingLabel="Loading recommendations…" emptyIcon="lightbulb" empty="No recommendations available at this time.">
            {(list) => (
              <div className="recommendation-list">
                {list.slice(0, 4).map((c) => (
                  <div className="recommendation-item" key={c.id}>
                    <div className="rec-info">
                      <span className="rec-code">{c.name}</span>
                      <span className="rec-title">{c.tag}</span>
                      <span className="rec-track">
                        {c.track} | {c.credits} credits
                      </span>
                    </div>
                    <NavLink to={`/courses/${c.id}`} className="rec-btn">
                      View
                    </NavLink>
                  </div>
                ))}
              </div>
            )}
          </Async>
        </div>
      </div>

      <div className="card university-card">
        <div className="card-header">
          <h3>My University</h3>
          <NavLink to="/university" className="view-all">
            View all <Icon name="arrow-right" />
          </NavLink>
        </div>
        <Async res={university} loadingLabel="Loading university…" isEmpty={(u) => !u.universityName} emptyIcon="landmark" empty="No university assigned to your account yet.">
          {(u) => (
            <>
              <div className="university-sub">
                {u.universityName} · {u.department} · Year {u.academicYear ?? '-'}
              </div>
              <Async res={uniCourses} loadingLabel="Loading courses…" emptyIcon="book-open" empty="No courses found for your university.">
                {(courses) => (
                  <div className="uni-course-list">
                    {courses.slice(0, 6).map((c) => (
                      <div className="uni-course-item" key={c.id}>
                        <span className="uni-course-name">{c.title}</span>
                        <span className="badge">{c.materialsCount}</span>
                      </div>
                    ))}
                  </div>
                )}
              </Async>
              <h4 className="mini-heading">Latest summaries &amp; materials</h4>
              <Async res={uniMaterials} loadingLabel="Loading materials…" isEmpty={(p) => p.items.length === 0} emptyIcon="file-text" empty="No materials yet.">
                {(page) => (
                  <div className="activity-list">
                    {page.items.slice(0, 5).map((m) => (
                      <div className="activity-item" key={m.id}>
                        <div className="activity-content">
                          <span className="activity-message">{m.title}</span>
                          <span className="activity-time">
                            {m.courseName}
                            {m.instructorName ? ` · ${m.instructorName}` : ''} · {m.type} · {fmtDate(m.createdAt, { month: 'short', day: 'numeric' })}
                          </span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Async>
            </>
          )}
        </Async>
      </div>

      <div className="card quick-actions-card">
        <div className="card-header">
          <h3>Quick Actions</h3>
        </div>
        <div className="quick-actions-grid">
          <NavLink to="/academic-advisor" className="quick-action tile-grades">
            <span className="action-icon">
              <Icon name="pencil" />
            </span>
            <span>Check My Grades</span>
          </NavLink>
          <NavLink to="/study-planner" className="quick-action tile-planner">
            <span className="action-icon">
              <Icon name="calendar-days" />
            </span>
            <span>Plan My Study</span>
          </NavLink>
          <NavLink to="/courses" className="quick-action tile-courses">
            <span className="action-icon">
              <Icon name="graduation-cap" />
            </span>
            <span>Course Recommendations</span>
          </NavLink>
          <NavLink to="/academic-advisor" className="quick-action tile-insights">
            <span className="action-icon">
              <Icon name="lightbulb" />
            </span>
            <span>Academic Insights</span>
          </NavLink>
        </div>
      </div>

      <div className="two-column">
        <div className="card activity-card">
          <div className="card-header">
            <h3>Recent Activity</h3>
          </div>
          <Async res={activity} loadingLabel="Loading activity…" emptyIcon="history" empty="No recent activity.">
            {(items) => (
              <div className="activity-list">
                {items.map((a) => (
                  <div className="activity-item" key={a.id}>
                    <div className="activity-content">
                      <span className="activity-message">{a.text}</span>
                      <span className="activity-time">{timeAgo(a.createdAt)}</span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Async>
        </div>

        <div className="card insights-card">
          <div className="card-header">
            <h3>AI Insights</h3>
          </div>
          <Async res={insights} loadingLabel="Loading insights…" emptyIcon="chart-no-axes-combined" empty="No insights available. Keep learning!">
            {(items) => (
              <div className="insights-list">
                {items.map((ins, i) => (
                  <div className={`insight-item ${ins.type.toLowerCase()}`} key={i}>
                    <div className="insight-icon">
                      <Icon name={insightIcon(ins.type)} />
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
      </div>
    </div>
  );
}
