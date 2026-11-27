import { Link } from "react-router-dom";
import type { ReactNode } from "react";

/*
 * Reusable UI pieces shared by every React page (pure Tailwind utilities
 * + the design tokens from app.css — no component library dependency).
 */

/* ---------- Card ---------- */

export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <section className={`rounded-card border border-border/90 bg-card/88 shadow-card backdrop-blur-md ${className}`}>
      {children}
    </section>
  );
}

export function CardHeader({
  title,
  action,
}: {
  title: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex items-center justify-between border-b border-border px-5 py-4">
      <h3 className="text-base font-semibold">{title}</h3>
      {action}
    </div>
  );
}

/** Small "View all"-style header link (right side of a card header). */
export function CardLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Link
      to={to}
      className="inline-flex items-center gap-1 text-[13px] font-medium text-primary no-underline transition-colors hover:underline"
    >
      {children}
    </Link>
  );
}

/* ---------- Button ---------- */

type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";

const BUTTON_BASE =
  "inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 font-medium no-underline transition-colors disabled:cursor-not-allowed disabled:opacity-60";
const BUTTON_VARIANTS: Record<ButtonVariant, string> = {
  primary: "bg-primary text-white hover:bg-primary-hover",
  secondary: "border border-border bg-bg text-text hover:bg-border",
  danger: "bg-danger text-white hover:bg-danger-hover",
  ghost: "bg-transparent text-primary hover:bg-primary-light",
};
const BUTTON_SIZES = {
  md: "px-5 py-2.5 text-sm",
  sm: "px-3.5 py-1.5 text-[13px]",
} as const;

export function Button({
  variant = "primary",
  size = "md",
  type = "button",
  to,
  onClick,
  disabled,
  title,
  children,
}: {
  variant?: ButtonVariant;
  size?: keyof typeof BUTTON_SIZES;
  type?: "button" | "submit" | "reset";
  /** When set, renders a react-router Link styled as a button. */
  to?: string;
  onClick?: () => void;
  disabled?: boolean;
  title?: string;
  children: ReactNode;
}) {
  const classes = `${BUTTON_BASE} ${BUTTON_VARIANTS[variant]} ${BUTTON_SIZES[size]}`;
  if (to) {
    return (
      <Link to={to} className={classes} title={title}>
        {children}
      </Link>
    );
  }
  return (
    <button type={type} className={classes} onClick={onClick} disabled={disabled} title={title}>
      {children}
    </button>
  );
}

/* ---------- Alert ---------- */

type AlertKind = "error" | "success" | "warning" | "info";

const ALERT_KINDS: Record<AlertKind, string> = {
  error: "border-danger bg-danger-light text-danger",
  success: "border-success bg-success-light text-success",
  warning: "border-warning bg-warning-light text-warning",
  info: "border-secondary bg-secondary-light text-secondary",
};

export function Alert({ kind = "error", children }: { kind?: AlertKind; children: ReactNode }) {
  return (
    <div
      role="alert"
      className={`mb-4 flex items-start gap-2.5 rounded-control border px-4 py-3 text-sm ${ALERT_KINDS[kind]}`}
    >
      {children}
    </div>
  );
}

/* ---------- Status badge ---------- */

export function StatusBadge({ status }: { status: string }) {
  const slug = status.toLowerCase().replace(/\s+/g, "-");
  const tone =
    slug.includes("to-grade") || slug.includes("pending")
      ? "bg-warning-light text-warning"
      : (
          {
            excellent: "bg-success-light text-success",
            passed: "bg-success-light text-success",
            good: "bg-secondary-light text-secondary",
            available: "bg-secondary-light text-secondary",
            "in-progress": "bg-warning-light text-warning",
            "needs-attention": "bg-danger-light text-danger",
            locked: "bg-danger-light text-danger",
          }[slug] ?? "border border-border bg-bg text-text-secondary"
        );
  return (
    <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${tone}`}>
      {status}
    </span>
  );
}

/* ---------- Empty / error / loading states ---------- */

export function EmptyState({
  icon,
  message,
}: {
  icon: string;
  message: string;
}) {
  return (
    <div className="flex flex-col items-center gap-2 px-6 py-12 text-center text-sm text-text-secondary">
      <span className="mb-1 flex h-11 w-11 items-center justify-center rounded-full bg-primary-light text-xl text-primary">
        <UiIcon name={icon} />
      </span>
      {message}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-start gap-2.5 px-5 py-4">
      <Alert kind="error">{message}</Alert>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
}

export function Spinner({ className = "" }: { className?: string }) {
  return (
    <span
      className={`inline-block h-5 w-5 animate-search-spin rounded-full border-2 border-border border-t-primary ${className}`}
      aria-hidden="true"
    />
  );
}

/** Centered "Loading …" block used while a page/card loads. */
export function LoadingBlock({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-10 text-sm text-text-secondary">
      <Spinner />
      <span>{label}</span>
    </div>
  );
}

/** Grey shimmer placeholder bar (use while the real content loads). */
export function Skeleton({ className = "" }: { className?: string }) {
  return <span className={`block animate-pulse rounded-control bg-border ${className}`} aria-hidden="true" />;
}

/* ---------- Confirm dialog ---------- */

import { UiIcon } from "../../UiIcon";

export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  danger = false,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  if (!open) return null;
  return (
    <div
      className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/50"
      onClick={onCancel}
      role="presentation"
    >
      <div
        className="w-[90%] max-w-[420px] rounded-card border border-border bg-surface shadow-card-lg"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          if (e.key === "Escape") onCancel();
        }}
      >
        <div className="border-b border-border px-5 py-4">
          <h3 className="text-lg font-semibold">{title}</h3>
        </div>
        <div className="px-5 py-4 text-sm">{children}</div>
        <div className="flex justify-end gap-3 border-t border-border px-5 py-4">
          <Button variant="secondary" onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button variant={danger ? "danger" : "primary"} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
