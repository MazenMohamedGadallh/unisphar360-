import { Icon } from '../../components/Icon';
import { Async, Empty, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { fmtDate, fmtDateTime } from '../../lib/format';
import type { GraduateOverviewDto, NotificationDto } from '../../types/dto';

export function cvStatusLabel(status: string): string {
  switch (status) {
    case 'Parsed': return 'parsed successfully';
    case 'Manual': return 'manual text ready';
    case 'Failed': return 'could not auto-extract text (manual entry still works)';
    case 'UploadOnly': return 'stored, text pending';
    default: return status;
  }
}

function truncate(text: string, max: number) {
  return text.length <= max ? text : `${text.slice(0, max).trimEnd()}…`;
}

export default function GradOverview() {
  usePageTitle('Career Overview');
  const overview = useApi<GraduateOverviewDto>('/api/grad/overview');
  const notifications = useApi<NotificationDto[]>(overview.data ? '/api/notifications' : null);

  return (
    <>
      <PageHeader title="Career Overview" subtitle="Your job search and career progress at a glance" />
      <Async res={overview} loadingLabel="Loading overview…">
        {(o) => (
          <>
            <div className="grad-overview-hero">
              <div className="hero-text">
                <h2>Welcome, {o.firstName}</h2>
                <p>
                  {o.matchingJobsAboveThreshold} new match(es) above your alert threshold, {o.applicationsCount} application(s) on record.
                </p>
              </div>
              <Link className="btn btn-primary" to="/graduate/jobs">
                Find Jobs
              </Link>
            </div>

            <div className="grad-stat-grid">
              {[
                { v: `${o.profileCompletionPercent}%`, l: 'Profile completion', to: '/graduate/profile', a: 'Improve' },
                { v: o.matchingJobsCount, l: 'Matching jobs', to: '/graduate/matches', a: 'View matches' },
                { v: o.savedJobsCount, l: 'Saved jobs', to: '/graduate/jobs?view=saved', a: 'Saved jobs' },
                { v: o.applicationsInProgress, l: 'Applications in progress', to: '/graduate/applications', a: 'My Applications' },
                { v: o.upcomingFollowUpsCount, l: 'Follow-ups this week', to: '/graduate/applications', a: 'Track' },
                { v: `${o.roadmapProgressPercent}%`, l: 'Roadmap progress', to: '/graduate/roadmap', a: 'Career Roadmap' },
              ].map((s, sIdx) => (
                <div className="card stat-card" key={`${s.l}-${sIdx}`}>
                  <div className="stat-value">{s.v}</div>
                  <div className="stat-label">{s.l}</div>
                  <Link className="btn btn-sm btn-secondary" to={s.to}>
                    {s.a}
                  </Link>
                </div>
              ))}
            </div>

            {o.profileMissingFields.length > 0 && (
              <div className="alert alert-warning">
                Your profile is missing: {o.profileMissingFields.join(', ')}. <Link to="/graduate/profile">Complete it</Link> for better matches.
              </div>
            )}

            <div className="grad-two-col">
              <div className="card">
                <div className="card-header">
                  <h3>Next actions</h3>
                </div>
                {o.nextActions.length === 0 ? (
                  <Empty icon="circle-check">No pending actions.</Empty>
                ) : (
                  <ul className="grad-list">
                    {o.nextActions.map((a, aIdx) => (
                      <li key={`${a}-${aIdx}`}>{a}</li>
                    ))}
                  </ul>
                )}
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>CV status</h3>
                </div>
                {o.cvStatus === null ? (
                  <>
                    <p>No CV uploaded yet. Upload one to enable AI analysis and stronger matches.</p>
                    <Link className="btn btn-primary btn-sm" to="/graduate/cv">
                      CV &amp; Resume Studio
                    </Link>
                  </>
                ) : (
                  <>
                    <p>
                      Latest CV: <strong>{cvStatusLabel(o.cvStatus)}</strong>
                      {o.cvUploadedAtUtc && <span> · uploaded {fmtDate(o.cvUploadedAtUtc)}</span>}
                      {o.cvAnalysisScore !== null && <span> · AI score {o.cvAnalysisScore}/100</span>}
                      {o.previousAnalysisCount > 0 && <span> · {o.previousAnalysisCount} analysis run(s)</span>}
                    </p>
                    <Link className="btn btn-sm btn-secondary" to="/graduate/cv">
                      CV &amp; Resume Studio
                    </Link>
                  </>
                )}
              </div>
            </div>

            <div className="grad-two-col">
              <div className="card">
                <div className="card-header">
                  <h3>Top skill gaps</h3>
                </div>
                {o.topSkillGaps.length === 0 ? (
                  <Empty icon="target">No gaps for your target role right now.</Empty>
                ) : (
                  <>
                    <ul className="grad-list">
                      {o.topSkillGaps.map((g, gIdx) => (
                        <li key={`${g.skill}-${gIdx}`}>
                          <strong>{g.skill}</strong> <span className="grad-badge">{g.priority}</span> <span className="muted">{g.jobsRequiring} jobs want this</span>
                        </li>
                      ))}
                    </ul>
                    <Link className="btn btn-sm btn-secondary" to="/graduate/skill-gap">
                      Skill Gap Analysis
                    </Link>
                  </>
                )}
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>Upcoming events</h3>
                </div>
                {o.upcomingEvents.length === 0 ? (
                  <Empty icon="calendar-days">No upcoming events.</Empty>
                ) : (
                  <>
                    <ul className="grad-list">
                      {o.upcomingEvents.map((ev) => (
                        <li key={ev.id}>
                          <strong>{ev.title}</strong>
                          <div className="muted">
                            {fmtDateTime(ev.startsAtUtc)} · {ev.location}
                          </div>
                        </li>
                      ))}
                    </ul>
                    <Link className="btn btn-sm btn-secondary" to="/graduate/alumni">
                      Alumni &amp; University Services
                    </Link>
                  </>
                )}
              </div>
            </div>

            <div className="card section-gap">
              <div className="card-header">
                <h3>Recent activity</h3>
              </div>
              {o.recentActivity.length === 0 ? (
                <Empty icon="history">No recent activity yet.</Empty>
              ) : (
                <ul className="grad-list">
                  {o.recentActivity.map((a) => (
                    <li key={a.id} className="muted">
                      {fmtDateTime(a.createdAtUtc)} — {a.message}
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <div className="card">
              <div className="card-header">
                <h3>Career notifications</h3>
                <Link className="btn btn-sm btn-secondary" to="/notifications">
                  See all
                </Link>
              </div>
              {!notifications.data || notifications.data.length === 0 ? (
                <Empty icon="bell">No notifications yet. Strong new matches, follow-up reminders and career announcements appear here.</Empty>
              ) : (
                <ul className="grad-list">
                  {[...notifications.data]
                    .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
                    .slice(0, 4)
                    .map((n) => (
                      <li key={n.id}>
                        {!n.isRead && <span className="grad-badge">New</span>} <strong>{n.title}</strong>
                        <div className="muted">{truncate(n.body, 160)}</div>
                        <div className="muted">{fmtDateTime(n.createdAtUtc)}</div>
                      </li>
                    ))}
                </ul>
              )}
            </div>
          </>
        )}
      </Async>
      <p className="muted small-print">
        <Icon name="info" /> Matches and gaps are computed from your profile, skills and CV.
      </p>
    </>
  );
}
