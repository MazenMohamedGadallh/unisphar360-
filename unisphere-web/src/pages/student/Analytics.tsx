import { Icon } from '../../components/Icon';
import { Async, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import type { DashboardPerformanceRowDto, GpaTrendPointDto, PerformanceAnalysisDto } from '../../types/dto';

function riskLabel(risk: string): string {
  switch (risk) {
    case 'OnTrack': return 'On Track';
    case 'NeedsAttention': return 'Needs Attention';
    case 'AtRisk': return 'At Risk';
    default: return risk;
  }
}

function riskClass(risk: string): string {
  switch (risk) {
    case 'OnTrack': return 'excellent';
    case 'NeedsAttention': return 'good';
    default: return 'needs-attention';
  }
}

function TrendChart({ points }: { points: GpaTrendPointDto[] }) {
  const values = points.map((p) => p.gpa);
  const min = Math.min(0, Math.min(...values) - 0.2);
  const max = Math.max(4.0, Math.max(...values) + 0.2);
  const pos = points.map((p, i) => ({
    x: Math.round((30 + (260 * i) / Math.max(1, points.length - 1)) * 10) / 10,
    y: Math.round((130 - (130 * (p.gpa - min)) / Math.max(0.01, max - min)) * 10) / 10,
  }));
  return (
    <svg viewBox="0 0 320 160" className="trend-chart" role="img" aria-label="GPA trend">
      <line x1="30" y1="10" x2="30" y2="140" className="trend-axis" />
      <line x1="30" y1="140" x2="300" y2="140" className="trend-axis" />
      <polyline points={pos.map((p) => `${p.x},${p.y}`).join(' ')} className="trend-line" />
      {pos.map((p, i) => (
        <g key={i}>
          <circle cx={p.x} cy={p.y} r="4" className="trend-dot" />
          <text x={p.x} y="155" textAnchor="middle" className="trend-label">
            {points[i].label}
          </text>
          <text x={p.x} y={p.y - 8} textAnchor="middle" className="trend-value">
            {points[i].gpa.toFixed(2)}
          </text>
        </g>
      ))}
    </svg>
  );
}

export default function Analytics() {
  usePageTitle('My Analytics');
  const analysis = useApi<PerformanceAnalysisDto>('/api/analytics/me/performance');
  const performance = useApi<DashboardPerformanceRowDto[]>('/api/dashboard/performance?semester=current');
  const trend = useApi<GpaTrendPointDto[]>('/api/analytics/me/gpa-trend');

  return (
    <>
      <PageHeader title="My Analytics" subtitle="Are you on the right track? Here's what your data says." />
      <Async res={analysis} loadingLabel="Loading your analytics…">
        {(a) => {
          const dash = `${((Math.max(0, Math.min(100, a.score)) / 100) * 339.3).toFixed(1)} 339.3`;
          return (
            <>
              <div className="card analytics-hero">
                <div className="hero-status">
                  <span className={`status-badge ${riskClass(a.riskLevel)}`}>{riskLabel(a.riskLevel)}</span>
                  <div className="hero-score">
                    <svg viewBox="0 0 140 140" className="gauge" role="img" aria-label={`Score ${Math.round(a.score)} of 100`}>
                      <circle cx="70" cy="70" r="54" className="gauge-track" />
                      <circle cx="70" cy="70" r="54" className={`gauge-fill ${riskClass(a.riskLevel)}`} strokeDasharray={dash} transform="rotate(-90 70 70)" />
                      <text x="70" y="66" textAnchor="middle" className="gauge-value">
                        {Math.round(a.score)}
                      </text>
                      <text x="70" y="86" textAnchor="middle" className="gauge-sub">
                        / 100
                      </text>
                    </svg>
                  </div>
                </div>
                <div className="hero-stats">
                  <div className="hero-stat">
                    <span className="card-label">Predicted GPA</span>
                    <span className="card-value">{a.predictedGpa.toFixed(2)}</span>
                  </div>
                  <div className="hero-stat">
                    <span className="card-label">Confidence</span>
                    <span className="card-value">{Math.round(a.confidence * 100)}%</span>
                  </div>
                </div>
              </div>

              <div className="two-column section-gap-top">
                <div className="card">
                  <div className="card-header">
                    <h3>Grades &amp; attendance per course</h3>
                  </div>
                  <div className="chart-container">
                    <Async res={performance} loadingLabel="Loading courses…" empty="No enrolled courses yet.">
                      {(rows) => (
                        <div className="bar-chart">
                          {rows.map((r) => (
                            <div className="bar-item" key={r.courseId}>
                              <span className="bar-label">{r.courseName}</span>
                              <div className="bar-wrapper">
                                <div className="bar" style={{ width: `${(r.gradePoints / 4) * 100}%` }} />
                              </div>
                              <span className="bar-value">
                                {r.gradeLetter} · {r.attendancePercent}%
                              </span>
                            </div>
                          ))}
                        </div>
                      )}
                    </Async>
                  </div>
                </div>
                <div className="card">
                  <div className="card-header">
                    <h3>GPA trend by semester</h3>
                  </div>
                  <div className="chart-container">
                    <Async res={trend} loadingLabel="Loading trend…" isEmpty={(t) => t.length < 2} empty="Finish this semester to start your trend line.">
                      {(t) => <TrendChart points={t} />}
                    </Async>
                  </div>
                </div>
              </div>

              <div className="card section-gap-top">
                <div className="card-header">
                  <h3>Why this result</h3>
                </div>
                <div className="chart-container">
                  <div className="bar-chart">
                    {a.factors.map((f, fIdx) => (
                      <div className="bar-item" key={`${f.factor}-${fIdx}`}>
                        <span className="bar-label">
                          {f.factor} ({Math.round(f.weight * 100)}%)
                        </span>
                        <div className="bar-wrapper">
                          <div className="bar" style={{ width: `${f.value}%` }} />
                        </div>
                        <span className="bar-value">+{f.contribution.toFixed(1)}</span>
                      </div>
                    ))}
                  </div>
                  <div className="factor-notes">
                    {a.factors.map((f, fIdx) => (
                      <p key={`${f.factor}-${fIdx}`}>
                        <strong>{f.factor}:</strong> {f.description}
                      </p>
                    ))}
                  </div>
                </div>
              </div>

              {a.courseRisks.length > 0 && (
                <div className="card section-gap-top">
                  <div className="card-header">
                    <h3>Course risk</h3>
                  </div>
                  <div className="table-wrapper">
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th>Course</th>
                          <th>Risk</th>
                          <th>Why</th>
                        </tr>
                      </thead>
                      <tbody>
                        {a.courseRisks.map((c, cIdx) => (
                          <tr key={`${c.course}-${cIdx}`}>
                            <td>{c.course}</td>
                            <td>
                              <span className={`status-badge ${riskClass(c.riskLevel)}`}>{riskLabel(c.riskLevel)}</span>
                            </td>
                            <td>{c.reason}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              <div className="two-column section-gap-top">
                <div className="card">
                  <div className="card-header">
                    <h3>Strengths</h3>
                  </div>
                  <div className="chart-container">
                    {a.strengths.length === 0 ? (
                      <div className="empty-state">No standout strengths yet — your first win is coming.</div>
                    ) : (
                      <ul className="plain-list">
                        {a.strengths.map((s, sIdx) => (
                          <li key={`${s}-${sIdx}`}>
                            <Icon name="circle-check" className="success" /> {s}
                          </li>
                        ))}
                      </ul>
                    )}
                    <h3 className="section-gap-top-sm">Areas to improve</h3>
                    {a.weaknesses.length === 0 ? (
                      <div className="empty-state">Nothing flagged. Keep it up!</div>
                    ) : (
                      <ul className="plain-list">
                        {a.weaknesses.map((w, wIdx) => (
                          <li key={`${w}-${wIdx}`}>
                            <Icon name="triangle-alert" className="warning" /> {w}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </div>
                <div className="card">
                  <div className="card-header">
                    <h3>Recommendations</h3>
                  </div>
                  <div className="chart-container">
                    {a.recommendations.length === 0 ? (
                      <div className="empty-state">No recommendations right now.</div>
                    ) : (
                      <ul className="plain-list">
                        {a.recommendations.map((r, rIdx) => (
                          <li key={`${r}-${rIdx}`}>
                            <Icon name="lightbulb" className="info" /> {r}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </div>
              </div>
            </>
          );
        }}
      </Async>
    </>
  );
}
