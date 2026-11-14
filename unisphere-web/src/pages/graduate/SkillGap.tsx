import { useState } from 'react';
import { Async, Empty, PageHeader, Progress } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import type { SkillGapReportDto } from '../../types/dto';

const ROLES = [
  'Backend Developer',
  'Frontend Developer',
  'Full-Stack Developer',
  'Data Analyst',
  'Data Scientist',
  'Machine Learning Engineer',
  'NLP Engineer',
  'Mobile Developer',
  'DevOps Engineer',
  'QA Engineer',
  'Software Engineer',
];

export default function SkillGap() {
  usePageTitle('Skill Gap Analysis');
  const [role, setRole] = useState('');
  const [applied, setApplied] = useState('');
  const gap = useApi<SkillGapReportDto>(applied ? `/api/grad/jobs/skill-gap?role=${encodeURIComponent(applied)}` : '/api/grad/jobs/skill-gap');

  return (
    <>
      <PageHeader title="Skill Gap Analysis" subtitle="See what your target role expects and close the gap" />
      <div className="form-inline card filter-card">
        <div className="form-group">
          <label htmlFor="grRole">Target role</label>
          <select id="grRole" value={role} onChange={(e) => setRole(e.target.value)}>
            <option value="">Auto-detect from my profile</option>
            {ROLES.map((r, rIdx) => (
              <option key={`${r}-${rIdx}`}>{r}</option>
            ))}
          </select>
        </div>
        <button className="btn btn-primary" onClick={() => (role === applied ? void gap.reload() : setApplied(role))}>
          Analyze
        </button>
      </div>
      <Async res={gap} loadingLabel="Analyzing your skills…">
        {(g) => (
          <>
            <div className="card section-gap">
              <div className="card-header">
                <h3>
                  Target role: {g.targetRole} · coverage {Math.round(g.coveragePercent)}%
                </h3>
              </div>
              <Progress value={g.coveragePercent} tone={g.coveragePercent >= 70 ? 'success' : g.coveragePercent >= 40 ? 'warning' : 'danger'} />
              <p className="muted small-print">Coverage weighs the role&apos;s core skills against what we detected in your profile and CV.</p>
            </div>
            <div className="grad-two-col">
              <div className="card">
                <div className="card-header">
                  <h3>Skills you have</h3>
                </div>
                {g.matched.length === 0 ? (
                  <Empty icon="target">No role skills detected yet — add skills to your profile.</Empty>
                ) : (
                  <ul className="grad-list">
                    {g.matched.map((r, rIdx) => (
                      <li key={`${r.skill}-${rIdx}`}>
                        <strong>{r.skill}</strong> <span className="grad-badge">{r.priority}</span>
                        {r.level && <span className="muted"> · {r.level}</span>}
                        <div className="muted">{r.jobsRequiring}</div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>Skills to learn next</h3>
                </div>
                {g.missing.length === 0 ? (
                  <Empty icon="circle-check">Nothing missing — you cover this role&apos;s core stack.</Empty>
                ) : (
                  <ul className="grad-list">
                    {g.missing.map((r, rIdx) => (
                      <li key={`${r.skill}-${rIdx}`}>
                        <strong>{r.skill}</strong> <span className={`grad-badge ${r.priority === 'High' ? 'bad' : 'warn'}`}>{r.priority}</span>
                        <div className="muted">{r.jobsRequiring}</div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
            <div className="card section-gap-top">
              <div className="card-header">
                <h3>Recommended steps</h3>
              </div>
              <ul className="grad-list">
                {g.recommendedSteps.map((s, sIdx) => (
                  <li key={`${s}-${sIdx}`}>{s}</li>
                ))}
              </ul>
            </div>
          </>
        )}
      </Async>
    </>
  );
}
