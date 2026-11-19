import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { ThemeToggle } from "../components/ThemeToggle";
import { Alert } from "../components/ui";
import { LoginCampusScene } from "./LoginCampusScene";
import "./login.css";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";
const TOKEN_KEY = "unisphere.auth.token";

// React-only convenience key for the saved-email suggestions (never sent
// anywhere, emails only). Same pattern as "unisphere.chat.provider".
const RECENT_EMAILS_KEY = "unisphere.login.recentEmails";
const RECENT_EMAILS_LIMIT = 4;
const SUGGESTION_CLOSE_MS = 160;

interface AuthUser {
  id: number;
  fullName: string;
  email: string;
  roleId: number;
  role: string;
  major: string;
  avatarUrl?: string | null;
}

interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthUser;
}

interface LoginResult {
  ok: boolean;
  user?: AuthUser;
  error?: string;
}

const DemoAccounts: Array<{ role: string; email: string; password: string }> = [
  { role: "Student", email: "mazenmohamed@unisphere.edu", password: "Password123!" },
  { role: "Student (Helwan)", email: "helwan.student@unisphere.edu", password: "Password123!" },
  { role: "Graduate", email: "graduate@unisphere.edu", password: "Graduate123!" },
  { role: "Doctor", email: "doctor@unisphere.edu", password: "Doctor123!" },
  { role: "Admin", email: "admin@unisphere.edu", password: "Admin123!" },
];

function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

function saveToken(token: string): void {
  try {
    localStorage.setItem(TOKEN_KEY, token);
  } catch {
    // Storage unavailable (private mode): keep the token in memory only.
  }
}

function clearToken(): void {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Storage unavailable: nothing to clear.
  }
}

function tokenExpiry(token: string): number | null {
  try {
    const payload = token.split(".")[1];
    const decoded = decodeURIComponent(
      atob(payload)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join(""),
    );
    const data = JSON.parse(decoded) as { exp?: number };
    return typeof data.exp === "number" ? data.exp * 1000 : null;
  } catch {
    return null;
  }
}

function isTokenUsable(token: string | null): boolean {
  if (!token) return false;
  const expiry = tokenExpiry(token);
  return expiry !== null && expiry > Date.now();
}

async function fetchMe(token: string): Promise<AuthUser | null> {
  const response = await fetch(`${API_BASE_URL}/api/auth/me`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) return null;
  return (await response.json()) as AuthUser;
}

async function login(email: string, password: string): Promise<LoginResult> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });

  if (response.ok) {
    const data = (await response.json()) as AuthResponse;
    saveToken(data.token);
    return { ok: true, user: data.user };
  }

  if (response.status === 401) return { ok: false, error: "Invalid email or password." };
  if (response.status === 403)
    return { ok: false, error: "This account has been deactivated. Contact an administrator." };

  let message = "Login failed. Please try again.";
  try {
    const body = (await response.json()) as { error?: string; errors?: Record<string, string[]> };
    if (body?.errors) {
      const first = Object.values(body.errors).flat()[0];
      if (first) message = first;
    } else if (body?.error) {
      message = body.error;
    }
  } catch {
    // Keep the default message.
  }

  return { ok: false, error: message };
}

function isSafeReturnUrl(url: string | null): boolean {
  if (!url) return false;
  if (!url.startsWith("/")) return false;
  if (url.startsWith("//") || url.startsWith("/\\")) return false;
  if (url.includes("\\")) return false;
  if (url.split("").some((c) => c.charCodeAt(0) <= 31)) return false;
  if (url === "/login") return false;
  return true;
}

function homeFor(role: string | null | undefined): string {
  switch ((role ?? "").trim().toLowerCase()) {
    case "student":
      return "/dashboard";
    case "doctor":
      return "/doctor";
    case "graduate":
      return "/graduate";
    case "admin":
      return "/admin";
    default:
      return "/login";
  }
}

function loadRecentEmails(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_EMAILS_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter((value): value is string => typeof value === "string" && value.includes("@"))
      .slice(0, RECENT_EMAILS_LIMIT);
  } catch {
    // Corrupt or unavailable storage: behave as if nothing was saved.
    return [];
  }
}

function saveRecentEmail(email: string): void {
  if (!email) return;
  try {
    const next = [
      email,
      ...loadRecentEmails().filter((saved) => saved.toLowerCase() !== email.toLowerCase()),
    ].slice(0, RECENT_EMAILS_LIMIT);
    localStorage.setItem(RECENT_EMAILS_KEY, JSON.stringify(next));
  } catch {
    // Storage unavailable (private mode): suggestions are best-effort only.
  }
}

