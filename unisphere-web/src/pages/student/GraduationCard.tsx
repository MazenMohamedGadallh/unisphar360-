// Graduation tracking (GET /api/graduation/overview, POST /apply, /application/withdraw,
// /ceremony, /certificate). The backend supports the full flow; this card exposes it.

import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, Modal, Progress, StatusBadge } from '../../components/ui';
import { ApiError, api } from '../../lib/api';
import { useAction, useApi } from '../../lib/hooks';
import { fmtDate, fmtDateTime } from '../../lib/format';
import type { GraduationCeremonyDto, GraduationCertificateDto, GraduationOverviewResponse } from '../../types/dto';

export function GraduationCard() {
  const overview = useApi<GraduationOverviewResponse>('/api/graduation/overview');
  const action = useAction();
  const [applyOpen, setApplyOpen] = useState(false);
  const [note, setNote] = useState('');
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [ceremony, setCeremony] = useState<GraduationCeremonyDto | null | 'none'>(null);
  const [certificate, setCertificate] = useState<GraduationCertificateDto | null>(null);

  const apply = () =>
    action.run(async () => {
      await api.post('/api/graduation/apply', { note: note.trim() || null, expectedGraduationYear: year ? Number(year) : null });
      setApplyOpen(false);
      setNote('');
      await overview.reload();
    });

  const withdraw = () =>
    action.run(async () => {
      await api.post('/api/graduation/application/withdraw');
      await overview.reload();
    });

  const loadCeremony = () =>
    action.run(async () => {
      try {
        setCeremony(await api.get<GraduationCeremonyDto>('/api/graduation/ceremony'));
      } catch (err) {
        if (err instanceof ApiError && err.status === 404) setCeremony('none');
        else throw err;
      }
    });

  const loadCertificate = () => action.run(async () => setCertificate(await api.get<GraduationCertificateDto>('/api/graduation/certificate')));

  return (
    <div className="card section-gap">
      <div className="card-header">
        <h3>
          <Icon name="graduation-cap" /> Graduation
        </h3>
      </div>
      <ErrorBox error={action.error} />
      <Async res={overview} loadingLabel="Checking your degree audit…">
        {(o) => {
          const status = o.applicationStatus;
          const open = status === 'Pending' || status === 'InReview';
          return (
            <div className="grad-audit">
              <div className="grad-audit-head">
                <div>
                  <strong>{o.degree || 'Degree'}</strong>
                  <div className="muted">
                    {[o.department, o.universityName, o.academicYear ? `Year ${o.academicYear}` : null].filter(Boolean).join(' · ')}
                  </div>
                </div>
                <div className="grad-audit-status">
                  {o.isGraduated ? (
                    <StatusBadge status="passed" label="Graduated" />
                  ) : o.isEligible ? (
                    <StatusBadge status="good" label="Eligible to apply" />
                  ) : (
                    <StatusBadge status="needs-attention" label="Not eligible yet" />
                  )}
                  {status !== 'none' && <StatusBadge status={status} label={`Application: ${status}`} />}
                </div>
              </div>

              <div className="grad-audit-metrics">
                <div>
                  <span className="muted">GPA</span>
                  <strong>
                    {o.gpa.toFixed(2)} <small className="muted">(min {o.minGpaRequired.toFixed(1)})</small>
                  </strong>
                </div>
                <div>
                  <span className="muted">Courses</span>
                  <strong>
                    {o.coursesCompleted}/{o.coursesRequired}
                  </strong>
                </div>
                <div>
                  <span className="muted">Credits</span>
                  <strong>
                    {o.creditsCompleted} done · {o.creditsInProgress} in progress
                  </strong>
                </div>
              </div>
              <Progress value={o.completionPercent} tone={o.completionPercent >= 100 ? 'success' : undefined} />

              <ul className="checklist">
                {o.checklist.map((c, cIdx) => (
                  <li key={`${c.key}-${cIdx}`} className={c.isMet ? 'met' : 'unmet'}>
                    <Icon name={c.isMet ? 'circle-check' : 'circle'} />
                    <div>
                      <strong>{c.title}</strong>
                      <div className="muted">{c.detail}</div>
                    </div>
                  </li>
                ))}
              </ul>

              {o.blockingReasons.length > 0 && !o.isGraduated && (
                <div className="alert alert-warning">
                  <strong>What&apos;s blocking graduation:</strong>
                  <ul className="plain-list bullet-list">
                    {o.blockingReasons.map((r, rIdx) => (
                      <li key={`${r}-${rIdx}`}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}

              {o.application && (
                <p className="muted">
                  Application #{o.application.id} submitted {fmtDateTime(o.application.submittedAtUtc)}
                  {o.application.reviewedAtUtc ? ` · reviewed ${fmtDateTime(o.application.reviewedAtUtc)}` : ''}
                  {o.application.decisionNote ? ` · “${o.application.decisionNote}”` : ''}
                </p>
              )}

              <div className="form-actions">
                {o.role === 'Student' && !o.isGraduated && !open && (
                  <button className="btn btn-primary" disabled={!o.isEligible || action.busy} onClick={() => setApplyOpen(true)} title={o.isEligible ? '' : 'Meet the requirements above first'}>
                    Apply for graduation
                  </button>
                )}
                {open && (
                  <button className="btn btn-secondary" disabled={action.busy} onClick={() => void withdraw()}>
                    Withdraw application
                  </button>
                )}
                <button className="btn btn-secondary" disabled={action.busy} onClick={() => void loadCeremony()}>
                  <Icon name="calendar-days" /> Next ceremony
                </button>
                {(o.isGraduated || o.alumni?.hasCertificate) && (
                  <button className="btn btn-secondary" disabled={action.busy} onClick={() => void loadCertificate()}>
                    <Icon name="award" /> View certificate
                  </button>
                )}
              </div>

              {ceremony === 'none' && <p className="muted">No graduation ceremony is scheduled yet.</p>}
              {ceremony && ceremony !== 'none' && (
                <div className="event-item">
                  <div className="event-info">
                    <strong>{ceremony.title}</strong>
                    <div className="muted">
                      {fmtDateTime(ceremony.startsAtUtc)} · {ceremony.location}
                    </div>
                    {ceremony.description && <p>{ceremony.description}</p>}
                  </div>
                </div>
              )}
            </div>
          );
        }}
      </Async>

      {applyOpen && (
        <Modal
          title="Apply for graduation"
          onClose={() => setApplyOpen(false)}
          footer={
            <>
              <button className="btn btn-secondary" onClick={() => setApplyOpen(false)}>
                Cancel
              </button>
              <button className="btn btn-primary" disabled={action.busy} onClick={() => void apply()}>
                {action.busy ? 'Submitting…' : 'Submit application'}
              </button>
            </>
          }
        >
          <div className="form-group">
            <label>Expected graduation year</label>
            <input type="number" value={year} onChange={(e) => setYear(e.target.value)} min={2000} max={2100} />
          </div>
          <div className="form-group">
            <label>Note to the registrar (optional)</label>
            <textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} />
          </div>
        </Modal>
      )}

      {certificate && (
        <Modal title="Digital certificate" onClose={() => setCertificate(null)}>
          <div className="certificate">
            <Icon name="award" size={40} />
            <p className="muted">This certifies that</p>
            <h2>{certificate.holder}</h2>
            <p>
              has been awarded the degree of <strong>{certificate.degree}</strong>
            </p>
            <p>{certificate.university}</p>
            <p>
              Class of {certificate.graduationYear} · Final GPA {certificate.finalGpa.toFixed(2)}
              {certificate.honors ? ` · ${certificate.honors}` : ''}
            </p>
            <p className="muted">
              Certificate {certificate.certificateId}
              {certificate.conferredOnUtc ? ` · conferred ${fmtDate(certificate.conferredOnUtc)}` : ''}
            </p>
          </div>
        </Modal>
      )}
    </div>
  );
}
