/* Shared pieces of the graduate workspace: page frame, loading/error handling, meters and form styles. */
import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { clearToken } from "../lib/api";
import { Card, ErrorState, Skeleton } from "./ui";
import { gradInput } from "./GradModal";

export const field = gradInput;
export const label = "block text-sm font-medium text-text";

export const fmtDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { dateStyle: "medium" });

/** Loads page data once (and on demand); sends expired sessions to the login page. */
export function useGradData<T>(load: () => Promise<T>) {
  const navigate = useNavigate();
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setError(null);
    try {
      setData(await load());
    } catch (e) {
      if ((e as Error).name === "Unauthorized") {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      setError((e as Error).message);
    }
  }, [load, navigate]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { data, error, reload };
}

/** Runs one action at a time, so a double click cannot send a request twice. */
export function useAction() {
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);

  async function run(name: string, action: () => Promise<unknown>, done?: string) {
    if (busy) return false;
    setBusy(name);
    setMessage(null);
    try {
      await action();
      if (done) setMessage({ kind: "success", text: done });
      return true;
    } catch (e) {
      setMessage({ kind: "error", text: (e as Error).message });
      return false;
    } finally {
      setBusy(null);
    }
  }

  return { busy, message, run };
}

export function Page({
  title, description, actions, error, loading, onRetry, children,
}: {
  title: string;
  description: string;
  actions?: ReactNode;
  error?: string | null;
  loading?: boolean;
  onRetry?: () => void;
  children?: ReactNode;
}) {
  useEffect(() => {
    document.title = `${title} - UniSphere 360+`;
  }, [title]);

  return (
    <div>
      <header className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div className="min-w-0">
          <h1 className="m-0 text-2xl font-semibold text-text">{title}</h1>
          <p className="mt-1 mb-0 max-w-[70ch] text-sm text-text-secondary">{description}</p>
        </div>
        {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
      </header>
      {error ? (
        <Card><ErrorState message={error} onRetry={onRetry} /></Card>
      ) : loading ? (
        <div className="grid gap-4" aria-busy="true" aria-label="Loading">
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-56 w-full" />
        </div>
      ) : (
        children
      )}
    </div>
  );
}

export function Panel({ title, aside, children, className = "" }: { title?: string; aside?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <Card className={`p-5 ${className}`}>
      {(title || aside) && (
        <div className="mb-4 flex flex-wrap items-baseline justify-between gap-2">
          {title && <h2 className="m-0 text-base font-semibold text-text">{title}</h2>}
          {aside}
        </div>
      )}
      {children}
    </Card>
  );
}

/** A null value reads "Not assessed": an empty bar would look like a score of zero. */
export function Meter({ name, value }: { name: string; value: number | null }) {
  return (
    <div>
      <div className="flex justify-between gap-3 text-sm">
        <span className="font-medium text-text">{name}</span>
        <span className="text-text-secondary tabular-nums">{value === null ? "Not assessed" : `${value}%`}</span>
      </div>
      <div
        className="mt-1.5 h-2 overflow-hidden rounded-full bg-border"
        role="meter" aria-label={name} aria-valuemin={0} aria-valuemax={100} aria-valuenow={value ?? undefined}
      >
        <div className="h-full rounded-full bg-primary" style={{ width: `${value ?? 0}%` }} />
      </div>
    </div>
  );
}

export function Tag({ tone = "plain", children }: { tone?: "plain" | "good" | "warn" | "bad" | "info"; children: ReactNode }) {
  const tones = {
    plain: "bg-bg text-text-secondary border border-border",
    good: "bg-success-light text-success",
    warn: "bg-warning-light text-warning",
    bad: "bg-danger-light text-danger",
    info: "bg-primary-light text-primary",
  };
  return <span className={`inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${tones[tone]}`}>{children}</span>;
}

export function Bullets({ items, empty = "Nothing to show." }: { items: string[]; empty?: string }) {
  return items.length === 0 ? (
    <p className="m-0 text-sm text-text-muted">{empty}</p>
  ) : (
    <ul className="m-0 list-disc space-y-1.5 pl-5 text-sm text-text-secondary">
      {items.map((item) => <li key={item}>{item}</li>)}
    </ul>
  );
}

export interface Mission {
  key: string; title: string; description: string; criteria: string; xp: number; route: string;
  status: "completed" | "active" | "locked"; completedAtUtc: string | null;
}

export interface Journey {
  careerLevel: number;
  xp: number;
  completedMissions: number;
  totalMissions: number;
  levels: { level: number; title: string; badge: string; status: Mission["status"]; missions: Mission[] }[];
  nextMission: Mission | null;
  history: { reason: string; amount: number; atUtc: string }[];
}

/** The eight levels as one route; the numbers are a real sequence, each level opens the next. */
export function Trail({ journey, selected, onSelect }: { journey: Journey; selected?: number; onSelect?: (level: number) => void }) {
  return (
    <ol className="m-0 grid list-none grid-cols-4 gap-y-4 p-0 lg:grid-cols-8">
      {journey.levels.map((l) => {
        const dot = l.status === "completed" ? "bg-primary text-white border-primary"
          : l.status === "active" ? "bg-card text-primary border-primary border-[3px]"
          : "bg-bg text-text-muted border-border";
        const body = (
          <>
            <span className={`relative z-10 flex h-10 w-10 items-center justify-center rounded-full border-2 text-sm font-bold ${dot}`}>{l.level}</span>
            <span className={`px-1 text-center text-xs leading-snug ${selected === l.level ? "font-semibold text-text" : "text-text-secondary"}`}>{l.title}</span>
          </>
        );
        return (
          <li key={l.level} className="relative">
            <span aria-hidden="true" className={`absolute top-5 right-1/2 h-0.5 w-full ${l.level % 4 === 1 ? "hidden" : ""} ${l.level === 5 ? "lg:block" : ""} ${l.status === "locked" ? "bg-border" : "bg-primary"}`} />
            {onSelect ? (
              <button type="button" aria-pressed={selected === l.level} aria-label={`Level ${l.level}: ${l.title}, ${l.status}`}
                onClick={() => onSelect(l.level)} className="flex w-full cursor-pointer flex-col items-center gap-1.5 border-0 bg-transparent p-0">
                {body}
              </button>
            ) : (
              <div className="flex flex-col items-center gap-1.5">{body}</div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