export function Login() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnUrl = searchParams.get("returnUrl");
  const expired = searchParams.get("expired");

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // Saved-email suggestions (emails only, stored under the React-only
  // "unisphere.login.recentEmails" key). Two flags give a smooth exit:
  // `open` drives the CSS classes, `suggestMounted` unmounts the list
  // after the fade-out finishes.
  const [recentEmails] = useState<string[]>(loadRecentEmails);
  const [suggestOpen, setSuggestOpen] = useState(false);
  const [suggestMounted, setSuggestMounted] = useState(false);
  const [activeSuggestion, setActiveSuggestion] = useState(-1);
  const closeTimerRef = useRef<number | null>(null);
  const emailInputRef = useRef<HTMLInputElement>(null);

  const typedEmail = email.trim().toLowerCase();
  const visibleEmails = recentEmails.filter((saved) => {
    const lower = saved.toLowerCase();
    return lower !== typedEmail && (typedEmail === "" || lower.includes(typedEmail));
  });

  useEffect(() => {
    return () => {
      if (closeTimerRef.current !== null) window.clearTimeout(closeTimerRef.current);
    };
  }, []);

  function openSuggestions() {
    if (closeTimerRef.current !== null) {
      window.clearTimeout(closeTimerRef.current);
      closeTimerRef.current = null;
    }
    setActiveSuggestion(-1);
    setSuggestMounted(true);
    setSuggestOpen(true);
  }

  function closeSuggestions(immediate = false) {
    setSuggestOpen(false);
    setActiveSuggestion(-1);
    if (closeTimerRef.current !== null) window.clearTimeout(closeTimerRef.current);
    if (immediate) {
      closeTimerRef.current = null;
      setSuggestMounted(false);
    } else {
      closeTimerRef.current = window.setTimeout(() => {
        closeTimerRef.current = null;
        setSuggestMounted(false);
      }, SUGGESTION_CLOSE_MS);
    }
  }

  function selectSuggestion(value: string) {
    setEmail(value);
    closeSuggestions(true);
    // Keep the pointer in the email field so the user can continue to the
    // password input without re-clicking.
    emailInputRef.current?.focus();
  }

  function handleEmailKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (!suggestOpen || visibleEmails.length === 0) return;
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveSuggestion((index) => (index + 1) % visibleEmails.length);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveSuggestion((index) => (index - 1 + visibleEmails.length) % visibleEmails.length);
    } else if (event.key === "Enter") {
      // Enter picks the highlighted suggestion instead of submitting.
      if (activeSuggestion >= 0) {
        event.preventDefault();
        selectSuggestion(visibleEmails[activeSuggestion]);
      }
    } else if (event.key === "Escape") {
      closeSuggestions(true);
    }
  }

  useEffect(() => {
    const token = getToken();
    if (!token) return;
    if (!isTokenUsable(token)) {
      clearToken();
      return;
    }
    // A usable token means the user is signed in: go to the landing page
    // instead of showing the login form again.
    void (async () => {
      const user = await fetchMe(token);
      if (user) navigate(isSafeReturnUrl(returnUrl) ? (returnUrl as string) : homeFor(user.role), { replace: true });
    })();
    // returnUrl comes from the URL (constant while this route is open) and
    // navigate is stable in react-router v7, so this behaves like run-once.
  }, [returnUrl, navigate]);

  useEffect(() => {
    document.title = "Login - UniSphere 360+";
  }, []);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const result = await login(email.trim(), password);
      if (result.ok && result.user) {
        // Success: remember the email for next time (React-only key).
        saveRecentEmail(email.trim());
        const target = isSafeReturnUrl(returnUrl)
          ? (returnUrl as string)
          : result.user.role === "Admin"
            ? "/analytics"
            : "/dashboard";
        navigate(target, { replace: true });
      } else {
        setError(result.error ?? "Login failed. Please try again.");
      }
    } catch {
      setError("Login failed. Please try again.");
    } finally {
      setLoading(false);
    }
  }

  function fillDemo(account: { role: string; email: string; password: string }) {
    setEmail(account.email);
    setPassword(account.password);
    setError(null);
  }

  return (
    <div className="login-shell">
      {/* Brand panel (desktop only; the mobile header sits in the form panel). */}
      <aside className="login-brand" aria-hidden="true">
        <LoginCampusScene />
        <div className="login-brand-scrim" />
        <div className="login-brand-dots" />
        <div className="login-brand-inner">
          <div className="login-brand-logo">
            <UiIcon name="diamond" />
            <span className="login-brand-title">UniSphere <span>360+</span></span>
          </div>
          <div>
            <h2 className="login-brand-heading">Your campus, one smart workspace.</h2>
            <p className="login-brand-sub">
              Grades, planning, career tools and AI assistance — everything academic in a single place.
            </p>
          </div>
          <div className="login-features">
            <div className="login-feature">
              <span className="login-feature-icon"><UiIcon name="sparkles" /></span>
              <span>
                AI Assistant
                <small>Chat, course-grounded answers and study help</small>
              </span>
            </div>
            <div className="login-feature">
              <span className="login-feature-icon"><UiIcon name="graduation-cap" /></span>
              <span>
                Academic Advisor
                <small>Performance insights and course recommendations</small>
              </span>
            </div>
            <div className="login-feature">
              <span className="login-feature-icon"><UiIcon name="briefcase" /></span>
              <span>
                Career tools
                <small>CV studio, job matches and interview coaching</small>
              </span>
            </div>
          </div>
        </div>
        <p className="login-brand-footer">UniSphere 360+ · Student success platform</p>
      </aside>

      {/* Form panel */}
      <main className="login-panel">
        <div className="login-theme-toggle">
          <ThemeToggle />
        </div>

        <div className="login-mobile-brand">
          <span className="text-primary"><UiIcon name="diamond" /></span>
          <span>UniSphere <span className="text-primary">360+</span></span>
        </div>

        <div className="login-card">
          <h1 className="login-heading">Welcome back</h1>
          <p className="login-sub">Sign in to your UniSphere 360+ account</p>

          {expired === "1" && <Alert kind="warning">Session expired. Please sign in again.</Alert>}

          {error && <Alert kind="error">{error}</Alert>}

          <form onSubmit={handleSubmit}>
            <div className="login-field">
              <label htmlFor="email" className="login-label">Email</label>
              <div className="login-input-wrap">
                <span className="login-input-icon"><UiIcon name="mail" /></span>
                <input
                  id="email"
                  ref={emailInputRef}
                  type="email"
                  value={email}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    openSuggestions();
                  }}
                  onFocus={openSuggestions}
                  onBlur={() => closeSuggestions()}
                  onKeyDown={handleEmailKeyDown}
                  placeholder="Enter your email"
                  required
                  autoComplete="email"
                  className="login-input"
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={suggestOpen && visibleEmails.length > 0}
                  aria-controls="login-email-suggestions"
                  aria-activedescendant={
                    suggestOpen && activeSuggestion >= 0 ? `login-email-opt-${activeSuggestion}` : undefined
                  }
                />
                {suggestMounted && visibleEmails.length > 0 && (
                  <ul
                    id="login-email-suggestions"
                    role="listbox"
                    aria-label="Saved emails"
                    className={suggestOpen ? "login-suggestions" : "login-suggestions closing"}
                  >
                    {visibleEmails.map((saved, index) => (
                      <li key={saved}>
                        <button
                          type="button"
                          role="option"
                          id={`login-email-opt-${index}`}
                          aria-selected={activeSuggestion === index}
                          className={
                            activeSuggestion === index
                              ? "login-suggestion is-active"
                              : "login-suggestion"
                          }
                          onMouseEnter={() => setActiveSuggestion(index)}
                          // Keep focus in the input so the list never
                          // unmounts between mousedown and click.
                          onMouseDown={(e) => e.preventDefault()}
                          onClick={() => selectSuggestion(saved)}
                        >
                          <span className="login-suggestion-icon"><UiIcon name="mail" /></span>
                          <span className="login-suggestion-label">{saved}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
            <div className="login-field">
              <label htmlFor="password" className="login-label">Password</label>
              <div className="login-input-wrap">
                <span className="login-input-icon"><UiIcon name="lock" /></span>
                <input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter your password"
                  required
                  autoComplete="current-password"
                  className="login-input has-eye"
                />
                <button
                  type="button"
                  className="login-eye"
                  onClick={() => setShowPassword((visible) => !visible)}
                  aria-label={showPassword ? "Hide password" : "Show password"}
                  title={showPassword ? "Hide password" : "Show password"}
                >
                  <UiIcon name={showPassword ? "eye-off" : "eye"} />
                </button>
              </div>
            </div>
            <button type="submit" className="login-submit" disabled={loading}>
              {loading ? (
                <>
                  <span
                    className="h-4 w-4 animate-search-spin rounded-full border-2 border-white/40 border-t-white"
                    aria-hidden="true"
                  />
                  Signing in...
                </>
              ) : (
                "Sign In"
              )}
            </button>
          </form>

          <p className="login-register">
            Don&apos;t have an account? <Link to="/register">Register</Link>
          </p>

          <div className="login-demo">
            <p className="login-demo-title">Demo accounts — click to fill, then Sign In</p>
            {DemoAccounts.map((account) => (
              <button
                key={account.email}
                type="button"
                onClick={() => fillDemo(account)}
                className="login-demo-item"
              >
                <strong className="login-demo-role">{account.role}</strong>
                <span className="login-demo-email">{account.email}</span>
                <span className="login-demo-pass">{account.password}</span>
              </button>
            ))}
          </div>
        </div>
      </main>
    </div>
  );
}
