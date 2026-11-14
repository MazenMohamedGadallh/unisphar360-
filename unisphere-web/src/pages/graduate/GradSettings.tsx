import { useEffect, useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { useTheme, type ThemePref } from '../../lib/theme';
import type { GradSettingsDto } from '../../types/dto';
import { ChangePasswordCard } from '../shared/Settings';

const THEMES: Array<{ value: ThemePref; label: string }> = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
];

export default function GradSettings() {
  usePageTitle('Settings');
  const { user } = useAuth();
  const theme = useTheme();
  const settings = useApi<GradSettingsDto>('/api/grad/settings');
  const action = useAction();
  const [flash, setFlash] = useFlash();
  const [form, setForm] = useState({ notifyNewMatches: true, notifyFollowUps: true, threshold: 50, keywords: '', locations: '' });

  useEffect(() => {
    const s = settings.data;
    if (!s) return;
    setForm({
      notifyNewMatches: s.notifyNewMatches,
      notifyFollowUps: s.notifyFollowUps,
      threshold: s.matchAlertThresholdPercent,
      keywords: s.alertKeywords ?? '',
      locations: s.alertLocations ?? '',
    });
  }, [settings.data]);

  const payload = (themePreference: string) => ({
    notifyNewMatches: form.notifyNewMatches,
    notifyFollowUps: form.notifyFollowUps,
    matchAlertThresholdPercent: form.threshold,
    showGpaOnProfile: settings.data?.showGpaOnProfile ?? false,
    themePreference,
    alertKeywords: form.keywords.trim() || null,
    alertLocations: form.locations.trim() || null,
  });

  const save = () =>
    action.run(async () => {
      const updated = await api.put<GradSettingsDto>('/api/grad/settings', payload(settings.data?.themePreference ?? theme.pref));
      if (updated) settings.setData(updated);
      setFlash('Settings saved.');
    });

  const changeTheme = (pref: ThemePref) => {
    theme.setPref(pref);
    if (!settings.data) return;
    void action.run(async () => {
      const updated = await api.put<GradSettingsDto>('/api/grad/settings', payload(pref));
      if (updated) settings.setData(updated);
    });
  };

  return (
    <>
      <PageHeader title="Settings" subtitle="Account preferences, appearance, and how career alerts reach you" />
      <ErrorBox error={action.error} />
      <Async res={settings} loadingLabel="Loading settings…">
        {() => (
          <>
            <div className="card narrow-card">
              <div className="card-header">
                <h3>
                  <Icon name="user-round" /> Account
                </h3>
              </div>
              <div className="kv-list">
                <div>
                  <span>Full name</span>
                  <span>{user?.fullName}</span>
                </div>
                <div>
                  <span>Email</span>
                  <span>{user?.email}</span>
                </div>
                <div>
                  <span>Field</span>
                  <span>{user?.major || '—'}</span>
                </div>
                <div>
                  <span>Role</span>
                  <span className="grad-badge">Graduate</span>
                </div>
              </div>
              <p className="muted small-print">
                Personal details are managed on <Link to="/graduate/profile">My Career Profile</Link>. Email changes are not supported yet.
              </p>
              <h4 className="mini-heading">
                <Icon name="sun-moon" /> Appearance
              </h4>
              <div className="form-inline" role="group" aria-label="Theme">
                {THEMES.map((t, tIdx) => (
                  <button key={`${t.value}-${tIdx}`} type="button" className={`btn ${theme.pref === t.value ? 'btn-primary' : 'btn-secondary'}`} aria-pressed={theme.pref === t.value} onClick={() => changeTheme(t.value)}>
                    {t.label}
                  </button>
                ))}
              </div>
              <p className="muted small-print">Applies instantly and is saved with your account.</p>
            </div>

            <div className="card narrow-card section-gap-top">
              <div className="card-header">
                <h3>
                  <Icon name="bell" /> Career notifications
                </h3>
              </div>
              <label className="toggle-row">
                <span>
                  <strong>Notify me about strong new job matches</strong>
                  <span className="muted block">Checked whenever the portal indexes jobs that fit your CV.</span>
                </span>
                <input type="checkbox" checked={form.notifyNewMatches} onChange={(e) => setForm({ ...form, notifyNewMatches: e.target.checked })} />
              </label>
              <div className="toggle-row">
                <span>
                  <strong>Match alert threshold</strong>
                  <span className="muted block">Only matches scoring at least this many percent notify you.</span>
                  <input
                    type="range"
                    min={0}
                    max={100}
                    step={5}
                    value={form.threshold}
                    disabled={!form.notifyNewMatches}
                    onChange={(e) => setForm({ ...form, threshold: Number(e.target.value) })}
                    aria-label="Match alert threshold"
                  />
                </span>
                <span className="grad-badge">≥ {form.threshold}%</span>
              </div>
              <label className="toggle-row">
                <span>
                  <strong>Remind me about follow-ups</strong>
                  <span className="muted block">Interviews, application deadlines, and their follow-up dates.</span>
                </span>
                <input type="checkbox" checked={form.notifyFollowUps} onChange={(e) => setForm({ ...form, notifyFollowUps: e.target.checked })} />
              </label>
              <h4 className="mini-heading">
                <Icon name="target" /> Job alert targeting
              </h4>
              <p className="muted small-print">Optional. Leave blank to be alerted about everything above your threshold.</p>
              <div className="form-group">
                <label htmlFor="grKeywords">Alert keywords (comma-separated, matched against job title and company)</label>
                <input id="grKeywords" value={form.keywords} onChange={(e) => setForm({ ...form, keywords: e.target.value })} placeholder="e.g. data analyst, fintech" maxLength={300} />
              </div>
              <div className="form-group">
                <label htmlFor="grLocations">Alert locations (comma-separated, matched against job location)</label>
                <input id="grLocations" value={form.locations} onChange={(e) => setForm({ ...form, locations: e.target.value })} placeholder="e.g. Riyadh, remote" maxLength={300} />
              </div>
              <div className="form-actions">
                <button className="btn btn-primary" onClick={() => void save()} disabled={action.busy}>
                  {action.busy ? 'Saving…' : 'Save settings'}
                </button>
                {flash && <span className="grad-badge">{flash}</span>}
              </div>
            </div>

            <div className="card narrow-card section-gap-top">
              <div className="card-header">
                <h3>
                  <Icon name="inbox" /> Where alerts appear
                </h3>
              </div>
              <p>Your alerts are in-app only, delivered to the notification center:</p>
              <Link className="btn btn-secondary" to="/notifications">
                <Icon name="bell" /> Open notifications
              </Link>
            </div>
            <ChangePasswordCard />
          </>
        )}
      </Async>
    </>
  );
}
