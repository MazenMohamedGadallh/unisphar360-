import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import type { JobListingDto, JobMatchDto } from '../../types/dto';

function tone(score: number): string {
  return score >= 70 ? 'match-high' : score >= 45 ? 'match-mid' : 'match-low';
}

export default function Matches() {
  usePageTitle('AI Job Matches');
  const [minScore, setMinScore] = useState(20);
  const matches = useApi<JobMatchDto[]>(`/api/grad/jobs/matches?limit=10&minScore=${minScore}`);
  const action = useAction();

  const toggleSave = (job: JobListingDto) =>
    action.run(async () => {
      if (job.isSaved) await api.del(`/api/grad/jobs/${job.id}/save`);
      else await api.post(`/api/grad/jobs/${job.id}/save`);
      matches.setData((list) => list?.map((m) => (m.job.id === job.id ? { ...m, job: { ...m.job, isSaved: !job.isSaved } } : m)));
    });

  const apply = (job: JobListingDto) =>
    action.run(async () => {
      await api.post('/api/grad/applications', { jobListingId: job.id, status: 'Applied' });
      matches.setData((list) => list?.map((m) => (m.job.id === job.id ? { ...m, job: { ...m.job, isApplied: true } } : m)));
    });

  return (
    <>
      <PageHeader
        title="AI Job Matches"
        subtitle="Deterministic match scores based on your profile, CV and preferences"
        actions={
          <div className="form-inline compact">
            <label className="muted" htmlFor="minScore">
              Min score
            </label>
            <select id="minScore" value={minScore} onChange={(e) => setMinScore(Number(e.target.value))}>
              {[0, 20, 40, 60, 80].map((v, vIdx) => (
                <option key={`${v}-${vIdx}`} value={v}>
                  {v}%
                </option>
              ))}
            </select>
            <button className="btn btn-sm btn-secondary" onClick={() => void matches.reload()}>
              <Icon name="refresh-cw" /> Refresh
            </button>
          </div>
        }
      />
      <ErrorBox error={action.error} />
      <Async
        res={matches}
        loadingLabel="Scoring jobs against your profile…"
        emptyIcon="thumbs-up"
        empty="No matches yet. Complete your profile (headline, skills, preferences) and upload a CV so the matcher can score jobs, then press Refresh."
      >
        {(list) => (
          <div className="match-list">
            {list.map((m) => (
              <div className="card match-card" key={m.job.id}>
                <div className={`match-head ${tone(m.scorePercent)}`}>
                  <div>
                    <h2>{m.job.title}</h2>
                    <p>
                      {m.job.companyName} · {m.job.location || 'Worldwide'}
                      {m.job.remote ? ' · Remote' : ''}
                    </p>
                  </div>
                  <div className="match-score">
                    <div>{m.scorePercent}%</div>
                    <small>match score</small>
                  </div>
                </div>
                <div className="match-body">
                  <div className="row-actions wrap">
                    <span className="grad-badge">Source: {m.job.source}</span>
                    {m.job.url && (
                      <a className="btn btn-sm btn-secondary" href={m.job.url} target="_blank" rel="noopener noreferrer">
                        Open posting
                      </a>
                    )}
                    <button className="btn btn-sm btn-secondary" disabled={action.busy} onClick={() => void toggleSave(m.job)}>
                      {m.job.isSaved ? (
                        <>
                          <Icon name="check" /> Saved
                        </>
                      ) : (
                        'Save'
                      )}
                    </button>
                    {m.job.isApplied ? (
                      <span className="grad-badge">Applied</span>
                    ) : (
                      <button className="btn btn-sm btn-primary" disabled={action.busy} onClick={() => void apply(m.job)}>
                        Mark as applied
                      </button>
                    )}
                  </div>
                  <p>
                    <strong>Matched skills:</strong>{' '}
                    {m.matchedSkills.length > 0 ? (
                      m.matchedSkills.map((s, sIdx) => (
                        <span className="grad-badge" key={`${s}-${sIdx}`}>
                          {s}
                        </span>
                      ))
                    ) : (
                      <span className="muted">none from our catalog yet</span>
                    )}
                  </p>
                  {m.missingSkills.length > 0 && (
                    <p>
                      <strong>Missing skills:</strong>{' '}
                      {m.missingSkills.map((s, sIdx) => (
                        <span className="grad-badge bad" key={`${s}-${sIdx}`}>
                          {s}
                        </span>
                      ))}
                    </p>
                  )}
                  <p className="muted">{m.explanation}</p>
                  {m.actions.length > 0 && (
                    <ul className="grad-list">
                      {m.actions.map((a, aIdx) => (
                        <li key={`${a}-${aIdx}`}>{a}</li>
                      ))}
                    </ul>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </Async>
    </>
  );
}
