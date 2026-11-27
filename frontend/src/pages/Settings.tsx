import { useState } from "react";
import { Alert, Button, Card, CardHeader } from "../components/ui";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

function getTheme(): "light" | "dark" {
  try {
    return document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
  } catch {
    return "light";
  }
}

export function Settings() {
  const [theme, setTheme] = useState<"light" | "dark">(getTheme);
  const [sidebarCollapsed, setSidebarCollapsed] = useState<boolean>(() => {
    try {
      return localStorage.getItem("unisphere.sidebar.collapsed") === "1";
    } catch {
      return false;
    }
  });
  const [quickLinksOpen, setQuickLinksOpen] = useState<boolean>(() => {
    try {
      return localStorage.getItem("unisphere.quicklinks.open") !== "0";
    } catch {
      return true;
    }
  });
  const [savedNotice, setSavedNotice] = useState<string | null>(null);

  function applyTheme(next: "light" | "dark") {
    setTheme(next);
    document.documentElement.setAttribute("data-theme", next);
    try {
      localStorage.setItem("unisphere.theme", next);
    } catch {
      // ignore
    }
    setSavedNotice(`Switched to ${next === "dark" ? "Midnight Indigo Dark" : "Blue-Tinted Lavender Light"} theme.`);
  }

  function toggleSidebarPref(collapsed: boolean) {
    setSidebarCollapsed(collapsed);
    try {
      localStorage.setItem("unisphere.sidebar.collapsed", collapsed ? "1" : "0");
    } catch {
      // ignore
    }
    setSavedNotice("Sidebar preference saved (applies on next reload or immediately via the topbar toggle).");
  }

  function toggleQuickLinksPref(open: boolean) {
    setQuickLinksOpen(open);
    try {
      localStorage.setItem("unisphere.quicklinks.open", open ? "1" : "0");
    } catch {
      // ignore
    }
    setSavedNotice("Quick Links preference saved.");
  }

  return (
    <div className="dashboard">
      <div className="mb-6">
        <h1 className="m-0 text-2xl font-semibold text-text">Workspace Settings</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Customize your Blue-Tinted Lavender appearance, sidebar layout defaults, and account security.
        </p>
      </div>

      {savedNotice && <Alert kind="success">{savedNotice}</Alert>}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader title="Appearance & Theme" />
          <div className="flex flex-col gap-4 p-5 text-sm">
            <p className="m-0 text-text-secondary">
              Choose between the airy Blue-Tinted Lavender canvas and Midnight Indigo Dark Mode. Your preference is stored in your browser.
            </p>
            <div className="grid grid-cols-2 gap-3">
              <button
                type="button"
                onClick={() => applyTheme("light")}
                className={`flex cursor-pointer flex-col items-start gap-2 rounded-card border p-4 text-left transition ${
                  theme === "light"
                    ? "border-primary bg-primary-light/60"
                    : "border-border bg-bg hover:border-primary/50"
                }`}
              >
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-card text-primary">
                  <UiIcon name="sun" />
                </span>
                <span className="font-semibold text-text">Lavender Light</span>
                <span className="text-xs text-text-secondary">Soft #F3F4FF canvas &amp; deep indigo ink</span>
              </button>
              <button
                type="button"
                onClick={() => applyTheme("dark")}
                className={`flex cursor-pointer flex-col items-start gap-2 rounded-card border p-4 text-left transition ${
                  theme === "dark"
                    ? "border-primary bg-primary-light/60"
                    : "border-border bg-bg hover:border-primary/50"
                }`}
              >
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-card text-primary">
                  <UiIcon name="moon" />
                </span>
                <span className="font-semibold text-text">Midnight Dark</span>
                <span className="text-xs text-text-secondary">Deep #0B0D1E canvas &amp; glowing periwinkle</span>
              </button>
            </div>
          </div>
        </Card>

        <Card>
          <CardHeader title="Navigation & Account" />
          <div className="flex flex-col gap-4 p-5 text-sm">
            <label className="flex cursor-pointer items-center justify-between gap-3 rounded-control border border-border bg-bg/60 px-4 py-3">
              <div>
                <div className="font-medium text-text">Start with Sidebar Collapsed</div>
                <div className="text-xs text-text-secondary">Maximize horizontal space for exam grading &amp; tables</div>
              </div>
              <input
                type="checkbox"
                checked={sidebarCollapsed}
                onChange={(e) => toggleSidebarPref(e.target.checked)}
              />
            </label>

            <label className="flex cursor-pointer items-center justify-between gap-3 rounded-control border border-border bg-bg/60 px-4 py-3">
              <div>
                <div className="font-medium text-text">Expand Quick Links by Default</div>
                <div className="text-xs text-text-secondary">Keep Profile, Notifications, Settings &amp; Help expanded</div>
              </div>
              <input
                type="checkbox"
                checked={quickLinksOpen}
                onChange={(e) => toggleQuickLinksPref(e.target.checked)}
              />
            </label>

            <div className="flex flex-wrap gap-2 pt-2">
              <Button size="sm" variant="secondary" to="/profile">
                <UiIcon name="user" />
                Edit Profile &amp; Circular Avatar
              </Button>
              <Button size="sm" variant="secondary" to="/notifications">
                <UiIcon name="bell" />
                Notification Inbox
              </Button>
            </div>
          </div>
        </Card>
      </div>
    </div>
  );
}
