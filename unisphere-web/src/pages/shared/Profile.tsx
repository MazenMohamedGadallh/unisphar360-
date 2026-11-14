import { useEffect, useState, type FormEvent } from 'react';
import { Alert, ErrorBox, Loading, PageHeader } from '../../components/ui';
import { api, assetUrl, errorMessage } from '../../lib/api';
import { useAuth } from '../../lib/auth';
import { useApi, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { initials } from '../../lib/format';
import type { MeDto } from '../../types/dto';

function majorLabel(role: string | undefined): string {
  switch (role) {
    case 'Doctor': return 'Field of teaching (department)';
    case 'Graduate': return 'Field of study';
    default: return 'Major';
  }
}

export default function Profile() {
  usePageTitle('My Profile');
  const { refreshUser } = useAuth();
  const me = useApi<MeDto>('/api/users/me');
  const [fullName, setFullName] = useState('');
  const [major, setMajor] = useState('');
  const [avatarUrl, setAvatarUrl] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    if (!me.data) return;
    setFullName(me.data.fullName);
    setMajor(me.data.major);
    setAvatarUrl(me.data.avatarUrl ?? '');
  }, [me.data]);

  const isDoctor = me.data?.role === 'Doctor';

  const save = async (e: FormEvent) => {
    e.preventDefault();
    if (!me.data) return;
    setSaving(true);
    setMessage(null);
    try {
      await api.put(`/api/users/${me.data.id}`, { fullName: fullName.trim(), major: major.trim(), avatarUrl: avatarUrl.trim() || null });
      await me.reload();
      await refreshUser().catch(() => undefined);
      setMessage({ ok: true, text: 'Profile updated.' });
    } catch (err) {
      setMessage({ ok: false, text: errorMessage(err, 'Save failed. Please check your input and try again.') });
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <PageHeader title="My Profile" subtitle="View and edit your personal information." />
      <ErrorBox error={me.error} onRetry={() => void me.reload()} />
      <div className="card narrow-card">
        <div className="card-header">
          <h3>Personal Information</h3>
        </div>
        {me.loading && !me.data ? (
          <Loading label="Loading profile…" />
        ) : me.data ? (
          <form onSubmit={save} className="profile-form">
            <div className="profile-avatar-row">
              {avatarUrl ? <img className="avatar-preview" src={assetUrl(avatarUrl)} alt="" /> : <span className="avatar-initial avatar-lg">{initials(fullName)}</span>}
              <div>
                <strong>{me.data.fullName}</strong>
                <div className="muted">{me.data.role}</div>
              </div>
            </div>
            {isDoctor && (
              <Alert kind="info">
                Doctor identity fields are locked; only the avatar can be changed here. Your full doctor profile is on the{' '}
                <Link to="/doctor/profile">Doctor Profile</Link> page.
              </Alert>
            )}
            <div className="form-group">
              <label>Full Name</label>
              <input type="text" value={fullName} onChange={(e) => setFullName(e.target.value)} required maxLength={200} disabled={isDoctor} />
            </div>
            <div className="form-group">
              <label>Email</label>
              <input type="email" value={me.data.email} disabled />
            </div>
            <div className="form-group">
              <label>{majorLabel(me.data.role)}</label>
              <input type="text" value={major} onChange={(e) => setMajor(e.target.value)} required maxLength={200} disabled={isDoctor} />
            </div>
            <div className="form-group">
              <label>Role</label>
              <input type="text" value={me.data.role} disabled />
            </div>
            <div className="form-group">
              <label>Avatar URL</label>
              <input type="url" value={avatarUrl} onChange={(e) => setAvatarUrl(e.target.value)} placeholder="https://…" maxLength={500} />
            </div>
            {message && <div className={`alert ${message.ok ? 'alert-success' : 'alert-error'}`}>{message.text}</div>}
            <div className="form-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>
                {saving ? 'Saving…' : 'Save Changes'}
              </button>
              <Link to="/settings" className="btn btn-secondary">
                Change password
              </Link>
            </div>
          </form>
        ) : null}
      </div>
    </>
  );
}
