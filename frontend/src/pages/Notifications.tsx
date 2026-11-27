import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, notifyNotificationsUpdated } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface NotificationRow {
  id: number;
  title: string;
  body: string;
  isRead: boolean;
  createdAtUtc: string;
}

interface AnnouncementRow {
  id: number;
  title: string;
  body: string;
  category: string;
  courseName: string | null;
  createdAtUtc: string;
}

const fmt = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

export function Notifications() {
  const navigate = useNavigate();
  const [items, setItems] = useState<NotificationRow[] | null>(null);
  const [announcements, setAnnouncements] = useState<AnnouncementRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [nRes, aRes] = await Promise.all([
        fetch(`${API_BASE_URL}/api/notifications`, { headers: authHeaders() }),
        fetch(`${API_BASE_URL}/api/announcements`, { headers: authHeaders() }),
      ]);
      if (nRes.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!nRes.ok) throw new Error(`HTTP ${nRes.status}`);
      setItems((await nRes.json()) as NotificationRow[]);
      if (aRes.ok) setAnnouncements((await aRes.json()) as AnnouncementRow[]);
    } catch {
      setError("Could not load notifications.");
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function markRead(id: number) {
    await fetch(`${API_BASE_URL}/api/notifications/${id}/read`, {
      method: "POST",
      headers: authHeaders(),
    });
    setItems((prev) => prev?.map((n) => (n.id === id ? { ...n, isRead: true } : n)) ?? null);
    notifyNotificationsUpdated();
  }

  async function markAllRead() {
    await fetch(`${API_BASE_URL}/api/notifications/read-all`, {
      method: "POST",
      headers: authHeaders(),
    });
    setItems((prev) => prev?.map((n) => ({ ...n, isRead: true })) ?? null);
    notifyNotificationsUpdated();
  }

  async function deleteNotification(id: number) {
    const res = await fetch(`${API_BASE_URL}/api/notifications/${id}`, {
      method: "DELETE",
      headers: authHeaders(),
    });
    if (res.ok) {
      setItems((prev) => prev?.filter((n) => n.id !== id) ?? null);
      notifyNotificationsUpdated();
    }
  }

  const unreadCount = items?.filter((n) => !n.isRead).length ?? 0;

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Notifications &amp; Announcements</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Real-time updates for published exams, course materials, grading, and campus-wide announcements.
          </p>
        </div>
        {unreadCount > 0 && (
          <Button size="sm" variant="secondary" onClick={() => void markAllRead()}>
            <UiIcon name="check" />
            Mark all as read ({unreadCount})
          </Button>
        )}
      </div>

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void load()} /></Card>
      ) : items === null ? (
        <LoadingBlock label="Loading notifications…" />
      ) : (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          <Card className="overflow-hidden lg:col-span-7">
            <CardHeader title={`Your Inbox (${items.length})`} />
            {items.length === 0 ? (
              <EmptyState icon="bell" message="You're all caught up — no notifications yet." />
            ) : (
              <div className="divide-y divide-border">
                {items.map((n) => (
                  <div
                    key={n.id}
                    className={`flex items-start justify-between gap-4 px-5 py-4 text-sm ${
                      n.isRead ? "opacity-80" : "bg-primary-light/30"
                    }`}
                  >
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        {!n.isRead && <span className="h-2 w-2 shrink-0 rounded-full bg-primary" />}
                        <span className="font-semibold text-text">{n.title}</span>
                      </div>
                      <p className="m-0 mt-1 text-text-secondary">{n.body}</p>
                      <div className="mt-1 text-[11px] text-text-muted">{fmt(n.createdAtUtc)}</div>
                    </div>
                    <div className="flex shrink-0 items-center gap-1.5">
                      {!n.isRead && (
                        <Button size="sm" variant="ghost" onClick={() => void markRead(n.id)}>
                          Mark read
                        </Button>
                      )}
                      <Button
                        size="sm"
                        variant="ghost"
                        aria-label="Delete notification"
                        onClick={() => void deleteNotification(n.id)}
                      >
                        <UiIcon name="trash-2" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>

          <Card className="overflow-hidden lg:col-span-5">
            <CardHeader title="Campus Announcements" />
            {announcements.length === 0 ? (
              <EmptyState icon="sparkles" message="No campus announcements right now." />
            ) : (
              <div className="divide-y divide-border">
                {announcements.map((a) => (
                  <div key={a.id} className="px-5 py-4 text-sm">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-text">{a.title}</span>
                      <span className="rounded bg-primary-light px-2 py-0.5 text-[10px] font-semibold text-primary">
                        {a.category}
                      </span>
                    </div>
                    <p className="m-0 mt-1 text-text-secondary">{a.body}</p>
                    <div className="mt-1 text-[11px] text-text-muted">
                      {a.courseName ? `${a.courseName} · ` : ""}{fmt(a.createdAtUtc)}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
