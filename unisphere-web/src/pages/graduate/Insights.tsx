import { Async, BarList, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { fmtDateTime } from '../../lib/format';
import type { GradInsightsDto } from '../../types/dto';

export default function Insights() {
  usePageTitle('Career Insights');
  const data = useApi<GradInsightsDto>('/api/grad/insights');
  return (
    <>
      <PageHeader
        title="Career Insights"
        subtitle="What the job market looks like in your catalog right now"
        actions={
          <button className="btn btn-sm btn-secondary" onClick={() => void data.reload()}>
            Refresh
          </button>
        }
      />
      <Async res={data} loadingLabel="Analyzing the catalog…">
        {(d) => (
          <>
            <div className="grad-stat-grid">
              <div className="card stat-card">
                <div className="stat-value">{d.jobsIndexed}</div>
                <div className="stat-label">Jobs indexed</div>
              </div>
              <div className="card stat-card">
                <div className="stat-value stat-value-sm">{d.lastSyncUtc ? fmtDateTime(d.lastSyncUtc) : 'never'}</div>
                <div className="stat-label">Last sync</div>
              </div>
              <div className="card stat-card">
                <div className="stat-value">{d.jobsByWorkType.find((t) => t.name === 'Remote')?.count ?? 0}</div>
                <div className="stat-label">Remote listings</div>
              </div>
            </div>
            <div className="grad-two-col">
              <div className="card">
                <div className="card-header">
                  <h3>Top skills in demand</h3>
                </div>
                <BarList items={d.topSkills.slice(0, 10).map((s) => ({ label: s.name, value: s.count }))} />
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>Listings by industry</h3>
                </div>
                <BarList items={d.jobsByIndustry.map((s) => ({ label: s.name || 'Other', value: s.count }))} />
              </div>
            </div>
            <div className="grad-two-col section-gap-top">
              <div className="card">
                <div className="card-header">
                  <h3>Work types</h3>
                </div>
                <BarList items={d.jobsByWorkType.map((s) => ({ label: s.name || 'Unspecified', value: s.count }))} />
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>Recently posted titles</h3>
                </div>
                {d.recentTitles.length === 0 ? (
                  <p className="muted">No recent listings yet. Sync jobs from Find Jobs.</p>
                ) : (
                  <ul className="grad-list">
                    {d.recentTitles.map((t, i) => (
                      <li key={i}>{t}</li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          </>
        )}
      </Async>
    </>
  );
}
