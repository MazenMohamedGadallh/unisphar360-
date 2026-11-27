import { useTheme } from "../useTheme";
import { UiIcon } from "../UiIcon";

/* Sun/moon button in the topbar. Mirrors the Razor toggle: it flips the
 * concrete light/dark theme; the preference is stored under the React-only
 * localStorage key "unisphere.theme" (see useTheme.ts). */

const ICON_BUTTON =
  "relative inline-flex min-h-11 min-w-11 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent p-2.5 text-xl text-text-secondary transition-colors hover:bg-bg hover:text-text";

export function ThemeToggle({ className }: { className?: string }) {
  const { effective, toggleTheme } = useTheme();
  return (
    <button
      type="button"
      className={className ?? ICON_BUTTON}
      title={effective === "dark" ? "Switch to light mode" : "Switch to dark mode"}
      aria-label={effective === "dark" ? "Switch to light mode" : "Switch to dark mode"}
      onClick={toggleTheme}
    >
      <UiIcon name={effective === "dark" ? "sun" : "moon"} />
    </button>
  );
}
