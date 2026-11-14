// Thin fetch wrapper around the UniSphere ASP.NET Core API.
//
// - Every request carries the JWT from POST /api/auth/login as a Bearer token.
// - Errors are normalised into ApiError with a human-readable message. The
//   backend answers with either a plain string ("Invalid email or password."),
//   RFC 7807 ProblemDetails ({ title, detail, errors }) or { message|error }.
// - A 401 on any authenticated call clears the session (expired token) so the
//   app falls back to the login page, mirroring the Blazor auth gate.

const TOKEN_KEY = 'unisphere.token';

/** Base URL of the API. Empty in development (Vite proxies /api to :5080). */
export const API_BASE: string = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

let token: string | null = readStoredToken();
const unauthorizedListeners = new Set<() => void>();

function readStoredToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

export function getToken(): string | null {
  return token;
}

export function setToken(value: string | null): void {
  token = value;
  try {
    if (value) localStorage.setItem(TOKEN_KEY, value);
    else localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Storage unavailable (private mode): keep the in-memory token only.
  }
}

/** Called when an authenticated request comes back 401 (expired / revoked token). */
export function onUnauthorized(listener: () => void): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

/** Human message for any thrown value (ApiError, network failure, etc.). */
export function errorMessage(err: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (err instanceof ApiError) return err.message || fallback;
  if (err instanceof TypeError) return 'Cannot reach the server. Make sure the UniSphere API is running.';
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}

export function url(path: string): string {
  return API_BASE + path;
}

function statusFallback(status: number): string {
  switch (status) {
    case 400: return 'The request was not valid.';
    case 401: return 'Your session has expired. Please sign in again.';
    case 403: return 'You do not have access to this.';
    case 404: return 'Not found.';
    case 409: return 'This conflicts with existing data.';
    case 413: return 'The file is too large.';
    case 429: return 'Too many requests. Please wait a moment and try again.';
    case 502: return 'The AI provider returned an invalid response. Please try again.';
    case 503: return 'This service is not available right now.';
    default: return status >= 500 ? 'The server ran into a problem. Please try again.' : `Request failed (${status}).`;
  }
}

async function toApiError(res: Response): Promise<ApiError> {
  let text = '';
  try {
    text = await res.text();
  } catch {
    /* ignore */
  }
  let message = '';
  let fieldErrors: Record<string, string[]> = {};
  if (text) {
    try {
      const body = JSON.parse(text) as unknown;
      if (typeof body === 'string') {
        message = body;
      } else if (body && typeof body === 'object') {
        const b = body as Record<string, unknown>;
        if (b.errors && typeof b.errors === 'object' && !Array.isArray(b.errors)) {
          fieldErrors = b.errors as Record<string, string[]>;
        }
        const firstField = Object.values(fieldErrors).flat()[0];
        message =
          (typeof b.detail === 'string' && b.detail) ||
          (typeof firstField === 'string' && firstField) ||
          (typeof b.message === 'string' && b.message) ||
          (typeof b.error === 'string' && b.error) ||
          (typeof b.title === 'string' && b.title) ||
          '';
        if (!message && Array.isArray(b.errors)) message = String(b.errors[0] ?? '');
      }
    } catch {
      // Plain-text body (ASP.NET returns raw strings for BadRequest("...")).
      message = text.length < 400 ? text : '';
    }
  }
  return new ApiError(res.status, message || statusFallback(res.status), fieldErrors);
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Send without the Authorization header (login / register). */
  anonymous?: boolean;
  signal?: AbortSignal;
  headers?: Record<string, string>;
}

/** Low-level request returning the raw Response after error handling. */
export async function rawRequest(path: string, opts: RequestOptions = {}): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json', ...(opts.headers ?? {}) };
  let body: BodyInit | undefined;
  if (opts.body instanceof FormData) {
    body = opts.body; // the browser sets the multipart boundary itself
  } else if (opts.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(opts.body);
  }
  if (!opts.anonymous && token) headers.Authorization = `Bearer ${token}`;

  const res = await fetch(url(path), { method: opts.method ?? 'GET', headers, body, signal: opts.signal });
  if (!res.ok) {
    if (res.status === 401 && !opts.anonymous && token) {
      unauthorizedListeners.forEach((l) => l());
    }
    throw await toApiError(res);
  }
  return res;
}

/** JSON request. Empty bodies (204 No Content) resolve to undefined. */
export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const res = await rawRequest(path, opts);
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  if (!text) return undefined as T;
  try {
    return JSON.parse(text) as T;
  } catch {
    return text as unknown as T;
  }
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { signal }),
  post: <T = void>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body: body ?? {} }),
  put: <T = void>(path: string, body?: unknown) => request<T>(path, { method: 'PUT', body: body ?? {} }),
  patch: <T = void>(path: string, body?: unknown) => request<T>(path, { method: 'PATCH', body: body ?? {} }),
  del: <T = void>(path: string) => request<T>(path, { method: 'DELETE' }),
  upload: <T>(path: string, form: FormData, method = 'POST') => request<T>(path, { method, body: form }),
};

/** Build a query string from defined, non-empty values. */
export function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue;
    sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

/**
 * Download a protected file. A plain <a href> cannot carry the bearer token,
 * so the file is fetched with it and handed to the browser as a Blob
 * (same approach as wwwroot/js/research.js in the Blazor UI).
 */
export async function downloadFile(path: string, fallbackName = 'download'): Promise<void> {
  const res = await rawRequest(path, { headers: { Accept: '*/*' } });
  const blob = await res.blob();
  const name = fileNameFromDisposition(res.headers.get('Content-Disposition')) || fallbackName;
  const href = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = href;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(href), 2000);
}

function fileNameFromDisposition(header: string | null): string | null {
  if (!header) return null;
  const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(header);
  if (star) {
    try {
      return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ''));
    } catch {
      /* fall through */
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : null;
}

/** Resolve a server-relative asset URL (e.g. /uploads/doctor/avatar/x.png). */
export function assetUrl(path: string | null | undefined): string | undefined {
  if (!path) return undefined;
  if (/^(https?:|data:|blob:)/i.test(path)) return path;
  return API_BASE + (path.startsWith('/') ? path : `/${path}`);
}
