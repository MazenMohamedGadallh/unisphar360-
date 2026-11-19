/*
 * Small shared helpers for calling the .NET API from React pages.
 * (Login/Register/Chat keep their own copies for now; new pages use this.)
 */

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";
export const TOKEN_KEY = "unisphere.auth.token";

export function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

export function clearToken(): void {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Storage unavailable: nothing to clear.
  }
}

/** Authorization header for fetch calls (empty bearer when no token). */
export function authHeaders(): Record<string, string> {
  return { Authorization: `Bearer ${getToken() ?? ""}` };
}

/** Signed-in user as returned by GET /api/auth/me. */
export interface AuthUser {
  id: number;
  fullName: string;
  email: string;
  role: string;
  avatarUrl?: string | null;
}
