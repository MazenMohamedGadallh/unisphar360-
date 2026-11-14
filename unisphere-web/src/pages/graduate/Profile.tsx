import { useEffect, useState, type FormEvent } from 'react';
import { Icon } from '../../components/Icon';
import { Async, Empty, ErrorBox, PageHeader, Progress } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { fmtDateTime } from '../../lib/format';
import type { CvDocumentDto, GraduateProfileDto, GraduateProfileUpdateRequest } from '../../types/dto';

type Editable = Record<keyof GraduateProfileUpdateRequest, string>;

const FIELDS: Array<{ key: keyof GraduateProfileUpdateRequest; label: string; placeholder?: string; area?: boolean; wide?: boolean }> = [
  { key: 'headline', label: 'Professional headline', placeholder: 'Backend Developer | .NET & APIs', wide: true },
  { key: 'careerSummary', label: 'Career summary', area: true },
  { key: 'linkedInUrl', label: 'LinkedIn URL', placeholder: 'https://linkedin.com/in/…' },
  { key: 'gitHubUrl', label: 'GitHub URL', placeholder: 'https://github.com/…' },
  { key: 'portfolioUrl', label: 'Portfolio URL', placeholder: 'https://…' },
  { key: 'preferredJobTitles', label: 'Target job titles', placeholder: 'Backend Developer, Data Analyst' },
  { key: 'preferredIndustries', label: 'Preferred industries', placeholder: 'Tech, Fintech' },
  { key: 'preferredLocations', label: 'Preferred locations', placeholder: 'Cairo, Egypt, Remote' },
  { key: 'workModes', label: 'Work modes', placeholder: 'Remote, Hybrid, Onsite' },
  { key: 'employmentType', label: 'Employment type', placeholder: 'Full-time' },
  { key: 'experienceLevel', label: 'Experience level', placeholder: 'Entry-level' },
  { key: 'languages', label: 'Languages', placeholder: 'Arabic (Native), English (Fluent)' },
  { key: 'careerGoal', label: 'Career goal', area: true },
];

function toForm(p: GraduateProfileDto): Editable {
  const f = {} as Editable;
  for (const { key } of FIELDS) f[key] = (p[key as keyof GraduateProfileDto] as string | null) ?? '';
  return f;
}

const show = (v: string | null | undefined) => (v && v.trim() ? v.trim() : '—');

