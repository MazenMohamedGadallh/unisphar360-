import { useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { navFor, settingsFor } from "./nav";
import { ThemeToggle } from "./ThemeToggle";
import { TopbarSearch } from "./TopbarSearch";
import { ConfirmDialog } from "./ui";
import { API_BASE_URL, authHeaders, clearToken, getToken, type AuthUser } from "../lib/api";

/*
 * App shell for every protected page — Sidebar + Topbar, mirroring the
 * Blazor MainLayout.razor:
 * - 260px sidebar: brand, role-based nav, quick links, user chip; a drawer
 *   with overlay below 992px.
 * - 64px sticky topbar: hamburger, live search, theme toggle, AI shortcut,
 *   notification bell with unread badge, avatar menu.
 * - Auth gate: no/invalid token bounces to /login (expired tokens get the
 *   "Session expired" message via ?expired=1).
 */

const NAV_ITEM =
  "group mb-1 flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-3 text-start text-sm font-medium text-text-secondary no-underline transition-all duration-200 hover:bg-bg hover:text-text";
const NAV_ITEM_ACTIVE = "bg-primary-light! text-primary! [&_svg]:text-secondary!";
const NAV_ICON =
  "flex w-6 flex-none items-center justify-center text-lg text-text-muted transition-colors group-hover:text-text-secondary";
const NAV_ITEM_LOGOUT =
  "group mt-auto mb-1 flex w-full cursor-pointer items-center gap-3 rounded-control border-solid border-t border-x-0 border-b-0 border-border bg-transparent px-3 py-3 text-start text-sm font-medium text-danger transition-all duration-200 hover:bg-danger-light hover:text-danger [&_svg]:text-danger!";
const ICON_BUTTON =
  "relative inline-flex min-h-11 min-w-11 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent p-2.5 text-xl text-text-secondary transition-colors hover:bg-bg hover:text-text";
const AVATAR_MENU_ITEM =
  "flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-2.5 text-start text-sm font-medium text-text-secondary no-underline transition-colors hover:bg-bg hover:text-text";
const AVATAR_MENU_LOGOUT =
  "flex w-full cursor-pointer items-center gap-3 rounded-control border-0 bg-transparent px-3 py-2.5 text-start text-sm font-medium text-danger transition-colors hover:bg-danger-light hover:text-danger";

export function AppLayout() {
  const location = useLocation();
  const navigate = useNavigate();
  const [authReady, setAuthReady] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [unreadCount, setUnreadCount] = useState(0);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [avatarMenuOpen, setAvatarMenuOpen] = useState(false);
  const [showLogoutModal, setShowLogoutModal] = useState(false);

  const hamburgerRef = useRef<HTMLButtonElement | null>(null);
  const closeSidebarRef = useRef<HTMLButtonElement | null>(null);
  const wasSidebarOpen = useRef(false);

  // Identity + unread badge: fetched on mount and refreshed on every
  // navigation (mirrors the Blazor layout's OnParametersSetAsync). The
  // layout route keeps this component mounted, so navigating between
  // pages doesn't remount the shell.
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
        // Backend unreachable: keep the shell usable with a generic menu.
      } finally {
        if (!cancelled) setAuthReady(true);
      }
      if (cancelled) return;
      try {
        const unreadResponse = await fetch(`${API_BASE_URL}/api/notifications/unread-count`, {
          headers: authHeaders(),
          signal: controller.signal,
        });
        const data = unreadResponse.ok
          ? ((await unreadResponse.json()) as { unreadCount: number })
          : null;
        if (!cancelled) setUnreadCount(data?.unreadCount ?? 0);
      } catch {
        if (!cancelled) setUnreadCount(0);
      }
    })();
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [location.pathname, navigate]);

  // Drawer focus management: opening focuses the close button, closing
  // returns focus to the hamburger (same as MainLayout.razor).
  useEffect(() => {
    if (wasSidebarOpen.current === sidebarOpen) return;
    wasSidebarOpen.current = sidebarOpen;
    if (sidebarOpen) closeSidebarRef.current?.focus();
    else hamburgerRef.current?.focus();
  }, [sidebarOpen]);

  function navLinkClass({ isActive }: { isActive: boolean }) {
    return isActive ? `${NAV_ITEM} ${NAV_ITEM_ACTIVE}` : NAV_ITEM;
  }

  // Session is still being validated: render nothing (no protected content
  // flash) until the check settles — mirrors the Blazor auth gate.
  if (!authReady) return null;

  const initial = user?.fullName ? user.fullName[0].toUpperCase() : "?";
  const navItems = navFor(user?.role);

  return (
    <div className="flex min-h-screen">
      {/* Sidebar drawer */}
      <aside
        className={`${sidebarOpen ? "translate-x-0" : "-translate-x-full"} fixed inset-y-0 left-0 z-50 w-sidebar flex flex-col overflow-hidden bg-surface transition-transform duration-300 border-e border-border lg:translate-x-0`}
        onKeyDown={(event) => {
          if (event.key === "Escape") setSidebarOpen(false);
        }}
      >
        <div className="flex shrink-0 items-center justify-between border-b border-border p-5">
          <div className="flex items-center gap-2.5">
            <span className="inline-flex items-center justify-center text-2xl text-primary"><UiIcon name="diamond" /></span>
            <span className="text-lg font-bold text-text">UniSphere <span className="text-primary">360+</span></span>
          </div>
          <button
            ref={closeSidebarRef}
            type="button"
            className="hidden max-lg:inline-flex min-h-11 min-w-11 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent p-2.5 text-2xl text-text-secondary transition-colors hover:text-text"
            onClick={() => setSidebarOpen(false)}
            aria-label="Close sidebar"
          >
            <UiIcon name="x" />
          </button>
        </div>

        <nav className="min-h-0 flex-1 overflow-y-auto px-3 py-4" aria-label="Main navigation">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              onClick={() => setSidebarOpen(false)}
              className={navLinkClass}
            >
              <span className={NAV_ICON}><UiIcon name={item.icon} /></span>
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="mt-auto shrink-0 border-t border-border px-3 py-4">
          <div className="mb-2 px-2 text-[11px] font-semibold uppercase text-text-secondary">Quick Links</div>
          <NavLink to="/profile" onClick={() => setSidebarOpen(false)} className={navLinkClass}>
            <span className={NAV_ICON}><UiIcon name="user-round" /></span>
            <span>My Profile</span>
          </NavLink>
          <NavLink to="/notifications" onClick={() => setSidebarOpen(false)} className={navLinkClass}>
            <span className={NAV_ICON}><UiIcon name="bell" /></span>
            <span>Notifications</span>
          </NavLink>
          <NavLink to={settingsFor(user?.role)} onClick={() => setSidebarOpen(false)} className={navLinkClass}>
            <span className={NAV_ICON}><UiIcon name="settings" /></span>
            <span>Settings</span>
          </NavLink>
          <NavLink to="/help" onClick={() => setSidebarOpen(false)} className={navLinkClass}>
            <span className={NAV_ICON}><UiIcon name="circle-help" /></span>
            <span>Help &amp; Support</span>
          </NavLink>
          <button
            type="button"
            className={NAV_ITEM_LOGOUT}
            onClick={() => setShowLogoutModal(true)}
            title="Log out"
            aria-label="Log out"
          >
            <span className={NAV_ICON}><UiIcon name="log-out" /></span>
            <span>Logout</span>
          </button>
        </div>

        <div className="shrink-0 border-t border-border p-3">
          {user?.fullName ? (
            <div className="flex items-center gap-2.5 px-2 pb-3 pt-1">
              <span className="flex h-8 w-8 flex-none items-center justify-center rounded-full bg-primary text-sm font-bold text-white">{initial}</span>
              <span className="truncate text-[13px] font-semibold text-text">{user.fullName}</span>
            </div>
          ) : null}
        </div>
      </aside>
      {sidebarOpen && (
        <div className="fixed inset-0 z-40 bg-black/50" onClick={() => setSidebarOpen(false)} aria-hidden="true" />
      )}

      {/* Main content */}
      <div className="flex min-h-screen flex-1 flex-col lg:ps-[260px]">
        {/* Top bar */}
        <header className="sticky top-0 z-30 flex h-topbar items-center gap-2 border-b border-border bg-surface px-3 sm:gap-4 sm:px-6">
          <button
            ref={hamburgerRef}
            type="button"
            className="inline-flex min-h-11 min-w-11 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent p-2.5 text-2xl text-text-secondary transition-colors hover:bg-bg hover:text-text lg:hidden"
            onClick={() => setSidebarOpen(true)}
            aria-label="Open navigation"
          >
            <UiIcon name="menu" />
          </button>

          <TopbarSearch />

          <div className="ms-auto flex items-center gap-4">
            <ThemeToggle />
            <NavLink to="/chat" className={ICON_BUTTON} title="AI Assistant">
              <UiIcon name="sparkles" />
            </NavLink>
            <NavLink to="/notifications" className={ICON_BUTTON} title="Notifications">
              <UiIcon name="bell" />
              {unreadCount > 0 && (
                <span className="absolute -top-2 -end-2 flex h-[18px] w-[18px] items-center justify-center rounded-full bg-danger text-[10px] font-semibold text-white">
                  {unreadCount}
                </span>
              )}
            </NavLink>
            <div className="relative">
              <button
                type="button"
                className="flex h-9 w-9 cursor-pointer items-center justify-center overflow-hidden rounded-full border-0 bg-primary p-0 text-[15px] font-semibold text-white"
                title="Account"
                aria-label="Account menu"
                aria-expanded={avatarMenuOpen}
                onClick={() => setAvatarMenuOpen((open) => !open)}
              >
                {user?.avatarUrl ? (
                  <img src={user.avatarUrl} alt="Profile" className="h-full w-full object-cover" />
                ) : (
                  <span>{initial}</span>
                )}
              </button>
              {avatarMenuOpen && (
                <>
                  <div className="fixed inset-0 z-40 bg-transparent" onClick={() => setAvatarMenuOpen(false)} aria-hidden="true" />
                  <div
                    className="absolute right-0 top-[calc(100%+10px)] z-50 min-w-[210px] rounded-card bg-surface border border-border p-2 shadow-card-lg"
                    role="menu"
                    aria-label="Account"
                  >
                    <div className="mb-1.5 flex items-center gap-2.5 border-b border-border px-3 py-2 text-sm font-semibold text-text">
                      <span className="flex h-[30px] w-[30px] flex-none items-center justify-center rounded-full bg-primary text-[13px] text-white">{initial}</span>
                      <span>{user?.fullName ?? "Account"}</span>
                    </div>
                    <NavLink
                      to="/profile"
                      role="menuitem"
                      onClick={() => setAvatarMenuOpen(false)}
                      className={AVATAR_MENU_ITEM}
                    >
                      <span className="flex w-5 flex-none items-center justify-center text-base"><UiIcon name="user-round" /></span>
                      <span>My Profile</span>
                    </NavLink>
                    <button
                      type="button"
                      role="menuitem"
                      className={AVATAR_MENU_LOGOUT}
                      onClick={() => {
                        setAvatarMenuOpen(false);
                        setShowLogoutModal(true);
                      }}
                    >
                      <span className="flex w-5 flex-none items-center justify-center text-base"><UiIcon name="log-out" /></span>
                      <span>Logout</span>
                    </button>
                  </div>
                </>
              )}
            </div>
          </div>
        </header>

        <main className="flex-1">
          <Outlet />
        </main>
      </div>

      <ConfirmDialog
        open={showLogoutModal}
        title="You're still signed in"
        confirmLabel="Log out"
        danger
        onConfirm={() => {
          setShowLogoutModal(false);
          clearToken();
          navigate("/login", { replace: true });
        }}
        onCancel={() => setShowLogoutModal(false)}
      >
        You are already logged in as <strong>{user?.fullName ?? "this account"}</strong>. Do you want to log out?
      </ConfirmDialog>
    </div>
  );
}
