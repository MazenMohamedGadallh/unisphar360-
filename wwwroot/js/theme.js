// Theme handling: "light" | "dark" | "system" persisted in localStorage.
// The resolved concrete theme is mirrored to documentElement.dataset.theme,
// which is what the CSS ([data-theme="dark"]) keys off.
const KEY = "theme";

export function systemTheme() {
    try {
        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    } catch {
        return "light";
    }
}

export function getTheme() {
    try {
        const stored = localStorage.getItem(KEY);
        if (stored === "light" || stored === "dark" || stored === "system") return stored;
    } catch {
        // Private mode etc: fall through to system.
    }
    return "system";
}

export function applyTheme(pref) {
    const concrete = pref === "system" ? systemTheme() : pref;
    document.documentElement.dataset.theme = concrete;
    return concrete;
}

export function setTheme(pref) {
    if (pref !== "light" && pref !== "dark" && pref !== "system") pref = "system";
    try {
        localStorage.setItem(KEY, pref);
    } catch {
        // Storage unavailable: still apply for this session.
    }
    return applyTheme(pref);
}

export function toggleTheme() {
    const current = document.documentElement.dataset.theme === "dark" ? "dark" : "light";
    return setTheme(current === "dark" ? "light" : "dark");
}