export default function GradProfile() {
  usePageTitle('My Career Profile');
  const profile = useApi<GraduateProfileDto>('/api/grad/profile');
  const cvs = useApi<CvDocumentDto[]>('/api/grad/profile/cv');
  const [form, setForm] = useState<Editable | null>(null);
  const [flash, setFlash] = useFlash();
  const action = useAction();
  const [skillName, setSkillName] = useState('');
  const [skillLevel, setSkillLevel] = useState('');
  const [entry, setEntry] = useState({ kind: 'Experience', title: '', organization: '', description: '', period: '' });

  useEffect(() => {
    if (profile.data) setForm(toForm(profile.data));
  }, [profile.data]);

  const latestCv = [...(cvs.data ?? [])].sort((a, b) => b.uploadedAtUtc.localeCompare(a.uploadedAtUtc))[0];

  const save = (e: FormEvent) => {
    e.preventDefault();
    if (!form) return;
    void action.run(async () => {
      const body: Record<string, string | null> = {};
      for (const { key } of FIELDS) body[key] = form[key].trim() || null;
      const updated = await api.put<GraduateProfileDto>('/api/grad/profile', body);
      if (updated) profile.setData(updated);
      else await profile.reload();
      setFlash('Saved.');
    });
  };

  const addSkill = () =>
    action.run(async () => {
      await api.post('/api/grad/profile/skills', { name: skillName.trim(), level: skillLevel.trim() || null });
      setSkillName('');
      setSkillLevel('');
      await profile.reload();
    });

  const removeSkill = (id: number) =>
    action.run(async () => {
      await api.del(`/api/grad/profile/skills/${id}`);
      await profile.reload();
    });

  const addEntry = () =>
    action.run(async () => {
      await api.post('/api/grad/profile/entries', {
        kind: entry.kind,
        title: entry.title.trim(),
        organization: entry.organization.trim() || null,
        description: entry.description.trim() || null,
        period: entry.period.trim() || null,
      });
      setEntry({ kind: entry.kind, title: '', organization: '', description: '', period: '' });
      await profile.reload();
    });

  const removeEntry = (id: number) =>
    action.run(async () => {
      await api.del(`/api/grad/profile/entries/${id}`);
      await profile.reload();
    });

  const uploadCv = (file: File | undefined) => {
    if (!file) return;
    if (file.size > 10 * 1024 * 1024) {
      action.setError('CV file is larger than the 10 MB limit.');
      return;
    }
    void action.run(async () => {
      const fd = new FormData();
      fd.append('file', file, file.name);
      await api.upload('/api/grad/profile/cv', fd);
      await Promise.all([cvs.reload(), profile.reload()]);
    });
  };

  return (
    <>
      <PageHeader title="My Career Profile" subtitle="Keep your profile current — employers and AI tools use it" />
      <ErrorBox error={action.error} />
      <Async res={profile} loadingLabel="Loading profile…">
        {(p) => (
          <>
            <div className="grad-overview-hero">
              <div className="hero-text">
                <h2>{p.fullName}</h2>
                <p>
                  {p.headline || 'No headline yet'} · completion {p.completionPercent}%
                </p>
              </div>
            </div>

            <div className="grad-two-col">
              <div className="card">
                <div className="card-header">
                  <h3>
                    <Icon name="user-round" /> Profile fields
                  </h3>
                </div>
                {form && (
                  <form onSubmit={save} className="profile-form">
                    <div className="form-grid-2">
                      {FIELDS.map((f, fIdx) => (
                        <div className={`form-group ${f.area || f.wide ? 'span-2' : ''}`} key={`${f.key}-${fIdx}`}>
                          <label htmlFor={`gr-${f.key}`}>{f.label}</label>
                          {f.area ? (
                            <textarea id={`gr-${f.key}`} rows={3} value={form[f.key]} onChange={(e) => setForm({ ...form, [f.key]: e.target.value })} />
                          ) : (
                            <input id={`gr-${f.key}`} value={form[f.key]} placeholder={f.placeholder} onChange={(e) => setForm({ ...form, [f.key]: e.target.value })} />
                          )}
                        </div>
                      ))}
                    </div>
                    <div className="form-actions">
                      <button type="submit" className="btn btn-primary" disabled={action.busy}>
                        {action.busy ? 'Saving…' : 'Save profile'}
                      </button>
                      {flash && <em className="muted">{flash}</em>}
                    </div>
                  </form>
                )}
              </div>

              <div className="stack">
                <div className="card">
                  <div className="card-header">
                    <h3>
                      <Icon name="graduation-cap" /> Education &amp; account
                    </h3>
                  </div>
                  <div className="kv-list">
                    <div>
                      <span>Field of study</span>
                      <span>{show(p.major)}</span>
                    </div>
                    <div>
                      <span>Department</span>
                      <span>{show(p.department)}</span>
                    </div>
                    <div>
                      <span>University</span>
                      <span>{show(p.universityName)}</span>
                    </div>
                    <div>
                      <span>Graduated from</span>
                      <span>{show(p.graduatedFrom)}</span>
                    </div>
                    <div>
                      <span>Graduation year</span>
                      <span>{p.graduationYear ?? '—'}</span>
                    </div>
                  </div>
                  <p className="muted small-print">Academic details are managed by the university. Contact support to correct them.</p>
                </div>

                <div className="card">
                  <div className="card-header">
                    <h3>
                      <Icon name="target" /> Profile completion
                    </h3>
                    <span className="grad-badge">{p.completionPercent}%</span>
                  </div>
                  <Progress value={p.completionPercent} tone={p.completionPercent >= 80 ? 'success' : undefined} />
                  {p.missingFields.length === 0 ? (
                    <p className="muted small-print">Everything is filled in — well done.</p>
                  ) : (
                    <>
                      <p className="muted small-print">Still missing:</p>
                      <div className="tag-list">
                        {p.missingFields.map((m, mIdx) => (
                          <span className="tag tag-danger" key={`${m}-${mIdx}`}>
                            {m}
                          </span>
                        ))}
                      </div>
                    </>
                  )}
                </div>

                <div className="card">
                  <div className="card-header">
                    <h3>
                      <Icon name="file-text" /> CV &amp; Resume
                    </h3>
                    <Link className="btn btn-sm btn-secondary" to="/graduate/cv">
                      Open CV Studio
                    </Link>
                  </div>
                  {!latestCv ? (
                    <p className="muted">No CV on file yet. Upload one so AI matching, interview prep, and skill-gap analysis have real material to work with.</p>
                  ) : (
                    <div className="cv-latest">
                      <Icon name="file-text" size={18} />
                      <div>
                        <strong>{latestCv.fileName}</strong> <span className="muted">· {Math.round(latestCv.sizeBytes / 1024)} KB</span>
                        <div className="muted">
                          Uploaded {fmtDateTime(latestCv.uploadedAtUtc)} · {latestCv.parseStatus} {latestCv.hasAnalysis && <span className="grad-badge">AI analysis ready</span>}
                        </div>
                        {latestCv.parseError && <p className="muted">Extraction note: {latestCv.parseError}. You can enter manual text in CV Studio.</p>}
                      </div>
                    </div>
                  )}
                  <label className="file-button">
                    <input type="file" accept=".pdf,.doc,.docx" onChange={(e) => uploadCv(e.target.files?.[0])} disabled={action.busy} />
                    <span className="btn btn-primary btn-sm">{action.busy ? 'Working…' : 'Upload CV (PDF or DOCX)'}</span>
                  </label>
                </div>

                <div className="card">
                  <div className="card-header">
                    <h3>
                      <Icon name="star" /> Skills ({p.skills.length})
                    </h3>
                  </div>
                  {p.skills.length === 0 ? (
                    <Empty icon="sparkles">No skills added yet.</Empty>
                  ) : (
                    <ul className="grad-list">
                      {p.skills.map((s) => (
                        <li key={s.id} className="row-between">
                          <span>
                            <strong>{s.name}</strong> <span className="muted">{s.level || 'no level set'}</span> <span className="grad-badge">{s.source}</span>
                          </span>
                          <button className="btn btn-sm btn-danger" disabled={action.busy} onClick={() => void removeSkill(s.id)}>
                            Remove
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                  <div className="form-inline">
                    <div className="form-group">
                      <label htmlFor="grSkillName">New skill</label>
                      <input id="grSkillName" value={skillName} onChange={(e) => setSkillName(e.target.value)} placeholder="C#" maxLength={100} />
                    </div>
                    <div className="form-group">
                      <label htmlFor="grSkillLevel">Level</label>
                      <input id="grSkillLevel" value={skillLevel} onChange={(e) => setSkillLevel(e.target.value)} placeholder="Intermediate" maxLength={50} />
                    </div>
                    <button className="btn btn-primary btn-sm" onClick={() => void addSkill()} disabled={action.busy || !skillName.trim()}>
                      Add skill
                    </button>
                  </div>
                </div>

                <div className="card">
                  <div className="card-header">
                    <h3>Experience &amp; projects</h3>
                  </div>
                  {p.entries.length === 0 ? (
                    <Empty icon="file-text">No entries yet. Add your experience, projects, certifications or education.</Empty>
                  ) : (
                    <ul className="grad-list">
                      {p.entries.map((en) => (
                        <li key={en.id} className="row-between">
                          <span>
                            <span className="grad-badge">{en.kind}</span> <strong>{en.title}</strong>
                            {en.organization && <span className="muted"> · {en.organization}</span>}
                            {en.period && <span className="muted"> · {en.period}</span>}
                            {en.description && <div className="muted">{en.description}</div>}
                          </span>
                          <button className="btn btn-sm btn-secondary" disabled={action.busy} onClick={() => void removeEntry(en.id)}>
                            Remove
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                  <div className="form-inline">
                    <div className="form-group">
                      <label htmlFor="grEntryKind">Type</label>
                      <select id="grEntryKind" value={entry.kind} onChange={(e) => setEntry({ ...entry, kind: e.target.value })}>
                        <option>Experience</option>
                        <option>Project</option>
                        <option>Certification</option>
                        <option>Education</option>
                      </select>
                    </div>
                    <div className="form-group">
                      <label htmlFor="grEntryTitle">Title</label>
                      <input id="grEntryTitle" value={entry.title} onChange={(e) => setEntry({ ...entry, title: e.target.value })} placeholder="Backend Engineering Intern" />
                    </div>
                  </div>
                  <div className="form-group">
                    <label htmlFor="grEntryOrg">Organization</label>
                    <input id="grEntryOrg" value={entry.organization} onChange={(e) => setEntry({ ...entry, organization: e.target.value })} placeholder="Company / university" />
                  </div>
                  <div className="form-group">
                    <label htmlFor="grEntryDesc">Description</label>
                    <textarea id="grEntryDesc" rows={2} value={entry.description} onChange={(e) => setEntry({ ...entry, description: e.target.value })} />
                  </div>
                  <div className="form-inline">
                    <div className="form-group">
                      <label htmlFor="grEntryPeriod">Period</label>
                      <input id="grEntryPeriod" value={entry.period} onChange={(e) => setEntry({ ...entry, period: e.target.value })} placeholder="2025 - 2026" />
                    </div>
                    <button className="btn btn-primary btn-sm" onClick={() => void addEntry()} disabled={action.busy || !entry.title.trim()}>
                      Add
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </>
        )}
      </Async>
    </>
  );
}
