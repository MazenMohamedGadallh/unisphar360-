// Formatting helpers. The API sends UTC timestamps (DateTime ...Utc); some
// arrive without a "Z" suffix, so treat zone-less ISO strings as UTC.

export function parseDate(value: string | null | undefined): Date | null {
  if (!value) return null;
  const hasZone = /[zZ]|[+-]\d{2}:?\d{2}$/.test(value);
  const d = new Date(hasZone || value.length <= 10 ? value : `${value}Z`);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function fmtDate(value: string | null | undefined, opts?: Intl.DateTimeFormatOptions): string {
  const d = parseDate(value);
  if (!d) return '—';
  return d.toLocaleDateString(undefined, opts ?? { year: 'numeric', month: 'short', day: 'numeric' });
}

export function fmtDateTime(value: string | null | undefined): string {
  const d = parseDate(value);
  if (!d) return '—';
  return d.toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function fmtTime(value: string | null | undefined): string {
  const d = parseDate(value);
  if (!d) return '';
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

export function timeAgo(value: string | null | undefined): string {
  const d = parseDate(value);
  if (!d) return '';
  const secs = Math.round((Date.now() - d.getTime()) / 1000);
  if (secs < 45) return 'just now';
  const mins = Math.round(secs / 60);
  if (mins < 60) return `${mins} min ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days} day${days === 1 ? '' : 's'} ago`;
  return fmtDate(value);
}

export function fmtNumber(n: number | null | undefined, digits = 2): string {
  if (n === null || n === undefined || Number.isNaN(n)) return '—';
  return n.toFixed(digits);
}

export function fmtBytes(bytes: number | null | undefined): string {
  if (!bytes && bytes !== 0) return '';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** yyyy-MM-dd for <input type="date">. */
export function toDateInput(value: string | null | undefined): string {
  const d = parseDate(value);
  if (!d) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** yyyy-MM-ddTHH:mm for <input type="datetime-local">. */
export function toDateTimeInput(value: string | null | undefined): string {
  const d = parseDate(value);
  if (!d) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${toDateInput(value)}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Local datetime-input value → ISO UTC string (or null when empty). */
export function fromDateTimeInput(value: string): string | null {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Date-input value (yyyy-MM-dd) → ISO string at UTC midnight (or null). */
export function fromDateInput(value: string): string | null {
  return value ? `${value}T00:00:00Z` : null;
}

export function initials(name: string | null | undefined): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  return (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
}

export function clsx(...parts: Array<string | false | null | undefined>): string {
  return parts.filter(Boolean).join(' ');
}

/** Lower-case kebab key used for status badge classes. */
export function statusKey(status: string | null | undefined): string {
  return (status ?? '').replace(/([a-z])([A-Z])/g, '$1-$2').replace(/\s+/g, '-').toLowerCase();
}

export function splitList(value: string | null | undefined): string[] {
  return (value ?? '')
    .split(/[,;\n]/)
    .map((s) => s.trim())
    .filter(Boolean);
}
