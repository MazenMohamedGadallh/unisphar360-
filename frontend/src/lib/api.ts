/*
 * Shared helpers for calling the UniSphere 360+ API from React pages.
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

export function saveToken(token: string): void {
  try {
    localStorage.setItem(TOKEN_KEY, token);
  } catch {
    // Storage unavailable
  }
}

export function clearToken(): void {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Storage unavailable
  }
}

/** Authorization header for fetch calls. */
export function authHeaders(): Record<string, string> {
  return { Authorization: `Bearer ${getToken() ?? ""}` };
}

/** Signed-in user as returned by GET /api/auth/me and profile endpoints. */
export interface AuthUser {
  id: number;
  firstName?: string;
  lastName?: string;
  fullName: string;
  email: string;
  phoneNumber?: string;
  roleId?: number;
  role: string;
  major?: string;
  department?: string | null;
  specialization?: string | null;
  academicYear?: number | null;
  universityId?: number | null;
  universityName?: string | null;
  avatarUrl?: string | null;
  bio?: string;
}

const USER_UPDATED_EVENT = "unisphere:user-updated";
const NOTIF_UPDATED_EVENT = "unisphere:notif-updated";

export function notifyUserUpdated(user: AuthUser): void {
  window.dispatchEvent(new CustomEvent<AuthUser>(USER_UPDATED_EVENT, { detail: user }));
}

export function onUserUpdated(handler: (user: AuthUser) => void): () => void {
  const listener = (ev: Event) => {
    const custom = ev as CustomEvent<AuthUser>;
    if (custom.detail) handler(custom.detail);
  };
  window.addEventListener(USER_UPDATED_EVENT, listener);
  return () => window.removeEventListener(USER_UPDATED_EVENT, listener);
}

export function notifyNotificationsUpdated(): void {
  window.dispatchEvent(new Event(NOTIF_UPDATED_EVENT));
}

export function onNotificationsUpdated(handler: () => void): () => void {
  window.addEventListener(NOTIF_UPDATED_EVENT, handler);
  return () => window.removeEventListener(NOTIF_UPDATED_EVENT, handler);
}

/** Message from an ASP.NET error body (ProblemDetails, validation errors, or { error | message }). */
export async function errorMessage(res: Response, fallback: string): Promise<string> {
  const body = await res.json().catch(() => null);
  if (typeof body === "string") return body;
  const validation = body?.errors ? Object.values(body.errors).flat().join(" ") : "";
  return validation || body?.error || body?.message || body?.detail || body?.title || fallback;
}

/** The API stores uploads as root-relative paths, which must resolve against the API, not the Vite server. */
export function assetUrl(path: string): string {
  return path.startsWith("/uploads/") ? `${API_BASE_URL}${path}` : path;
}
