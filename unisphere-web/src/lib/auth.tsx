// Session handling: the JWT returned by POST /api/auth/login is kept in
// localStorage (so a refresh keeps you signed in, like the Blazor UI's
// ProtectedLocalStorage) and the signed-in user comes from GET /api/auth/me.

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, ApiError, getToken, onUnauthorized, request, setToken } from './api';
import type { AuthResponse, AuthUserDto, RegisterRequest } from '../types/dto';

export type Role = 'Student' | 'Graduate' | 'Doctor' | 'Admin';

type Status = 'loading' | 'anonymous' | 'authenticated';

interface AuthValue {
  status: Status;
  user: AuthUserDto | null;
  role: Role | null;
  login: (email: string, password: string) => Promise<AuthUserDto>;
  register: (req: RegisterRequest) => Promise<AuthUserDto>;
  logout: () => void;
  /** Re-read /api/auth/me (after a profile change). */
  refreshUser: () => Promise<void>;
  /** True when the last session ended because the token expired. */
  expired: boolean;
}

const AuthContext = createContext<AuthValue | null>(null);

export function normaliseRole(role: string | null | undefined): Role | null {
  switch ((role ?? '').trim().toLowerCase()) {
    case 'student': return 'Student';
    case 'graduate': return 'Graduate';
    case 'doctor': return 'Doctor';
    case 'admin': return 'Admin';
    default: return null;
  }
}

/** Same mapping as Services/RoleRoutes.HomeFor on the backend. */
export function homeFor(role: string | null | undefined): string {
  switch (normaliseRole(role)) {
    case 'Student': return '/dashboard';
    case 'Doctor': return '/doctor';
    case 'Graduate': return '/graduate';
    case 'Admin': return '/admin';
    default: return '/login';
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>(() => (getToken() ? 'loading' : 'anonymous'));
  const [user, setUser] = useState<AuthUserDto | null>(null);
  const [expired, setExpired] = useState(false);

  const clear = useCallback((wasExpired: boolean) => {
    setToken(null);
    setUser(null);
    setStatus('anonymous');
    setExpired(wasExpired);
  }, []);

  // Restore the session on first load.
  useEffect(() => {
    if (!getToken()) return;
    let cancelled = false;
    request<AuthUserDto>('/api/auth/me')
      .then((me) => {
        if (cancelled) return;
        setUser(me);
        setStatus('authenticated');
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof ApiError && (err.status === 401 || err.status === 403)) clear(err.status === 401);
        else {
          // Server unreachable: keep the token, show the app shell's error states.
          setStatus('anonymous');
        }
      });
    return () => {
      cancelled = true;
    };
  }, [clear]);

  // Any API call that comes back 401 ends the session.
  useEffect(() => onUnauthorized(() => clear(true)), [clear]);

  const login = useCallback(async (email: string, password: string) => {
    const res = await request<AuthResponse>('/api/auth/login', {
      method: 'POST',
      body: { email: email.trim(), password },
      anonymous: true,
    });
    setToken(res.token);
    setUser(res.user);
    setExpired(false);
    setStatus('authenticated');
    return res.user;
  }, []);

  const register = useCallback(async (req: RegisterRequest) => {
    const res = await request<AuthResponse>('/api/auth/register', { method: 'POST', body: req, anonymous: true });
    setToken(res.token);
    setUser(res.user);
    setExpired(false);
    setStatus('authenticated');
    return res.user;
  }, []);

  const logout = useCallback(() => clear(false), [clear]);

  const refreshUser = useCallback(async () => {
    const me = await api.get<AuthUserDto>('/api/auth/me');
    setUser(me);
  }, []);

  const value = useMemo<AuthValue>(
    () => ({ status, user, role: normaliseRole(user?.role), login, register, logout, refreshUser, expired }),
    [status, user, login, register, logout, refreshUser, expired],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>.');
  return ctx;
}
