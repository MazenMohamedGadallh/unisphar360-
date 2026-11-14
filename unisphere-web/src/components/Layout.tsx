import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Icon } from './Icon';
import { Modal } from './ui';
import { api, assetUrl } from '../lib/api';
import { useAuth, type Role } from '../lib/auth';
import { NavLink, useLocation, useNavigate } from '../lib/router';
import { useTheme } from '../lib/theme';
import { initials } from '../lib/format';
import type { SearchItemDto, SearchResponseDto, UnreadCountDto } from '../types/dto';

interface NavEntry {
  to: string;
  label: string;
  icon: string;
  end?: boolean;
}

// Sidebar entries per role, taken from MainLayout.razor, RoleNavMenu.razor and GradNav.razor.
const NAV: Record<Role, NavEntry[]> = {
  Student: [
    { to: '/dashboard', label: 'Home', icon: 'house', end: true },
    { to: '/university', label: 'My University', icon: 'landmark' },
    { to: '/academic-advisor', label: 'Academic Advisor', icon: 'graduation-cap' },
    { to: '/study-planner', label: 'Study Planner', icon: 'calendar-days' },
    { to: '/career-assistant', label: 'Career Assistant', icon: 'briefcase' },
    { to: '/research-hub', label: 'Research Hub', icon: 'microscope' },
    { to: '/student-services', label: 'Student Services', icon: 'headset' },
    { to: '/analytics', label: 'My Analytics', icon: 'chart-no-axes-combined' },
    { to: '/student/exams', label: 'My Exams', icon: 'clipboard-check' },
    { to: '/events', label: 'Student Events', icon: 'calendar-days' },
    { to: '/chat', label: 'AI Assistant', icon: 'sparkles' },
  ],
  Graduate: [
    { to: '/graduate', label: 'Career Overview', icon: 'house', end: true },
    { to: '/graduate/profile', label: 'My Career Profile', icon: 'user-round' },
    { to: '/graduate/cv', label: 'CV & Resume Studio', icon: 'file-text' },
    { to: '/graduate/jobs', label: 'Find Jobs', icon: 'search' },
    { to: '/graduate/matches', label: 'AI Job Matches', icon: 'thumbs-up' },
    { to: '/graduate/skill-gap', label: 'Skill Gap Analysis', icon: 'target' },
    { to: '/graduate/roadmap', label: 'Career Roadmap', icon: 'map' },
    { to: '/graduate/applications', label: 'My Applications', icon: 'file-check-2' },
    { to: '/graduate/interview', label: 'AI Interview Coach', icon: 'mic' },
    { to: '/graduate/assistant', label: 'AI Career Assistant', icon: 'message-square' },
    { to: '/graduate/alumni', label: 'Alumni & University Services', icon: 'landmark' },
    { to: '/graduate/events', label: 'Graduated Events', icon: 'party-popper' },
    { to: '/graduate/insights', label: 'Career Insights', icon: 'chart-no-axes-combined' },
    { to: '/research-hub', label: 'Research Hub', icon: 'microscope' },
  ],
  Doctor: [
    { to: '/doctor', label: 'Home', icon: 'house', end: true },
    { to: '/doctor/courses', label: 'My Courses', icon: 'book-open' },
    { to: '/doctor/research', label: 'Research Hub', icon: 'microscope' },
    { to: '/doctor/profile', label: 'Doctor Profile', icon: 'user-round' },
    { to: '/doctor/students', label: 'My Students', icon: 'users' },
    { to: '/doctor/materials', label: 'Materials', icon: 'file-text' },
    { to: '/doctor/announcements', label: 'Announcements', icon: 'megaphone' },
    { to: '/doctor/ai', label: 'AI Assistant', icon: 'sparkles' },
    { to: '/doctor/exam-assistant', label: 'Exam Assistant', icon: 'clipboard-list' },
    { to: '/chat', label: 'AI Chat', icon: 'message-square' },
  ],
  Admin: [
    { to: '/admin', label: 'Admin Panel', icon: 'shield-check' },
    { to: '/doctor/research', label: 'Research Hub', icon: 'microscope' },
  ],
};

const RECENTS_KEY = 'unisphere.recentSearches';

