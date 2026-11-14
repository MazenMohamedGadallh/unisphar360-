import { useEffect, useState, type FormEvent } from 'react';
import { PageHeader } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useAuth } from '../../lib/auth';
import { useApi, usePageTitle } from '../../lib/hooks';
import { useNavigate } from '../../lib/router';
import { useTheme, type ThemePref } from '../../lib/theme';
import { fromDateTimeInput, toDateTimeInput } from '../../lib/format';
import type { UserNotificationSettingDto } from '../../types/dto';

const THEMES: Array<{ value: ThemePref; label: string }> = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
];

export function ThemePicker() {
  const theme = useTheme();
  return (
    <div className="form-inline" role="group" aria-label="Theme">
      {THEMES.map((t, tIdx) => (
        <button
          key={`${t.value}-${tIdx}`}
          type="button"
          className={`btn ${theme.pref === t.value ? 'btn-primary' : 'btn-secondary'}`}
          aria-pressed={theme.pref === t.value}
          onClick={() => theme.setPref(t.value)}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

export function ChangePasswordCard() {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setMessage(null);
    if (next !== confirm) {
      setMessage({ ok: false, text: 'New passwords do not match.' });
      return;
    }
    if (next.length < 8) {
      setMessage({ ok: false, text: 'New password must be at least 8 characters.' });
      return;
    }
    setSaving(true);
    try {
      await api.post('/api/auth/change-password', { currentPassword: current, newPassword: next });
      setMessage({ ok: true, text: 'Password updated successfully.' });
      setCurrent('');
      setNext('');
      setConfirm('');
    } catch (err) {
      setMessage({ ok: false, text: errorMessage(err, 'Failed to update the password. Please try again.') });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card narrow-card section-gap-top">
      <div className="card-header">
        <h3>Change Password</h3>
      </div>
      <form onSubmit={submit} className="settings-form">
        <div className="form-group">
          <label>Current Password</label>
          <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} required autoComplete="current-password" />
        </div>
        <div className="form-group">
          <label>New Password</label>
          <input type="password" value={next} onChange={(e) => setNext(e.target.value)} required minLength={8} autoComplete="new-password" />
        </div>
        <div className="form-group">
          <label>Confirm New Password</label>
          <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required autoComplete="new-password" />
        </div>
        {message && <div className={`alert ${message.ok ? 'alert-success' : 'alert-error'}`}>{message.text}</div>}
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? 'Updating…' : 'Update Password'}
        </button>
      </form>
    </div>
  );
}

function NotificationPreferenceCard() {
  const setting = useApi<UserNotificationSettingDto>('/api/user/notification-settings');
  const [pref, setPref] = useState('On');
  const [until, setUntil] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    if (!setting.data) return;
    setPref(setting.data.preference || 'On');
    setUntil(toDateTimeInput(setting.data.mutedUntilUtc));
  }, [setting.data]);

  const save = async () => {
    setSaving(true);
    setMessage(null);
    try {
      const res = await api.put<UserNotificationSettingDto>('/api/user/notification-settings', {
        preference: pref,
        mutedUntilUtc: pref === 'MuteUntil' ? fromDateTimeInput(until) : null,
      });
      setting.setData(res);
      setMessage({ ok: true, text: 'Notification preference saved.' });
    } catch (err) {
      setMessage({ ok: false, text: errorMessage(err) });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card narrow-card section-gap-top">
      <div className="card-header">
        <h3>Notifications</h3>
      </div>
      {setting.error ? (
        <div className="alert alert-error">{setting.error}</div>
      ) : (
        <>
          <div className="form-group">
            <label>Exam &amp; announcement notifications</label>
            <select value={pref} onChange={(e) => setPref(e.target.value)}>
              <option value="On">On</option>
              <option value="MuteUntil">Mute until…</option>
              <option value="Off">Off</option>
            </select>
          </div>
          {pref === 'MuteUntil' && (
            <div className="form-group">
              <label>Muted until</label>
              <input type="datetime-local" value={until} onChange={(e) => setUntil(e.target.value)} />
            </div>
          )}
          {message && <div className={`alert ${message.ok ? 'alert-success' : 'alert-error'}`}>{message.text}</div>}
          <button className="btn btn-primary" onClick={() => void save()} disabled={saving || (pref === 'MuteUntil' && !until)}>
            {saving ? 'Saving…' : 'Save preference'}
          </button>
        </>
      )}
    </div>
  );
}

export default function Settings() {
  usePageTitle('Settings');
  const { logout } = useAuth();
  const navigate = useNavigate();

  return (
    <>
      <PageHeader title="Settings" subtitle="Manage your account preferences." />
      <div className="card narrow-card">
        <div className="card-header">
          <h3>Appearance</h3>
        </div>
        <p className="muted small-print">Light, dark, or follow your system preference. Applies immediately and is remembered on this device.</p>
        <ThemePicker />
      </div>
      <NotificationPreferenceCard />
      <ChangePasswordCard />
      <div className="card narrow-card section-gap-top">
        <div className="card-header">
          <h3>Account Actions</h3>
        </div>
        <div className="settings-actions">
          <button
            className="btn btn-danger"
            onClick={() => {
              logout();
              navigate('/login', { replace: true });
            }}
          >
            Logout
          </button>
        </div>
      </div>
    </>
  );
}
