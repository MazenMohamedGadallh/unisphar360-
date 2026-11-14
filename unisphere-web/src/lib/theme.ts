// Theme handling: "light" | "dark" | "system" persisted under the same
// localStorage key as the Blazor UI (wwwroot/js/theme.js). The concrete theme
// is mirrored to <html data-theme>, which app.css keys off.

import { useCallback, useEffect, useState } from 'react';

export type ThemePref = 'light' | 'dark' | 'system';
const KEY = 'theme';

function systemTheme(): 'light' | 'dark' {
  try {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  } catch {
    return 'light';
  }
}

export function getThemePref(): ThemePref {
  try {
    const v = localStorage.getItem(KEY);
    if (v === 'light' || v === 'dark' || v === 'system') return v;
  } catch {
    /* ignore */
  }
  return 'system';
}

function apply(pref: ThemePref): 'light' | 'dark' {
  const concrete = pref === 'system' ? systemTheme() : pref;
  document.documentElement.dataset.theme = concrete;
  return concrete;
}

const listeners = new Set<() => void>();

export function setThemePref(pref: ThemePref): void {
  try {
    localStorage.setItem(KEY, pref);
  } catch {
    /* ignore */
  }
  apply(pref);
  listeners.forEach((l) => l());
}

export function useTheme() {
  const [pref, setPref] = useState<ThemePref>(getThemePref);
  const [concrete, setConcrete] = useState<'light' | 'dark'>(() => apply(getThemePref()));

  useEffect(() => {
    const sync = () => {
      const p = getThemePref();
      setPref(p);
      setConcrete(apply(p));
    };
    listeners.add(sync);
    let mq: MediaQueryList | null = null;
    try {
      mq = window.matchMedia('(prefers-color-scheme: dark)');
      mq.addEventListener('change', sync);
    } catch {
      /* ignore */
    }
    return () => {
      listeners.delete(sync);
      mq?.removeEventListener('change', sync);
    };
  }, []);

  const toggle = useCallback(() => setThemePref(concrete === 'dark' ? 'light' : 'dark'), [concrete]);
  return { pref, concrete, isDark: concrete === 'dark', setPref: setThemePref, toggle };
}