function loadRecents(): string[] {
  try {
    const v = JSON.parse(localStorage.getItem(RECENTS_KEY) ?? '[]') as unknown;
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string').slice(0, 6) : [];
  } catch {
    return [];
  }
}

function saveRecent(term: string): string[] {
  const next = [term, ...loadRecents().filter((t) => t.toLowerCase() !== term.toLowerCase())].slice(0, 6);
  try {
    localStorage.setItem(RECENTS_KEY, JSON.stringify(next));
  } catch {
    /* ignore */
  }
  return next;
}

function Highlight({ text, term }: { text: string; term: string }) {
  const t = term.trim();
  if (t.length < 2) return <>{text}</>;
  const i = text.toLowerCase().indexOf(t.toLowerCase());
  if (i < 0) return <>{text}</>;
  return (
    <>
      {text.slice(0, i)}
      <mark>{text.slice(i, i + t.length)}</mark>
      {text.slice(i + t.length)}
    </>
  );
}

function TopSearch() {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [groups, setGroups] = useState<SearchResponseDto['groups']>([]);
  const [selected, setSelected] = useState(-1);
  const [recents, setRecents] = useState<string[]>(loadRecents);
  const inputRef = useRef<HTMLInputElement>(null);
  const seq = useRef(0);

  const flat = useMemo(() => groups.flatMap((g) => g.items), [groups]);

  // Ctrl+K and "/" focus the search box (wwwroot/js/search.js behaviour).
  useEffect(() => {
    const onKey = (e: globalThis.KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      const tag = (el?.tagName ?? '').toLowerCase();
      const typing = tag === 'input' || tag === 'textarea' || tag === 'select' || el?.isContentEditable;
      if (((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') || (e.key === '/' && !typing)) {
        e.preventDefault();
        inputRef.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  useEffect(() => {
    const term = query.trim();
    setSelected(-1);
    if (term.length < 2) {
      seq.current++;
      setGroups([]);
      setLoading(false);
      setError(null);
      return;
    }
    const id = ++seq.current;
    setLoading(true);
    setError(null);
    const t = setTimeout(async () => {
      try {
        const res = await api.get<SearchResponseDto>(`/api/search?q=${encodeURIComponent(term)}&take=20`);
        if (id === seq.current) setGroups(res?.groups ?? []);
      } catch {
        if (id === seq.current) setError('Search failed. Please try again.');
      } finally {
        if (id === seq.current) setLoading(false);
      }
    }, 300);
    return () => clearTimeout(t);
  }, [query]);

  const go = (item: SearchItemDto) => {
    setRecents(saveRecent(query.trim()));
    setOpen(false);
    setQuery('');
    navigate(item.route);
  };

  const fullSearch = (term: string) => {
    const t = term.trim();
    if (!t) return;
    setRecents(saveRecent(t));
    setOpen(false);
    navigate(`/search?q=${encodeURIComponent(t)}`);
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') {
      setOpen(false);
      inputRef.current?.blur();
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      setOpen(true);
      setSelected((s) => (flat.length === 0 ? -1 : Math.min(s + 1, flat.length - 1)));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setSelected((s) => Math.max(s - 1, -1));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (selected >= 0 && selected < flat.length) go(flat[selected]);
      else fullSearch(query);
    }
  };

  const term = query.trim();
  const showRecents = term.length === 0 && recents.length > 0;
  let index = -1;

  return (
    <div className="search-wrap">
      <div className="search-box">
        <span className="search-icon">
          <Icon name="search" />
        </span>
        <input
          ref={inputRef}
          type="text"
          placeholder="Search courses, events, modules…  (Ctrl+K)"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onKeyDown={onKeyDown}
          autoComplete="off"
          role="combobox"
          aria-expanded={open}
          aria-label="Search"
          aria-controls="topbar-search-list"
        />
        {loading && <span className="search-spinner" aria-hidden="true" />}
      </div>
      {open && (showRecents || term.length > 0) && (
        <div className="search-dropdown" id="topbar-search-list" role="listbox" aria-label="Search results">
          {error ? (
            <div className="alert alert-error search-alert">{error}</div>
          ) : showRecents ? (
            <>
              <div className="search-group-label">Recent searches</div>
              {recents.map((r, rIdx) => (
                <div key={`${r}-${rIdx}`} className="search-item" onMouseDown={(e) => e.preventDefault()} onClick={() => setQuery(r)}>
                  <span className="search-item-icon">
                    <Icon name="history" />
                  </span>
                  <span className="search-item-text">
                    <strong>{r}</strong>
                  </span>
                </div>
              ))}
            </>
          ) : term.length < 2 ? (
            <div className="search-group-label">Type at least 2 characters…</div>
          ) : !loading && flat.length === 0 ? (
            <div className="search-group-label">No results for “{term}”. Press Enter to search everywhere.</div>
          ) : (
            groups.map((g, gi) => (
              <div key={`${g.type}-${gi}`}>
                <div className="search-group-label">{g.label}</div>
                {g.items.map((item) => {
                  index++;
                  const i = index;
                  return (
                    <div
                      key={`${g.type}-${item.id ?? item.route}-${item.title}`}
                      role="option"
                      aria-selected={selected === i}
                      className={`search-item${selected === i ? ' selected' : ''}`}
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => go(item)}
                    >
                      <span className="search-item-icon">
                        <Icon name={item.icon} />
                      </span>
                      <span className="search-item-text">
                        <strong>
                          <Highlight text={item.title} term={term} />
                        </strong>
                        {item.subtitle && <small>{item.subtitle}</small>}
                      </span>
                    </div>
                  );
                })}
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}

export function Layout({ children }: { children: ReactNode }) {
  const { user, role, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const theme = useTheme();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [avatarMenu, setAvatarMenu] = useState(false);
  const [confirmLogout, setConfirmLogout] = useState(false);
  const [unread, setUnread] = useState(0);

  // Refresh the unread badge on every navigation (MainLayout.OnParametersSetAsync).
  const refreshUnread = useCallback(async () => {
    try {
      const res = await api.get<UnreadCountDto>('/api/notifications/unread-count');
      setUnread(res?.unreadCount ?? 0);
    } catch {
      setUnread(0);
    }
  }, []);

  useEffect(() => {
    void refreshUnread();
    setSidebarOpen(false);
    setAvatarMenu(false);
  }, [location.pathname, refreshUnread]);

  useEffect(() => {
    const onChange = () => void refreshUnread();
    window.addEventListener('unisphere:notifications-changed', onChange);
    return () => window.removeEventListener('unisphere:notifications-changed', onChange);
  }, [refreshUnread]);

  const nav = role ? NAV[role] : [];
  const settingsPath = role === 'Graduate' ? '/graduate/settings' : '/settings';
  const profilePath = role === 'Doctor' ? '/doctor/profile' : role === 'Graduate' ? '/graduate/profile' : '/profile';
  const assistantPath = role === 'Graduate' ? '/graduate/assistant' : role === 'Doctor' ? '/doctor/ai' : role === 'Student' ? '/chat' : null;

  const doLogout = () => {
    setConfirmLogout(false);
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="app-container">
      {sidebarOpen && <div className="sidebar-backdrop" onClick={() => setSidebarOpen(false)} />}
      <aside className={`sidebar${sidebarOpen ? ' open' : ''}`}>
        <div className="sidebar-header">
          <div className="logo">
            <span className="logo-icon">
              <Icon name="diamond" />
            </span>
            <span className="logo-text">
              UniSphere <span className="logo-plus">360+</span>
            </span>
          </div>
          <button className="sidebar-close" onClick={() => setSidebarOpen(false)} aria-label="Close sidebar">
            <Icon name="x" />
          </button>
        </div>

        <nav className="sidebar-nav" aria-label="Main navigation">
          {nav.map((n, nIdx) => (
            <NavLink key={`${n.to}-${nIdx}`} to={n.to} end={n.end} className="nav-item">
              <span className="nav-icon">
                <Icon name={n.icon} />
              </span>
              <span>{n.label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-section sidebar-pinned">
          <div className="sidebar-section-title">Quick Links</div>
          <NavLink to="/profile" className="nav-item" end>
            <span className="nav-icon">
              <Icon name="user-round" />
            </span>
            <span>My Account</span>
          </NavLink>
          <NavLink to="/notifications" className="nav-item">
            <span className="nav-icon">
              <Icon name="bell" />
            </span>
            <span>Notifications</span>
          </NavLink>
          <NavLink to={settingsPath} className="nav-item">
            <span className="nav-icon">
              <Icon name="settings" />
            </span>
            <span>Settings</span>
          </NavLink>
          <NavLink to="/help" className="nav-item">
            <span className="nav-icon">
              <Icon name="circle-help" />
            </span>
            <span>Help &amp; Support</span>
          </NavLink>
          <button className="nav-item logout-btn sidebar-logout" onClick={() => setConfirmLogout(true)}>
            <span className="nav-icon">
              <Icon name="log-out" />
            </span>
            <span>Logout</span>
          </button>
        </div>

        <div className="sidebar-footer">
          {user && (
            <div className="sidebar-user">
              <span className="sidebar-user-avatar">{initials(user.fullName)}</span>
              <span className="sidebar-user-name">
                {user.fullName}
                <small className="sidebar-user-role">{user.role}</small>
              </span>
            </div>
          )}
        </div>
      </aside>

      <div className="main-content">
        <header className="top-bar">
          <button className="menu-toggle" onClick={() => setSidebarOpen((v) => !v)} aria-label="Open menu">
            <Icon name="menu" />
          </button>

          <TopSearch />

          <div className="top-bar-actions">
            <button
              type="button"
              className="icon-btn theme-toggle"
              title={theme.isDark ? 'Switch to light mode' : 'Switch to dark mode'}
              aria-label={theme.isDark ? 'Switch to light mode' : 'Switch to dark mode'}
              onClick={theme.toggle}
            >
              <Icon name={theme.isDark ? 'sun' : 'moon'} />
            </button>
            {assistantPath && (
              <NavLink to={assistantPath} className="icon-btn" title="AI Assistant">
                <Icon name="sparkles" />
              </NavLink>
            )}
            <NavLink to="/notifications" className="icon-btn" title="Notifications">
              <Icon name="bell" />
              {unread > 0 && <span className="badge">{unread > 99 ? '99+' : unread}</span>}
            </NavLink>
            <div className="avatar-wrap">
              <button
                type="button"
                className="user-avatar"
                title="Account"
                aria-label="Account menu"
                aria-expanded={avatarMenu}
                onClick={() => setAvatarMenu((v) => !v)}
              >
                {user?.avatarUrl ? <img src={assetUrl(user.avatarUrl)} alt="Profile" /> : <span className="avatar-initial">{initials(user?.fullName)}</span>}
              </button>
              {avatarMenu && (
                <>
                  <div className="avatar-menu" role="menu" aria-label="Account">
                    <div className="avatar-menu-user">
                      <span className="avatar-initial">{initials(user?.fullName)}</span>
                      <span className="avatar-menu-name">
                        {user?.fullName}
                        <small>{user?.email}</small>
                      </span>
                    </div>
                    <NavLink to={profilePath} className="avatar-menu-item" role="menuitem">
                      <span className="nav-icon">
                        <Icon name="user-round" />
                      </span>
                      <span>My Profile</span>
                    </NavLink>
                    <NavLink to={settingsPath} className="avatar-menu-item" role="menuitem">
                      <span className="nav-icon">
                        <Icon name="settings" />
                      </span>
                      <span>Settings</span>
                    </NavLink>
                    <button
                      type="button"
                      className="avatar-menu-item logout"
                      role="menuitem"
                      onClick={() => {
                        setAvatarMenu(false);
                        setConfirmLogout(true);
                      }}
                    >
                      <span className="nav-icon">
                        <Icon name="log-out" />
                      </span>
                      <span>Logout</span>
                    </button>
                  </div>
                  <div className="search-backdrop" onClick={() => setAvatarMenu(false)} />
                </>
              )}
            </div>
          </div>
        </header>

        <main className="page-content">{children}</main>
      </div>

      {confirmLogout && (
        <Modal
          title="Log out?"
          onClose={() => setConfirmLogout(false)}
          footer={
            <>
              <button className="btn btn-secondary" onClick={() => setConfirmLogout(false)}>
                Cancel
              </button>
              <button className="btn btn-danger" onClick={doLogout}>
                Log out
              </button>
            </>
          }
        >
          <p>
            You are signed in as <strong>{user?.fullName ?? 'this account'}</strong>. Do you want to log out?
          </p>
        </Modal>
      )}
    </div>
  );
}
