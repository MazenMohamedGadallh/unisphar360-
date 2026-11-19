import { useEffect } from "react";
import { useNavigate } from "react-router-dom";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";
const TOKEN_KEY = "unisphere.auth.token";

interface AuthUser {
  id: number;
  fullName: string;
  email: string;
  roleId: number;
  role: string;
  major: string;
  avatarUrl?: string | null;
}

function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
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

export function Home() {
  const navigate = useNavigate();

  useEffect(() => {
    const token = getToken();
    if (!token) {
      navigate("/login", { replace: true });
      return;
    }
    if (!isTokenUsable(token)) {
      clearToken();
      navigate("/login", { replace: true });
      return;
    }
    void (async () => {
      const user = await fetchMe(token);
      if (user) navigate(homeFor(user.role), { replace: true });
      else navigate("/login", { replace: true });
    })();
    // Empty dependency array on purpose: this runs once on mount.
  }, []);

  return (
    <div className="flex min-h-[80vh] flex-col items-center justify-center gap-3.5 text-sm text-text-secondary" role="status">
      <span className="h-[22px] w-[22px] flex-none animate-search-spin rounded-full border-2 border-border border-t-primary" aria-hidden="true" />
      <span>Opening your workspace&hellip;</span>
    </div>
  );
}
