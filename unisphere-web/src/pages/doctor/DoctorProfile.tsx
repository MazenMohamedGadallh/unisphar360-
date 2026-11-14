import { useEffect, useState } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ErrorBox, PageHeader } from '../../components/ui';
import { api, assetUrl } from '../../lib/api';
import { useAuth } from '../../lib/auth';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { initials } from '../../lib/format';
import type { DoctorProfileCourseDto, DoctorProfileDto, MaterialUploadResultDto } from '../../types/dto';
import { ChangePasswordCard } from '../shared/Settings';

export default function DoctorProfile() {
  usePageTitle('Doctor Profile');
  const { refreshUser } = useAuth();
  const profile = useApi<DoctorProfileDto>('/api/doctor/profile');
  const action = useAction();
  const [flash, setFlash] = useFlash(5000);
  const [avatarInput, setAvatarInput] = useState('');
  const [editing, setEditing] = useState<number | null>(null);
  const [title, setTitle] = useState('');

  useEffect(() => {
    setAvatarInput(profile.data?.avatarUrl ?? '');
  }, [profile.data?.avatarUrl]);

  const saveAvatar = (url: string) =>
    action.run(async () => {
      await api.put('/api/doctor/profile', { avatarUrl: url.trim() || null });
      await profile.reload();
      await refreshUser().catch(() => undefined);
      setFlash('Avatar updated.');
    });

  const uploadAvatar = (file: File | undefined) => {
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) return action.setError('Avatar images are limited to 5 MB.');
    void action.run(async () => {
      const fd = new FormData();
      fd.append('file', file, file.name);
      const res = await api.upload<MaterialUploadResultDto>('/api/doctor/profile/avatar', fd);
      await api.put('/api/doctor/profile', { avatarUrl: res.fileUrl });
      await profile.reload();
      await refreshUser().catch(() => undefined);
      setFlash('Avatar uploaded.');
    });
  };

  const saveTitle = (c: DoctorProfileCourseDto) => {
    const t = title.trim();
    if (t && t.length < 3) return action.setError('Display title must be at least 3 characters.');
    void action.run(async () => {
      await api.put(`/api/doctor/courses/${c.courseId}/display-title`, { displayTitle: t || null });
      setEditing(null);
      setFlash(t ? `Students now see "${t}".` : 'Display title cleared — students will see the catalog title.');
      await profile.reload();
    });
  };

  const locked = (label: string, value: string | null | undefined) => (
    <div className="locked-field" key={label}>
      <span className="locked-label">
        {label}{' '}
        <span className="lock-icon" title="Locked">
          <Icon name="lock" />
        </span>
      </span>
      <span className="locked-value">{value && value.trim() ? value : '—'}</span>
    </div>
  );

  return (
    <>
      <PageHeader title="My Profile" subtitle="Personal details are verified by the administration. You can edit your avatar, password and course display titles." />
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <Async res={profile} loadingLabel="Loading profile…">
        {(p) => (
          <>
            <div className="two-column">
              <div className="card">
                <div className="card-header">
                  <h3>Personal Information</h3>
                  <span className="status-badge good">
                    <Icon name="lock" /> Locked
                  </span>
                </div>
                <div className="locked-hint">
                  <Icon name="info" /> Contact an administrator to change this.
                </div>
                <div className="locked-fields">
                  {locked('First Name', p.firstName)}
                  {locked('Middle Name', p.middleName)}
                  {locked('Last Name', p.lastName)}
                  {locked('Email', p.email)}
                  {locked('Address', p.address)}
                  {locked('Phone Number', p.phoneNumber)}
                  {locked('University Name', p.universityName)}
                  {locked('Department', p.department)}
                  {locked('Specialization', p.specialization)}
                </div>
              </div>
              <div className="stack">
                <div className="card">
                  <div className="card-header">
                    <h3>Avatar</h3>
                    <span className="status-badge excellent">
                      <Icon name="pencil" /> Editable
                    </span>
                  </div>
                  <div className="avatar-preview-wrap">
                    {p.avatarUrl ? <img className="avatar-preview" src={assetUrl(p.avatarUrl)} alt="Avatar" /> : <span className="avatar-initial avatar-lg">{initials(p.fullName)}</span>}
                  </div>
                  <div className="form-group">
                    <label htmlFor="avatarUrl">Avatar URL</label>
                    <input id="avatarUrl" value={avatarInput} onChange={(e) => setAvatarInput(e.target.value)} placeholder="/uploads/doctor/avatar/… or https://…" />
                  </div>
                  <div className="form-group">
                    <label>Or upload an image (PNG / JPEG / WebP, max 5 MB)</label>
                    <input type="file" accept=".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp" onChange={(e) => uploadAvatar(e.target.files?.[0])} disabled={action.busy} />
                  </div>
                  <button className="btn btn-primary" onClick={() => void saveAvatar(avatarInput)} disabled={action.busy}>
                    {action.busy ? 'Saving…' : 'Save Avatar'}
                  </button>
                </div>
              </div>
            </div>
            <ChangePasswordCard />
            <div className="card section-gap-top">
              <div className="card-header">
                <h3>My Course Display Titles</h3>
                <span className="status-badge excellent">
                  <Icon name="pencil" /> Editable
                </span>
              </div>
              <p className="locked-hint">The name students see for your courses. The official course code and catalog title stay unchanged.</p>
              {p.courses.length === 0 ? (
                <div className="empty-state">You are not assigned to any course yet. Contact an administrator.</div>
              ) : (
                <div className="table-wrapper">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Code</th>
                        <th>Current display title</th>
                        <th>Catalog title</th>
                        <th>Term</th>
                        <th>Enrolled</th>
                        <th />
                      </tr>
                    </thead>
                    <tbody>
                      {p.courses.map((c) => (
                        <tr key={c.courseId}>
                          <td>
                            <span className="rec-code">{c.code}</span>
                          </td>
                          <td>
                            {editing === c.courseId ? (
                              <input className="display-title-input" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} autoFocus />
                            ) : (
                              <>
                                <strong>{c.displayTitle || c.catalogTitle}</strong> {c.displayTitle && <span className="rec-track">(custom)</span>}
                              </>
                            )}
                          </td>
                          <td>{c.catalogTitle}</td>
                          <td>{c.semester}</td>
                          <td>
                            <Link to={`/doctor/courses/${c.courseId}`}>
                              {c.enrolledCount} student{c.enrolledCount === 1 ? '' : 's'}
                            </Link>
                          </td>
                          <td>
                            {editing === c.courseId ? (
                              <div className="row-actions">
                                <button className="btn btn-sm btn-primary" onClick={() => saveTitle(c)} disabled={action.busy}>
                                  Save
                                </button>
                                <button className="btn btn-sm btn-secondary" onClick={() => setEditing(null)}>
                                  Cancel
                                </button>
                              </div>
                            ) : (
                              <button
                                className="btn btn-sm btn-secondary"
                                onClick={() => {
                                  setEditing(c.courseId);
                                  setTitle(c.displayTitle ?? c.catalogTitle);
                                }}
                              >
                                Rename
                              </button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </>
        )}
      </Async>
    </>
  );
}
