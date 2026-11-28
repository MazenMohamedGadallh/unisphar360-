import { Fragment, useCallback, useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { BrandLogo } from "./BrandLogo";
import { LiveCampusBackground } from "./LiveCampusBackground";
import { UserAvatar } from "./UserAvatar";
import { aiRouteFor, navFor, settingsFor } from "./nav";
import { ThemeToggle } from "./ThemeToggle";
import { TopbarSearch } from "./TopbarSearch";
import { ConfirmDialog } from "./ui";
import {
  API_BASE_URL,
  authHeaders,
  clearToken,
  getToken,
  onNotificationsUpdated,
  onUserUpdated,
  type AuthUser,
} from "../lib/api";

const SIDEBAR_COLLAPSED_KEY = "unisphere.sidebar.collapsed";
const QUICK_LINKS_OPEN_KEY = "unisphere.quicklinks.open";

const NAV_ITEM =
  "group mb-1 flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-2.5 text-start text-sm font-medium text-text-secondary no-underline transition-all duration-150 hover:bg-primary-light/60 hover:text-text hover:translate-x-0.5";
const NAV_ITEM_ACTIVE =
  "bg-primary-light! text-primary! font-semibold shadow-xs [&_svg]:text-primary!";
const NAV_ICON =
  "flex w-6 flex-none items-center justify-center text-lg text-text-muted transition-colors group-hover:text-primary";
const NAV_ITEM_LOGOUT =
  "group mt-1 flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-2.5 text-start text-sm font-medium text-danger transition-all duration-150 hover:bg-danger-light hover:text-danger [&_svg]:text-danger!";
const ICON_BUTTON =
  "relative inline-flex min-h-10 min-w-10 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent p-2 text-lg text-text-secondary transition-all hover:bg-primary-light/70 hover:text-primary";
const AVATAR_MENU_ITEM =
  "flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-2.5 text-start text-sm font-medium text-text-secondary no-underline transition-colors hover:bg-primary-light/60 hover:text-text";
const AVATAR_MENU_LOGOUT =
  "flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-2.5 text-start text-sm font-medium text-danger transition-colors hover:bg-danger-light hover:text-danger";

function readBool(key: string, fallback: boolean): boolean {
  try {
    const raw = localStorage.getItem(key);
    if (raw === "true") return true;
    if (raw === "false") return false;
  } catch {
    // ignore
  }
  return fallback;
}

export function AppLayout() {
  const location = useLocation();
  const navigate = useNavigate();

  const [authReady, setAuthReady] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [unreadCount, setUnreadCount] = useState(0);
  const [sidebarOpen, setSidebarOpen] = useState(false); // mobile drawer
  const [collapsed, setCollapsed] = useState<boolean>(() => readBool(SIDEBAR_COLLAPSED_KEY, false));
  const [quickLinksOpen, setQuickLinksOpen] = useState<boolean>(() => readBool(QUICK_LINKS_OPEN_KEY, true));
  const [avatarMenuOpen, setAvatarMenuOpen] = useState(false);
  const [showLogoutModal, setShowLogoutModal] = useState(false);

  const hamburgerRef = useRef<HTMLButtonElement | null>(null);
  const closeSidebarRef = useRef<HTMLButtonElement | null>(null);
  const wasSidebarOpen = useRef(false);

  const fetchUnread = useCallback(async (signal?: AbortSignal) => {
    try {
      const unreadResponse = await fetch(`${API_BASE_URL}/api/notifications/unread-count`, {
        headers: authHeaders(),
        signal,
      });
      if (unreadResponse.ok) {
        const data = (await unreadResponse.json()) as { unreadCount: number };
        setUnreadCount(data?.unreadCount ?? 0);
      }
    } catch {
      // ignore abort/offline
    }
  }, []);

  useEffect(() => {
    const unsubUser = onUserUpdated((updated) => setUser(updated));
    const unsubNotif = onNotificationsUpdated(() => void fetchUnread());
    return () => {
      unsubUser();
      unsubNotif();
    };
  }, [fetchUnread]);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    void (async () => {
      if (!getToken()) {
        navigate("/login", { replace: true });
        return;
      }

      try {
        const meResponse = await fetch(`${API_BASE_URL}/api/auth/me`, {
          headers: authHeaders(),
          signal: controller.signal,
        });
        if (cancelled) return;
        if (meResponse.status === 401) {
          clearToken();
          navigate("/login?expired=1", { replace: true });
          return;
        }
        if (meResponse.ok) setUser((await meResponse.json()) as AuthUser);
      } catch (thrown) {
        if ((thrown as Error).name === "AbortError") return;
      } finally {
        if (!cancelled) setAuthReady(true);
      }

      if (!cancelled) await fetchUnread(controller.signal);
    })();

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [location.pathname, navigate, fetchUnread]);

  useEffect(() => {
    if (wasSidebarOpen.current === sidebarOpen) return;
    wasSidebarOpen.current = sidebarOpen;
    if (sidebarOpen) closeSidebarRef.current?.focus();
    else hamburgerRef.current?.focus();
  }, [sidebarOpen]);

  function toggleCollapsed() {
    setCollapsed((prev) => {
      const next = !prev;
      try {
        localStorage.setItem(SIDEBAR_COLLAPSED_KEY, String(next));
      } catch {
        // ignore
      }
      return next;
    });
  }

  function toggleQuickLinks() {
    setQuickLinksOpen((prev) => {
      const next = !prev;
      try {
        localStorage.setItem(QUICK_LINKS_OPEN_KEY, String(next));
      } catch {
        // ignore
      }
      return next;
    });
  }

  function navLinkClass({ isActive }: { isActive: boolean }) {
    return isActive ? `${NAV_ITEM} ${NAV_ITEM_ACTIVE}` : NAV_ITEM;
  }

  if (!authReady) return null;

  const navItems = navFor(user?.role);

  return (
    <div className="relative flex min-h-screen text-text">
      {/* Living Lavender University Backdrop */}
      <LiveCampusBackground variant="workspace" />

      {/* Mobile Drawer Overlay */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-99 bg-black/45 backdrop-blur-xs lg:hidden"
          onClick={() => setSidebarOpen(false)}
          aria-hidden="true"
        />
      )}

      {/* Collapsible Left Sidebar */}
      <aside
        className={`fixed inset-y-0 left-0 z-100 flex flex-col border-r border-border/80 bg-surface/82 backdrop-blur-xl transition-all duration-250 ease-out lg:translate-x-0 ${
          sidebarOpen ? "translate-x-0 shadow-card-lg" : "-translate-x-full"
        } ${collapsed ? "lg:w-0 lg:overflow-hidden lg:border-r-0" : "w-[var(--width-sidebar)]"}`}
        aria-label="Primary navigation"
      >
        {/* Brand Header */}
        <div className="flex h-[var(--height-topbar)] shrink-0 items-center justify-between border-b border-border px-4">
          <NavLink
            to="/"
            className="flex items-center gap-2.5 no-underline"
            onClick={() => setSidebarOpen(false)}
          >
            <BrandLogo size={34} />
            <span className="whitespace-nowrap text-[17px] font-bold tracking-tight text-text">
              UniSphere <span className="text-primary">360+</span>
            </span>
          </NavLink>
          <button
            ref={closeSidebarRef}
            type="button"
            className="inline-flex min-h-10 min-w-10 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent text-lg text-text-secondary hover:bg-primary-light/60 hover:text-text lg:hidden"
            onClick={() => setSidebarOpen(false)}
            aria-label="Close navigation menu"
          >
            <UiIcon name="x" />
          </button>
        </div>

        {/* Role Navigation Links */}
        <nav className="flex flex-1 flex-col overflow-y-auto px-3 py-4">
          <div className="px-3 pb-2 text-[11px] font-semibold tracking-wider text-text-muted uppercase">
            {user?.role ? `${user.role} Workspace` : "Workspace"}
          </div>
          {navItems.map((item) => (
            <Fragment key={item.to}>
            {item.group && <div className="px-3 pt-3 pb-1 text-xs font-semibold text-text-muted">{item.group}</div>}
            <NavLink
              to={item.to}
              end={item.end}
              className={navLinkClass}
              onClick={() => setSidebarOpen(false)}
            >
              <span className={NAV_ICON}>
                <UiIcon name={item.icon} />
              </span>
              <span className="truncate whitespace-nowrap">{item.label}</span>
            </NavLink>
            </Fragment>
          ))}

          {/* Collapsible QUICK LINKS Section */}
          <div className="mt-6 border-t border-border pt-3">
            <button
              type="button"
              onClick={toggleQuickLinks}
              aria-expanded={quickLinksOpen}
              className="mb-1 flex w-full cursor-pointer items-center justify-between rounded-control border-0 bg-transparent px-3 py-2 text-[11px] font-semibold tracking-wider text-text-muted uppercase transition-colors hover:bg-primary-light/50 hover:text-text"
            >
              <span>Quick Links</span>
              <span
                className="inline-flex text-sm transition-transform duration-200"
                style={{ transform: quickLinksOpen ? "rotate(0deg)" : "rotate(-90deg)" }}
              >
                <UiIcon name="chevron-down" />
              </span>
            </button>

            {quickLinksOpen && (
              <div className="flex flex-col">
                <NavLink to="/profile" className={navLinkClass} onClick={() => setSidebarOpen(false)}>
                  <span className={NAV_ICON}><UiIcon name="user-round" /></span>
                  <span>My Profile</span>
                </NavLink>
                <NavLink to="/notifications" className={navLinkClass} onClick={() => setSidebarOpen(false)}>
                  <span className={NAV_ICON}><UiIcon name="bell" /></span>
                  <span className="flex-1">Notifications</span>
                  {unreadCount > 0 && (
                    <span className="rounded-full bg-primary px-1.5 py-0.5 text-[11px] font-semibold text-white tabular-nums">
                      {unreadCount}
                    </span>
                  )}
                </NavLink>
                <NavLink to={settingsFor(user?.role)} className={navLinkClass} onClick={() => setSidebarOpen(false)}>
                  <span className={NAV_ICON}><UiIcon name="settings" /></span>
                  <span>Settings</span>
                </NavLink>
                <NavLink to="/help" className={navLinkClass} onClick={() => setSidebarOpen(false)}>
                  <span className={NAV_ICON}><UiIcon name="circle-help" /></span>
                  <span>Help &amp; Support</span>
                </NavLink>
                <button
                  type="button"
                  className={NAV_ITEM_LOGOUT}
                  onClick={() => {
                    setSidebarOpen(false);
                    setShowLogoutModal(true);
                  }}
                >
                  <span className={NAV_ICON}><UiIcon name="log-out" /></span>
                  <span>Logout</span>
                </button>
              </div>
            )}
          </div>
        </nav>

        {/* Bottom Circular Avatar User Chip */}
        {user && (
          <NavLink
            to="/profile"
            onClick={() => setSidebarOpen(false)}
            className="flex items-center gap-3 border-t border-border px-4 py-3.5 no-underline transition-colors hover:bg-primary-light/40"
          >
            <UserAvatar fullName={user.fullName} avatarUrl={user.avatarUrl} size={38} />
            <div className="min-w-0 flex-1">
              <div className="truncate text-[13.5px] font-semibold text-text">{user.fullName}</div>
              <div className="truncate text-xs text-text-secondary">
                {user.role}{user.universityName ? ` · ${user.universityName}` : ""}
              </div>
            </div>
          </NavLink>
        )}
      </aside>

      {/* Main Content Area (Expands smoothly when sidebar collapses) */}
      <div
        className={`relative z-10 flex min-w-0 flex-1 flex-col transition-all duration-250 ease-out ${
          collapsed ? "lg:ml-0" : "lg:ml-[var(--width-sidebar)]"
        }`}
      >
        {/* Topbar */}
        <header className="sticky top-0 z-50 flex h-[var(--height-topbar)] items-center justify-between gap-3 border-b border-border/80 bg-surface/78 px-4 backdrop-blur-xl lg:px-6">
          <div className="flex min-w-0 flex-1 items-center gap-2.5">
            {/* Mobile Drawer Trigger */}
            <button
              ref={hamburgerRef}
              type="button"
              className={`${ICON_BUTTON} lg:hidden`}
              onClick={() => setSidebarOpen(true)}
              aria-label="Open navigation menu"
            >
              <UiIcon name="menu" />
            </button>

            {/* Desktop Collapsible Sidebar Toggle */}
            <button
              type="button"
              className={`${ICON_BUTTON} hidden lg:inline-flex`}
              onClick={toggleCollapsed}
              title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
              aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
            >
              <UiIcon name={collapsed ? "panel-left-open" : "panel-left-close"} />
            </button>

            {collapsed && (
              <NavLink to="/" className="hidden items-center gap-2 no-underline lg:flex">
                <BrandLogo size={28} />
                <span className="whitespace-nowrap text-sm font-bold text-text">
                  UniSphere <span className="text-primary">360+</span>
                </span>
              </NavLink>
            )}

            <TopbarSearch />
          </div>

          <div className="flex shrink-0 items-center gap-1.5">
            <ThemeToggle className={ICON_BUTTON} />

            <NavLink
              to={aiRouteFor(user?.role)}
              className={ICON_BUTTON}
              title="AI Assistant"
              aria-label="AI Assistant"
            >
              <UiIcon name="sparkles" />
            </NavLink>

            <NavLink
              to="/notifications"
              className={ICON_BUTTON}
              title="Notifications"
              aria-label="Notifications"
            >
              <UiIcon name="bell" />
              {unreadCount > 0 && (
                <span className="absolute top-1.5 right-1.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-bold text-white tabular-nums">
                  {unreadCount > 9 ? "9+" : unreadCount}
                </span>
              )}
            </NavLink>

            {/* Topbar Circular Avatar Dropdown */}
            <div className="relative ml-1">
              <button
                type="button"
                className="flex cursor-pointer items-center gap-2 rounded-full border border-border bg-card p-1 pr-2.5 transition-all hover:border-primary/50 hover:shadow-xs"
                onClick={() => setAvatarMenuOpen((open) => !open)}
                aria-label="Account menu"
                aria-expanded={avatarMenuOpen}
              >
                <UserAvatar fullName={user?.fullName} avatarUrl={user?.avatarUrl} size={32} />
                <span className="hidden max-w-[120px] truncate text-xs font-semibold text-text sm:inline">
                  {user?.firstName || user?.fullName?.split(" ")[0] || "Account"}
                </span>
              </button>

              {avatarMenuOpen && (
                <>
                  <div
                    className="fixed inset-0 z-40"
                    onClick={() => setAvatarMenuOpen(false)}
                    aria-hidden="true"
                  />
                  <div className="absolute right-0 top-full z-50 mt-2 w-60 rounded-card border border-border bg-surface p-2 shadow-card-lg">
                    {user && (
                      <div className="mb-1 flex items-center gap-3 border-b border-border px-3 py-2.5">
                        <UserAvatar fullName={user.fullName} avatarUrl={user.avatarUrl} size={36} />
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-semibold text-text">{user.fullName}</div>
                          <div className="truncate text-xs text-text-secondary">{user.email}</div>
                        </div>
                      </div>
                    )}
                    <NavLink to="/profile" className={AVATAR_MENU_ITEM} onClick={() => setAvatarMenuOpen(false)}>
                      <UiIcon name="user-round" /> My Profile
                    </NavLink>
                    <NavLink to={settingsFor(user?.role)} className={AVATAR_MENU_ITEM} onClick={() => setAvatarMenuOpen(false)}>
                      <UiIcon name="settings" /> Settings
                    </NavLink>
                    <NavLink to="/help" className={AVATAR_MENU_ITEM} onClick={() => setAvatarMenuOpen(false)}>
                      <UiIcon name="circle-help" /> Help &amp; Support
                    </NavLink>
                    <div className="my-1 border-t border-border" />
                    <button
                      type="button"
                      className={AVATAR_MENU_LOGOUT}
                      onClick={() => {
                        setAvatarMenuOpen(false);
                        setShowLogoutModal(true);
                      }}
                    >
                      <UiIcon name="log-out" /> Logout
                    </button>
                  </div>
                </>
              )}
            </div>
          </div>
        </header>

        {/* Main Workspace Viewport */}
        <main className="mx-auto w-full max-w-[1380px] flex-1 p-4 sm:p-6 lg:p-7">
          <Outlet />
        </main>
      </div>

      {/* Logout Confirmation Modal */}
      <ConfirmDialog
        open={showLogoutModal}
        title="Sign out of UniSphere 360+"
        confirmLabel="Sign Out"
        danger
        onCancel={() => setShowLogoutModal(false)}
        onConfirm={() => {
          setShowLogoutModal(false);
          clearToken();
          navigate("/login", { replace: true });
        }}
      >
        Are you sure you want to end your current session?
      </ConfirmDialog>
    </div>
  );
}
