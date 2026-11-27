import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { BrandLogo } from "../components/BrandLogo";
import { LiveCampusBackground } from "../components/LiveCampusBackground";
import { ThemeToggle } from "../components/ThemeToggle";
import { Alert } from "../components/ui";
import { API_BASE_URL, clearToken, getToken, saveToken, type AuthUser } from "../lib/api";
import "./login.css";

interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthUser;
}

const DEMO_ACCOUNTS: Array<{ role: string; email: string; password: string }> = [
  { role: "Student", email: "mazenmohamed@unisphere.edu", password: "Password123!" },
  { role: "Graduate", email: "graduate@unisphere.edu", password: "Graduate123!" },
  { role: "Doctor", email: "doctor@unisphere.edu", password: "Doctor123!" },
  { role: "Admin", email: "admin@unisphere.edu", password: "Admin123!" },
];

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
      return "/dashboard";
  }
}

export function Login() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [portalUser, setPortalUser] = useState<AuthUser | null>(null);
  const [error, setError] = useState<string | null>(null);

  const expired = searchParams.get("expired") === "1";

  useEffect(() => {
    document.title = "UniSphere 360+ — Sign In";
    const token = getToken();
    if (!token) return;
    let active = true;
    void (async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/api/auth/me`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!active) return;
        if (res.ok) {
          const me = (await res.json()) as AuthUser;
          navigate(homeFor(me.role), { replace: true });
        } else {
          clearToken();
        }
      } catch {
        // Stay on login if unreachable
      }
    })();
    return () => {
      active = false;
    };
  }, [navigate]);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting || portalUser) return;
    setError(null);
    if (!email.trim() || !password) {
      setError("Please enter both your email address and password.");
      return;
    }

    setSubmitting(true);
    try {
      const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email.trim(), password }),
      });

      if (!response.ok) {
        if (response.status === 401) {
          setError("Invalid email or password.");
        } else if (response.status === 403) {
          setError("This account has been deactivated. Contact an administrator.");
        } else {
          const body = (await response.json().catch(() => ({}))) as { error?: string };
          setError(body.error || "Sign in failed. Please try again.");
        }
        setSubmitting(false);
        return;
      }

      const data = (await response.json()) as AuthResponse;
      saveToken(data.token);
      setPortalUser(data.user);

      // Fast GPU-accelerated (~600ms transform + opacity) portal transition
      window.setTimeout(() => {
        navigate(homeFor(data.user.role), { replace: true });
      }, 580);
    } catch {
      setError("Unable to reach the server. Please try again.");
      setSubmitting(false);
    }
  }

  function quickFill(demo: (typeof DEMO_ACCOUNTS)[number]) {
    setEmail(demo.email);
    setPassword(demo.password);
    setError(null);
  }

  return (
    <div className="relative flex min-h-screen w-full flex-col overflow-hidden bg-[#160d2e] text-[#F5F3FF]">
      {/* 1. Full-Screen Prestigious University Campus Quad Background */}
      <LiveCampusBackground variant="hero" portalActive={Boolean(portalUser)} />

      {/* Top Bar: Spinning Top-Left Brand Lockup + Right Utility Controls */}
      <header className="relative z-10 mx-auto flex w-full max-w-[1380px] items-center justify-between gap-4 px-6 pt-6">
        <div className="login-stagger login-stagger-1 flex items-center gap-3">
          <div className="login-sphere-spin rounded-full">
            <BrandLogo size={40} className="ring-2 ring-[#C4B5FD]/50 shadow-[0_0_20px_rgba(124,58,237,0.45)]" />
          </div>
          <span
            className="whitespace-nowrap text-xl font-bold tracking-tight text-white"
            style={{ textShadow: "0 2px 14px rgba(11, 13, 30, 0.65)" }}
          >
            UniSphere <span className="login-brand-accent">360+</span>
          </span>
        </div>

        <div className="flex items-center gap-3">
          <ThemeToggle className="inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full border border-white/20 bg-white/10 text-white backdrop-blur-md transition-colors hover:bg-white/20" />
          <Link
            to="/register"
            className="whitespace-nowrap rounded-full border border-white/22 bg-white/10 px-4 py-2 text-xs font-semibold text-white no-underline backdrop-blur-md transition-colors hover:bg-white/20"
          >
            Create Account
          </Link>
        </div>
      </header>

      {/* Main Seamless Campus-Bento Stage */}
      <main
        className="relative z-10 mx-auto grid w-full max-w-[1340px] flex-1 grid-cols-1 items-center gap-10 px-6 pb-10 pt-2 lg:grid-cols-12 lg:gap-8"
        style={{
          opacity: portalUser ? 0 : 1,
          transform: portalUser ? "scale(0.96) translateY(-10px)" : "scale(1) translateY(0)",
          transition: "opacity 460ms cubic-bezier(0.16, 1, 0.3, 1), transform 560ms cubic-bezier(0.16, 1, 0.3, 1)",
        }}
      >
        {/* Left Side: Open Editorial Hero & 3 Translucent Frosted-Glass Feature Pills */}
        <section
          className="flex flex-col justify-center py-4 lg:col-span-7 lg:py-8 lg:pr-10"
          aria-label="UniSphere 360+ overview"
        >
          <div className="max-w-[540px]">
            <h1 className="login-stagger login-stagger-2 login-editorial-headline m-0 text-4xl font-bold leading-[1.1] tracking-tight text-white sm:text-5xl lg:text-[54px]">
              Your whole university life, in one place.
            </h1>

            <p className="login-stagger login-stagger-3 login-editorial-sub mt-4 mb-0 max-w-[490px] text-base font-medium leading-relaxed text-[#F1F3FF]/92 sm:text-[17px]">
              Courses, grades, exams and advice for students, doctors and graduates.
            </p>

            {/* 3 Compact Frosted-Glass Feature Pills */}
            <div className="mt-7 flex flex-col items-start gap-2.5">
              {[
                {
                  icon: "book-open",
                  label: "Lectures, materials and your study plan",
                  stagger: "login-stagger-4",
                },
                {
                  icon: "clipboard-check",
                  label: "Exams, results and attendance",
                  stagger: "login-stagger-5",
                },
                {
                  icon: "briefcase",
                  label: "Career help when you graduate",
                  stagger: "login-stagger-6",
                },
              ].map((item) => (
                <div
                  key={item.label}
                  className={`login-stagger ${item.stagger} login-feature-pill inline-flex items-center gap-3 rounded-full px-4 py-2.5 text-sm font-medium text-[#F5F3FF]`}
                >
                  <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-[#7C3AED]/45 text-base text-[#E9D5FF]">
                    <UiIcon name={item.icon} />
                  </span>
                  <span>{item.label}</span>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* 3. Right Side: Sleek True Frosted-Glass Sign-In Card */}
        <section className="lg:col-span-5">
          <div className="login-glass-panel mx-auto w-full max-w-[430px] rounded-3xl p-7 sm:p-8">
            {/* Centered Spinning Sphere Logo + Brand Name + Welcome Back */}
            <div className="mb-6 flex flex-col items-center text-center">
              <div className="login-sphere-spin mb-2.5 rounded-full">
                <BrandLogo size={54} className="ring-2 ring-[#C4B5FD]/50 shadow-[0_0_24px_rgba(124,58,237,0.45)]" />
              </div>
              <div className="text-sm font-bold tracking-tight text-white">
                UniSphere <span className="text-[#C084FC]">360+</span>
              </div>
              <h1 className="mt-3 mb-0 text-2xl font-bold tracking-tight text-white">Welcome back</h1>
              <p className="mt-1 mb-0 text-xs text-[#DDD6FE]">Sign in to enter your university workspace</p>
            </div>

            {expired && !error && (
              <div className="mb-4 rounded-control border border-[#FBBF24]/40 bg-[#FBBF24]/15 px-3.5 py-2.5 text-xs text-[#FDE68A]">
                Your previous session expired. Sign in again to continue.
              </div>
            )}

            {error && (
              <div className="mb-4">
                <Alert kind="error">{error}</Alert>
              </div>
            )}

            <form onSubmit={(e) => void handleSubmit(e)} className="flex flex-col gap-3.5">
              <div>
                <label htmlFor="login-email" className="mb-1.5 block text-xs font-medium text-[#EDE9FE]">
                  Email
                </label>
                <div className="flex items-center gap-2.5 rounded-xl border border-white/18 bg-[#120929]/68 px-3.5 py-2.5 transition-colors focus-within:border-[#A78BFA]">
                  <span className="text-base text-[#C4B5FD]">
                    <UiIcon name="mail" />
                  </span>
                  <input
                    id="login-email"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="name@unisphere.edu"
                    autoComplete="email"
                    className="w-full border-0 bg-transparent text-sm text-white outline-none placeholder:text-[#A594C8]"
                  />
                </div>
              </div>

              <div>
                <label htmlFor="login-password" className="mb-1.5 block text-xs font-medium text-[#EDE9FE]">
                  Password
                </label>
                <div className="flex items-center gap-2.5 rounded-xl border border-white/18 bg-[#120929]/68 px-3.5 py-2.5 transition-colors focus-within:border-[#A78BFA]">
                  <span className="text-base text-[#C4B5FD]">
                    <UiIcon name="lock" />
                  </span>
                  <input
                    id="login-password"
                    type={showPassword ? "text" : "password"}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="Enter your password"
                    autoComplete="current-password"
                    className="w-full border-0 bg-transparent text-sm text-white outline-none placeholder:text-[#A594C8]"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    aria-label={showPassword ? "Hide password" : "Show password"}
                    className="cursor-pointer border-0 bg-transparent p-1 text-base text-[#C4B5FD] transition-colors hover:text-white"
                  >
                    <UiIcon name={showPassword ? "eye-off" : "eye"} />
                  </button>
                </div>
              </div>

              <button
                type="submit"
                disabled={submitting}
                className="login-signin-btn mt-1.5 flex w-full cursor-pointer items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-semibold"
              >
                {submitting ? (
                  <>
                    <span className="h-4 w-4 animate-search-spin rounded-full border-2 border-white/40 border-t-white" />
                    Entering workspace…
                  </>
                ) : (
                  <>
                    Sign In <UiIcon name="arrow-right" />
                  </>
                )}
              </button>
            </form>

            {/* 4 Demo Account Pills in a Single Compact Row */}
            <div className="mt-5 border-t border-white/14 pt-4">
              <div className="mb-2 text-center text-[10.5px] font-semibold tracking-wider text-[#C4B5FD] uppercase">
                Quick Demo Access
              </div>
              <div className="grid grid-cols-4 gap-1.5">
                {DEMO_ACCOUNTS.map((acc) => {
                  const active = email.toLowerCase() === acc.email.toLowerCase();
                  return (
                    <button
                      key={acc.role}
                      type="button"
                      onClick={() => quickFill(acc)}
                      className={`cursor-pointer truncate rounded-full border px-2 py-1.5 text-[11.5px] font-medium transition-all ${
                        active
                          ? "border-[#C084FC] bg-[#7C3AED]/65 text-white shadow-[0_0_12px_rgba(168,85,247,0.35)]"
                          : "border-white/18 bg-white/8 text-[#EDE9FE] hover:border-[#C4B5FD]/60 hover:bg-white/16"
                      }`}
                    >
                      {acc.role}
                    </button>
                  );
                })}
              </div>
            </div>

            <p className="mt-4 mb-0 text-center text-xs text-[#DDD6FE]">
              New to UniSphere 360+?{" "}
              <Link to="/register" className="font-semibold text-[#D8B4FE] no-underline hover:underline">
                Create an account
              </Link>
            </p>
          </div>
        </section>
      </main>

      {/* Fast GPU-Accelerated (~600ms) Portal Transition Overlay */}
      {portalUser && (
        <div
          className="fixed inset-0 z-50 flex flex-col items-center justify-center gap-4 bg-[#160d2e]/88 backdrop-blur-xl"
          style={{ animation: "portal-flash-in 580ms cubic-bezier(0.16, 1, 0.3, 1) forwards" }}
        >
          <div className="login-sphere-spin rounded-full">
            <BrandLogo size={68} className="ring-4 ring-[#C084FC]/45" />
          </div>
          <div className="text-center">
            <div className="text-lg font-bold text-white">Welcome, {portalUser.firstName || portalUser.fullName}</div>
            <div className="mt-1 text-xs text-[#C4B5FD]">Opening {portalUser.role} workspace…</div>
          </div>
        </div>
      )}
    </div>
  );
}
