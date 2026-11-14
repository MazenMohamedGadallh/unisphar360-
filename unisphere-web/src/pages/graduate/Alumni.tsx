import { Async, Empty, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { fmtDate, fmtDateTime } from '../../lib/format';
import type { GradAlumniDto } from '../../types/dto';

const isCareerEvent = (d: string | null) => !!d && ['career', 'internship', 'hiring', 'job'].some((w) => d.toLowerCase().includes(w));

export default function Alumni() {
  usePageTitle('Alumni & University Services');
  const data = useApi<GradAlumniDto>('/api/grad/alumni');
  return (
    <>
      <PageHeader title="Alumni & University Services" subtitle="Your university career center, online" />
      <Async res={data} loadingLabel="Loading university services…">
        {(d) => (
          <>
            <div className="grad-overview-hero">
              <div className="hero-text">
                <h2>{d.universityName ?? 'UniSphere network'}</h2>
                <p>
                  {d.degree || 'Graduate'}
                  {d.graduationYear ? ` · Class of ${d.graduationYear}` : ''}
                  {d.department ? ` · ${d.department}` : ''}
                </p>
              </div>
              <button className="btn btn-secondary" onClick={() => void data.reload()}>
                Refresh
              </button>
            </div>
            <div className="card section-gap">
              <div className="card-header">
                <h3>Career center tip</h3>
              </div>
              <p>{d.careerCenterTip}</p>
              {d.contacts.length > 0 && (
                <ul className="grad-list">
                  {d.contacts.map((c, cIdx) => (
                    <li key={`${c}-${cIdx}`}>{c}</li>
                  ))}
                </ul>
              )}
            </div>
            <div className="grad-two-col">
              <div className="card">
                <div className="card-header">
                  <h3>Career events</h3>
                </div>
                {d.careerEvents.length === 0 ? (
                  <Empty icon="calendar-days">No career events scheduled right now.</Empty>
                ) : (
                  <ul className="grad-list">
                    {d.careerEvents.map((e) => (
                      <li key={e.id}>
                        <strong>{e.title}</strong> {isCareerEvent(e.description) && <span className="grad-badge">Career</span>}
                        <div className="muted">
                          {fmtDateTime(e.startsAtUtc)} · {e.location}
                        </div>
                        {e.description && <div className="muted">{e.description}</div>}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <div className="card">
                <div className="card-header">
                  <h3>University announcements</h3>
                </div>
                {d.announcements.length === 0 ? (
                  <Empty icon="megaphone">No announcements in the last 90 days.</Empty>
                ) : (
                  <ul className="grad-list">
                    {d.announcements.map((a) => (
                      <li key={a.id}>
                        <strong>{a.title}</strong> {a.category && <span className="grad-badge">{a.category}</span>}
                        <div className="muted">{fmtDate(a.createdAtUtc)}</div>
                        <div>{a.body.length > 300 ? `${a.body.slice(0, 300)}…` : a.body}</div>
                      </li>
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
