import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from './api';

export interface Resource<T> {
  data: T | undefined;
  loading: boolean;
  error: string | null;
  reload: () => Promise<void>;
  setData: (updater: T | ((prev: T | undefined) => T | undefined)) => void;
}

/**
 * Load JSON from an API path. Re-runs when the path changes; pass null to
 * skip loading. Stale responses (path changed mid-flight) are ignored.
 */
export function useApi<T>(path: string | null): Resource<T> {
  const [data, setDataState] = useState<T | undefined>(undefined);
  const [loading, setLoading] = useState<boolean>(path !== null);
  const [error, setError] = useState<string | null>(null);
  const seq = useRef(0);

  const load = useCallback(async () => {
    if (path === null) {
      setLoading(false);
      return;
    }
    const id = ++seq.current;
    setLoading(true);
    setError(null);
    try {
      const result = await api.get<T>(path);
      if (id === seq.current) setDataState(result);
    } catch (err) {
      if (id === seq.current) setError(errorMessage(err));
    } finally {
      if (id === seq.current) setLoading(false);
    }
  }, [path]);

  useEffect(() => {
    void load();
  }, [load]);

  const setData = useCallback((updater: T | ((prev: T | undefined) => T | undefined)) => {
    setDataState((prev) =>
      typeof updater === 'function' ? (updater as (p: T | undefined) => T | undefined)(prev) : updater,
    );
  }, []);

  return { data, loading, error, reload: load, setData };
}

/** Run an async action with busy + error state (form submits, buttons). */
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(async <R,>(fn: () => Promise<R>): Promise<R | undefined> => {
    setBusy(true);
    setError(null);
    try {
      return await fn();
    } catch (err) {
      setError(errorMessage(err));
      return undefined;
    } finally {
      setBusy(false);
    }
  }, []);

  return { busy, error, setError, run };
}

/** Debounce a changing value. */
export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** Set document.title like the Blazor <PageTitle>. */
export function usePageTitle(title: string): void {
  useEffect(() => {
    document.title = title ? `${title} - UniSphere 360+` : 'UniSphere 360+';
  }, [title]);
}

/** A short-lived flash message (e.g. "Saved."). */
export function useFlash(ms = 3500): [string | null, (msg: string | null) => void] {
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => {
    if (!msg) return;
    const t = setTimeout(() => setMsg(null), ms);
    return () => clearTimeout(t);
  }, [msg, ms]);
  return [msg, setMsg];
}
