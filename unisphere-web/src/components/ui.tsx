// Shared building blocks that reuse the class names from app.css (the same
// stylesheet the Blazor UI used), so every page keeps the original look.

import { useEffect, useRef, type ReactNode } from 'react';
import { Icon } from './Icon';
import { clsx, statusKey } from '../lib/format';

export function PageHeader({ title, subtitle, actions }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="page-header page-header-row">
      <div>
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </div>
  );
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="loading" role="status">
      <span className="search-spinner" aria-hidden="true" /> {label}
    </div>
  );
}

export function Alert({ kind = 'error', children, onClose }: { kind?: 'error' | 'success' | 'warning' | 'info'; children: ReactNode; onClose?: () => void }) {
  if (!children) return null;
  return (
    <div className={`alert alert-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      <span>{children}</span>
      {onClose && (
        <button type="button" className="alert-close" onClick={onClose} aria-label="Dismiss">
          <Icon name="x" />
        </button>
      )}
    </div>
  );
}

export function ErrorBox({ error, onRetry }: { error: string | null | undefined; onRetry?: () => void }) {
  if (!error) return null;
  return (
    <div className="alert alert-error alert-row" role="alert">
      <span>{error}</span>
      {onRetry && (
        <button type="button" className="btn btn-sm btn-secondary" onClick={onRetry}>
          Retry
        </button>
      )}
    </div>
  );
}

/**
 * Renders a useApi() resource: error box with retry, loading text, an empty
 * state when `isEmpty` says so, otherwise children(data).
 */
export function Async<T>({
  res,
  loadingLabel,
  empty,
  emptyIcon,
  isEmpty,
  children,
}: {
  res: { data: T | undefined; loading: boolean; error: string | null; reload: () => void | Promise<void> };
  loadingLabel?: string;
  empty?: ReactNode;
  emptyIcon?: string;
  isEmpty?: (data: T) => boolean;
  children: (data: T) => ReactNode;
}) {
  if (res.error) return <ErrorBox error={res.error} onRetry={() => void res.reload()} />;
  if (res.data === undefined) return res.loading ? <Loading label={loadingLabel} /> : null;
  const data = res.data;
  const emptyNow = isEmpty ? isEmpty(data) : Array.isArray(data) && data.length === 0;
  if (emptyNow && empty) return <Empty icon={emptyIcon}>{empty}</Empty>;
  return <>{children(data)}</>;
}

export function Empty({ icon = 'inbox', children }: { icon?: string; children: ReactNode }) {
  return (
    <div className="empty-state">
      <Icon name={icon} size={28} />
      <div>{children}</div>
    </div>
  );
}

export function Card({ title, icon, actions, children, className }: { title?: ReactNode; icon?: string; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={clsx('card', className)}>
      {(title || actions) && (
        <div className="card-header">
          {title && (
            <h3>
              {icon && <Icon name={icon} />} {title}
            </h3>
          )}
          {actions && <div className="card-header-actions">{actions}</div>}
        </div>
      )}
      <div className="card-body">{children}</div>
    </section>
  );
}

export function StatCard({ icon, label, value, delta }: { icon: string; label: ReactNode; value: ReactNode; delta?: ReactNode }) {
  return (
    <div className="card stat-card">
      <div className="card-icon">
        <Icon name={icon} />
      </div>
      <div className="card-info">
        <span className="card-label">{label}</span>
        <span className="card-value">{value}</span>
        {delta !== undefined && delta !== null && delta !== '' && <span className="card-delta">{delta}</span>}
      </div>
    </div>
  );
}

export function StatusBadge({ status, label }: { status: string | null | undefined; label?: ReactNode }) {
  if (!status && !label) return null;
  return <span className={`status-badge ${statusKey(status)}`}>{label ?? status}</span>;
}

export function Modal({
  title,
  onClose,
  children,
  footer,
  wide,
}: {
  title: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    ref.current?.focus();
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="modal-overlay" onMouseDown={onClose}>
      <div
        ref={ref}
        tabIndex={-1}
        className={clsx('modal', wide && 'modal-wide')}
        role="dialog"
        aria-modal="true"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h3>{title}</h3>
          <button type="button" className="modal-close" onClick={onClose} aria-label="Close">
            <Icon name="x" />
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-actions">{footer}</div>}
      </div>
    </div>
  );
}

export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Delete',
  danger = true,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <Modal
      title={title}
      onClose={onCancel}
      footer={
        <>
          <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </button>
          <button type="button" className={clsx('btn', danger ? 'btn-danger' : 'btn-primary')} onClick={onConfirm} disabled={busy}>
            {busy ? 'Working…' : confirmLabel}
          </button>
        </>
      }
    >
      <p>{message}</p>
    </Modal>
  );
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
  if (total <= pageSize) return null;
  return (
    <div className="pagination">
      <button type="button" className="btn btn-sm btn-secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        <Icon name="chevron-left" /> Previous
      </button>
      <span>
        Page {page} of {pages} · {total} total
      </span>
      <button type="button" className="btn btn-sm btn-secondary" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        Next <Icon name="chevron-right" />
      </button>
    </div>
  );
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: Array<{ key: T; label: ReactNode }>; value: T; onChange: (k: T) => void }) {
  return (
    <div className="admin-tabs" role="tablist">
      {tabs.map((t, tIdx) => (
        <button
          key={`${t.key}-${tIdx}`}
          type="button"
          role="tab"
          aria-selected={value === t.key}
          className={clsx('admin-tab', value === t.key && 'selected')}
          onClick={() => onChange(t.key)}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

/** Horizontal progress bar (0–100). */
export function Progress({ value, tone }: { value: number; tone?: 'success' | 'warning' | 'danger' }) {
  const pct = Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div className="progress" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
      <div className={clsx('progress-fill', tone && `tone-${tone}`)} style={{ width: `${pct}%` }} />
    </div>
  );
}

/** Simple labelled bar chart (label → count). */
export function BarList({ items, max, suffix = '' }: { items: Array<{ label: string; value: number }>; max?: number; suffix?: string }) {
  const top = max ?? Math.max(1, ...items.map((i) => i.value));
  if (items.length === 0) return <Empty icon="chart-column">No data yet.</Empty>;
  return (
    <div className="bar-list">
      {items.map((i, iIdx) => (
        <div className="bar-list-row" key={`${i.label}-${iIdx}`}>
          <span className="bar-list-label" title={i.label}>
            {i.label}
          </span>
          <div className="bar-list-track">
            <div className="bar-list-fill" style={{ width: `${Math.round((i.value / top) * 100)}%` }} />
          </div>
          <span className="bar-list-value">
            {Number.isInteger(i.value) ? i.value : i.value.toFixed(1)}
            {suffix}
          </span>
        </div>
      ))}
    </div>
  );
}

export function Field({ label, children, hint, required }: { label: ReactNode; children: ReactNode; hint?: ReactNode; required?: boolean }) {
  return (
    <div className="form-group">
      <label>
        {label}
        {required && <span className="required-mark"> *</span>}
      </label>
      {children}
      {hint && <div className="field-hint">{hint}</div>}
    </div>
  );
}

export function TagList({ items, tone }: { items: string[] | null | undefined; tone?: 'success' | 'danger' | 'neutral' }) {
  if (!items || items.length === 0) return null;
  return (
    <div className="tag-list">
      {items.map((t, tIdx) => (
        <span key={`${t}-${tIdx}`} className={clsx('tag', tone && `tag-${tone}`)}>
          {t}
        </span>
      ))}
    </div>
  );
}

export function BulletList({ items, empty }: { items: string[] | null | undefined; empty?: string }) {
  if (!items || items.length === 0) return empty ? <p className="muted">{empty}</p> : null;
  return (
    <ul className="plain-list bullet-list">
      {items.map((t, i) => (
        <li key={i}>{t}</li>
      ))}
    </ul>
  );
}
