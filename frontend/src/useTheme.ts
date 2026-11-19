import { useEffect, useState } from "react";

/*
 * Light/Dark theme for the React app.
 *
 * The preference lives in the REACT-ONLY localStorage key "unisphere.theme"
 * ("light" | "dark" | "system"). The Razor app stores its own preference
 * under a different key ("theme") — this code never reads or writes it.
 *
 * The resolved concrete theme is mirrored to document.documentElement
 * .dataset.theme, which is what the CSS ([data-theme="dark"]) keys off.
 * index.html runs the same resolution before first paint (no flash).
 */

const THEME_KEY = "unisphere.theme";

export type ThemePref = "light" | "dark" | "system";

function readPref(): ThemePref {
  try {
    const stored = localStorage.getItem(THEME_KEY);
    return stored === "light" || stored === "dark" ? stored : "system";
  } catch {
    // Storage unavailable (private mode): follow the system.
    return "system";
  }
}

function systemPrefersDark(): boolean {
  try {
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  } catch {
    return false;
  }
}

/** Writes the concrete theme onto <html>. Returns what was applied. */
function applyTheme(pref: ThemePref): "light" | "dark" {
  const concrete = pref === "system" ? (systemPrefersDark() ? "dark" : "light") : pref;
  document.documentElement.dataset.theme = concrete;
  return concrete;
}

/** Resolves a preference to the concrete theme it will render as. */
function resolve(pref: ThemePref): "light" | "dark" {
  return pref === "system" ? (systemPrefersDark() ? "dark" : "light") : pref;
}

export function useTheme() {
  const [pref, setPref] = useState<ThemePref>(readPref);
  const [effective, setEffective] = useState<"light" | "dark">(() => applyTheme(readPref()));

  // Keep <html> in sync with the preference, and follow OS changes while
  // the preference is "system" (state updates only from the OS event).
  useEffect(() => {
    applyTheme(pref);
    if (pref !== "system") return;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setEffective(applyTheme("system"));
    media.addEventListener("change", onChange);
    return () => media.removeEventListener("change", onChange);
  }, [pref]);

  /** Stores the preference ("light" | "dark" | "system") and applies it. */
  function setTheme(next: ThemePref) {
    try {
      localStorage.setItem(THEME_KEY, next);
    } catch {
      // Storage unavailable: apply for this session only.
    }
    setPref(next);
    setEffective(resolve(next));
  }

  /** Switches to the opposite concrete theme (what the sun/moon button does). */
  function toggleTheme() {
    setTheme(effective === "dark" ? "light" : "dark");
  }

  return { pref, effective, setTheme, toggleTheme };
}
