import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { UserAvatar } from "../components/UserAvatar";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

/* View model composed from the /api/admin/*, /api/events and /api/graduation endpoints. */
interface AdminData {
  stats: {
    totalUsers: number;
    activeUsersToday: number;
    coursesCount: number;
    newRegistrations7d: number;
    averageGpa: number;
  };
  logins14Days: Array<{ date: string; count: number }>;
  users: Array<{ id: number; fullName: string; email: string; role: string; major: string; isActive: boolean; isDeleted: boolean }>;
  userTotal: number;
  universities: Array<{ id: number; name: string }>;
  doctors: Array<{ id: number; fullName: string; universityId: number | null; isActive: boolean }>;
  courses: Array<{ id: number; code: string; title: string; universityId: number | null }>;
  assignments: Array<{ id: number; courseId: number; doctorUserId: number; doctorName: string }>;
  announcements: Array<{ id: number; title: string; body: string; category: string; universityId: number | null; createdAtUtc: string }>;
  events: Array<{ id: number; title: string; location: string; startsAtUtc: string; description: string | null }>;
  graduationApplications: Array<{
    id: number;
    studentName: string;
    email: string;
    degree: string;
    gpaAtApplication: number;
    coursesCompleted: number;
    coursesRequired: number;
    status: "Pending" | "InReview" | "Approved" | "Rejected" | "Withdrawn";
    submittedAtUtc: string;
    decisionNote: string | null;
  }>;
  auditLogs: Array<{ id: number; userId: number; method: string; path: string; statusCode: number; timestamp: string; ipAddress: string }>;
  auditTotal: number;
  systemHealth: { status: string; database: string; uptimeSeconds: number; version: string };
}

const ROLE_IDS: Record<string, number> = { Student: 1, Graduate: 2, Doctor: 3, Admin: 4 };

/* The API lists only days that had logins; the chart wants all 14. */
function last14Days(rows: Array<{ date: string; count: number }>) {
  const counts = new Map(rows.map((r) => [r.date.slice(0, 10), r.count]));
  return Array.from({ length: 14 }, (_, i) => {
    const date = new Date(Date.now() - (13 - i) * 86_400_000).toISOString().slice(0, 10);
    return { date, count: counts.get(date) ?? 0 };
  });
}

type AdminTab = "analytics" | "users" | "courses" | "graduation" | "events" | "announcements" | "audit";

const fmtShortDay = (isoDate: string) => {
  const parts = isoDate.split("-");
  return parts.length === 3 ? `${parts[1]}/${parts[2]}` : isoDate;
};

const fmtDateTime = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "short", timeStyle: "short" });

export function AdminPanel() {
  const navigate = useNavigate();
  const location = useLocation();
  const [data, setData] = useState<AdminData | null>(null);
  const [error, setError] = useState<string | null>(null);

  const resolveAdminTab = (pathname: string): AdminTab => {
    if (pathname.includes("/users")) return "users";
    if (pathname.includes("/courses")) return "courses";
    if (pathname.includes("/graduation")) return "graduation";
    if (pathname.includes("/events")) return "events";
    if (pathname.includes("/announcements")) return "announcements";
    if (pathname.includes("/audit")) return "audit";
    return "analytics";
  };
  const [tab, setTab] = useState<AdminTab>(() => resolveAdminTab(location.pathname));

  useEffect(() => {
    setTab(resolveAdminTab(location.pathname));
  }, [location.pathname]);

  const [hoveredDay, setHoveredDay] = useState<{ date: string; count: number } | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const [userSearch, setUserSearch] = useState("");

  // Announcement form
  const [annTitle, setAnnTitle] = useState("");
  const [annBody, setAnnBody] = useState("");
  const [annUniversity, setAnnUniversity] = useState("");

  // Direct Notification form
  const [notifTarget, setNotifTarget] = useState("role:All");
  const [notifTitle, setNotifTitle] = useState("");
  const [notifBody, setNotifBody] = useState("");

  // Campus Event form
  const [eventTitle, setEventTitle] = useState("");
  const [eventDate, setEventDate] = useState("");
  const [eventLocation, setEventLocation] = useState("");
  const [eventUniversity, setEventUniversity] = useState("");
  const [eventDesc, setEventDesc] = useState("");

  // Graduation review notes draft
  const [reviewNotes, setReviewNotes] = useState<Record<number, string>>({});

  const load = useCallback(async () => {
    setError(null);
    try {
      const paths = [
        "admin/overview",
        `admin/users/search?pageSize=100&search=${encodeURIComponent(userSearch.trim())}`,
        "auth/universities",
        "admin/doctors",
        "admin/courses",
        "course-assignments",
        "admin/announcements",
        "events",
        "graduation/applications?pageSize=100",
        "admin/audit-log?pageSize=100",
        "admin/system-health",
      ];
      const responses = await Promise.all(
        paths.map((path) => fetch(`${API_BASE_URL}/api/${path}`, { headers: authHeaders() })),
      );
      if (responses.some((r) => r.status === 401)) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (responses[0].status === 403) {
        setError("Admin Console is restricted to administrators.");
        return;
      }
      const failed = responses.find((r) => !r.ok);
      if (failed) throw new Error(`HTTP ${failed.status}`);
      const [overview, users, universities, doctors, courses, assignments, announcements, events, graduation, audit, health] =
        await Promise.all(responses.map((r) => r.json()));
      setData({
        stats: {
          ...overview,
          totalUsers: overview.totalStudents + overview.totalDoctors + overview.totalGraduates + overview.totalAdmins,
        },
        logins14Days: last14Days(overview.loginsPerDay),
        users: users.items,
        userTotal: users.total,
        universities,
        doctors,
        courses,
        assignments,
        announcements,
        events,
        graduationApplications: graduation.items,
        auditLogs: audit.items,
        auditTotal: audit.total,
        systemHealth: health,
      });
    } catch {
      setError("Could not load Admin Console.");
    }
  }, [navigate, userSearch]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 250); // debounces the user search box
    return () => window.clearTimeout(timer);
  }, [load]);

  /** One authenticated write, then reload. Returns false (after a toast) when the API refuses. */
  async function send(method: string, path: string, body: unknown, success: string | null, failure: string) {
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/${path}`, {
        method,
        headers: { ...authHeaders(), ...(body ? { "Content-Type": "application/json" } : {}) },
        body: body ? JSON.stringify(body) : undefined,
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, failure), isError: true });
        await load();
        return false;
      }
      if (success) {
        setToast({ text: success, isError: false });
        await load();
      }
      return true;
    } catch {
      setToast({ text: failure, isError: true });
      return false;
    }
  }

  const toggleUserActive = (id: number, isActive: boolean) =>
    send(
      "PATCH",
      `admin/users/${id}/status`,
      { isActive },
      isActive ? "User reactivated." : "User deactivated.",
      "Failed to update user status.",
    );

  const updateUserRole = (userId: number, role: string) =>
    send("PATCH", `admin/users/${userId}/role`, { role }, `User role updated to ${role}.`, "Could not change user role.");

  async function assignDoctor(courseId: number, doctorUserId: number | null) {
    // Only the assignment shown in the dropdown is replaced; co-instructors stay.
    const current = data?.assignments.find((a) => a.courseId === courseId);
    if (doctorUserId) {
      // Add first so a refused change leaves the current doctor in place.
      const added = await send("POST", "course-assignments", { courseId, doctorUserId }, null, "Failed to assign doctor.");
      if (!added) return;
    }
    if (current) {
      await send("DELETE", `course-assignments/${current.id}`, null, "Course doctor assignment saved.", "Failed to unassign doctor.");
    } else {
      setToast({ text: "Course doctor assignment saved.", isError: false });
      await load();
    }
  }

  async function postAnnouncement(e: FormEvent) {
    e.preventDefault();
    const ok = await send(
      "POST",
      "admin/announcements",
      { title: annTitle, body: annBody, universityId: Number(annUniversity) || null },
      "Announcement published.",
      "Failed to post announcement.",
    );
    if (ok) {
      setAnnTitle("");
      setAnnBody("");
    }
  }

  async function sendDirectNotification(e: FormEvent) {
    e.preventDefault();
    const message = { title: notifTitle, body: notifBody };
    const targets = notifTarget.startsWith("user:")
      ? [{ userId: Number(notifTarget.slice(5)) }]
      : notifTarget === "role:All"
        ? Object.values(ROLE_IDS).map((roleId) => ({ roleId }))
        : [{ roleId: ROLE_IDS[notifTarget.slice(5)] }];
    for (const target of targets) {
      const ok = await send("POST", "admin/notifications", { ...message, ...target }, null, "Failed to send notification.");
      if (!ok) return;
    }
    setNotifTitle("");
    setNotifBody("");
    setToast({ text: "Notification sent.", isError: false });
  }

  async function createEvent(e: FormEvent) {
    e.preventDefault();
    const ok = await send(
      "POST",
      "events",
      {
        title: eventTitle,
        location: eventLocation,
        startsAtUtc: new Date(eventDate).toISOString(),
        description: eventDesc,
        universityId: Number(eventUniversity) || null,
      },
      "Campus event published.",
      "Could not create event.",
    );
    if (ok) {
      setEventTitle("");
      setEventDate("");
      setEventLocation("");
      setEventDesc("");
    }
  }

  const deleteEvent = (id: number) => send("DELETE", `events/${id}`, null, "Campus event deleted.", "Could not delete event.");

  const reviewGraduation = (appId: number, decision: "Approved" | "Rejected") =>
    send(
      "POST",
      `graduation/applications/${appId}/review`,
      { approve: decision === "Approved", note: reviewNotes[appId] ?? "" },
      `Graduation application #${appId} marked as ${decision}.`,
      "Could not submit graduation review.",
    );

  const maxLogins = Math.max(1, ...(data?.logins14Days.map((d) => d.count) ?? [1]));

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Admin Console</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Platform analytics, user lifecycle management, course assignments, graduation reviews, campus events, announcements, and audit trails.
          </p>
        </div>
        <div className="flex flex-wrap rounded-control border border-border bg-card p-1">
          {(
            [
              ["analytics", "Overview", "/admin"],
              ["users", "Users", "/admin/users"],
              ["courses", "Courses", "/admin/courses"],
              ["graduation", "Graduation", "/admin/graduation"],
              ["events", "Events", "/admin/events"],
              ["announcements", "Broadcasts", "/admin/announcements"],
              ["audit", "Audit Logs", "/admin/audit"],
            ] as Array<[AdminTab, string, string]>
          ).map(([key, label, path]) => (
            <button
              key={key}
              type="button"
              onClick={() => {
                setTab(key);
                navigate(path);
              }}
              className={`cursor-pointer rounded-md border-0 px-3 py-1.5 text-xs font-semibold transition ${
                tab === key ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void load()} /></Card>
      ) : !data ? (
        <LoadingBlock label="Loading Admin Console…" />
      ) : (
        <>
          {/* KPI Strip */}
          <div className="grid grid-cols-2 gap-4 mb-6 sm:grid-cols-3 lg:grid-cols-6">
            {[
              { label: "Total Users", value: data.stats.totalUsers, tone: "gpa" },
              { label: "Active Today", value: data.stats.activeUsersToday, tone: "courses" },
              { label: "Courses", value: data.stats.coursesCount, tone: "attendance" },
              { label: "New This Week", value: data.stats.newRegistrations7d, tone: "recommended" },
              { label: "Average GPA", value: data.stats.averageGpa.toFixed(2), tone: "courses" },
              { label: "System Status", value: data.systemHealth.status, tone: "attendance" },
            ].map((kpi) => (
              <div key={kpi.label} className={`dash-stat ${kpi.tone}`}>
                <span className="dash-stat-label block truncate">{kpi.label}</span>
                <span className="dash-stat-value text-xl">{kpi.value}</span>
              </div>
            ))}
          </div>

          {tab === "analytics" && (
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
              {/* 14-Day Logins Bar Chart — always all 14 days, capped bar max-width, hover tooltips */}
              <Card className="lg:col-span-8">
                <CardHeader
                  title="Daily Logins — Last 14 Days"
                  action={
                    <span className="dash-num text-xs font-medium text-primary">
                      {hoveredDay ? `${hoveredDay.date}: ${hoveredDay.count} logins` : "Hover any bar for exact count"}
                    </span>
                  }
                />
                <div className="p-5">
                  <div className="grid h-56 grid-cols-14 items-end gap-1.5 border-b border-border pb-2">
                    {data.logins14Days.map((day) => {
                      const heightPct = Math.max(6, Math.round((day.count / maxLogins) * 100));
                      const active = hoveredDay?.date === day.date;
                      return (
                        <div
                          key={day.date}
                          className="group relative flex h-full flex-col items-center justify-end"
                          onMouseEnter={() => setHoveredDay(day)}
                          onMouseLeave={() => setHoveredDay(null)}
                        >
                          <div
                            className={`pointer-events-none mb-1.5 rounded bg-text px-1.5 py-0.5 text-[10px] font-semibold text-bg transition-opacity ${
                              active ? "opacity-100" : "opacity-0"
                            }`}
                          >
                            {day.count}
                          </div>
                          <div
                            style={{ height: `${heightPct}%` }}
                            className="w-full max-w-[28px] rounded-t-md bg-gradient-to-t from-primary to-accent transition-transform duration-150 group-hover:scale-y-105"
                            title={`${day.date}: ${day.count} logins`}
                          />
                        </div>
                      );
                    })}
                  </div>
                  <div className="mt-2 grid grid-cols-14 gap-1 text-center">
                    {data.logins14Days.map((day) => (
                      <span key={day.date} className="dash-num truncate text-[10px] text-text-secondary">
                        {fmtShortDay(day.date)}
                      </span>
                    ))}
                  </div>
                </div>
              </Card>

              {/* System Health */}
              <Card className="lg:col-span-4">
                <CardHeader title="System Health" />
                <div className="flex flex-col gap-3 p-5 text-sm">
                  <div className="flex items-center justify-between border-b border-border pb-2.5">
                    <span className="text-text-secondary">Service Status</span>
                    <StatusBadge status={data.systemHealth.status === "Healthy" ? "Excellent" : "Needs Attention"} />
                  </div>
                  <div className="flex items-center justify-between border-b border-border pb-2.5">
                    <span className="text-text-secondary">Uptime</span>
                    <span className="dash-num font-semibold text-text">
                      {Math.floor(data.systemHealth.uptimeSeconds / 60)}m {Math.floor(data.systemHealth.uptimeSeconds % 60)}s
                    </span>
                  </div>
                  <div className="flex items-center justify-between border-b border-border pb-2.5">
                    <span className="text-text-secondary">Database</span>
                    <span className="dash-num font-semibold text-primary">{data.systemHealth.database}</span>
                  </div>
                  <div>
                    <div className="text-xs text-text-secondary">API Version</div>
                    <div className="mt-0.5 text-xs font-medium text-text">{data.systemHealth.version}</div>
                  </div>
                </div>
              </Card>
            </div>
          )}

          {tab === "users" && (
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
              <Card className="overflow-hidden lg:col-span-12">
                <CardHeader
                  title={`Platform Users (showing ${data.users.length} of ${data.userTotal})`}
                  action={
                    <input
                      type="search"
                      aria-label="Search users"
                      placeholder="Search name or email…"
                      value={userSearch}
                      onChange={(e) => setUserSearch(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3 py-1.5 text-xs text-text outline-none focus:border-primary"
                    />
                  }
                />
                <div className="dash-table-wrap">
                  <table className="dash-table">
                    <thead>
                      <tr>
                        <th>User</th>
                        <th>Role</th>
                        <th>Major</th>
                        <th>Status</th>
                        <th />
                      </tr>
                    </thead>
                    <tbody>
                      {data.users.map((u) => (
                        <tr key={u.id}>
                          <td>
                            <div className="flex items-center gap-2.5">
                              <UserAvatar fullName={u.fullName} size={32} />
                              <div>
                                <div className="font-medium text-text">{u.fullName}</div>
                                <div className="text-xs text-text-secondary">{u.email}</div>
                              </div>
                            </div>
                          </td>
                          <td>
                            <select
                              aria-label={`Role for ${u.fullName}`}
                              value={u.role}
                              onChange={(e) => void updateUserRole(u.id, e.target.value)}
                              className="rounded-control border border-border bg-bg px-2 py-1 text-xs font-semibold text-primary outline-none focus:border-primary"
                            >
                              <option value="Student">Student</option>
                              <option value="Graduate">Graduate</option>
                              <option value="Doctor">Doctor</option>
                              <option value="Admin">Admin</option>
                            </select>
                          </td>
                          <td className="text-xs text-text-secondary">{u.major}</td>
                          <td>
                            <StatusBadge status={u.isActive && !u.isDeleted ? "Passed" : "Locked"} />
                          </td>
                          <td className="text-right">
                            <Button
                              size="sm"
                              variant={u.isActive ? "secondary" : "primary"}
                              disabled={u.isDeleted}
                              onClick={() => void toggleUserActive(u.id, !u.isActive)}
                            >
                              {u.isDeleted ? "Deleted" : u.isActive ? "Deactivate" : "Restore"}
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Card>
            </div>
          )}

          {tab === "courses" && (
            <Card className="overflow-hidden">
              <CardHeader title={`Course-Doctor Assignments (${data.courses.length})`} />
              <div className="dash-table-wrap">
                <table className="dash-table">
                  <thead>
                    <tr>
                      <th>Code</th>
                      <th>Course</th>
                      <th>University</th>
                      <th>Assigned Doctor</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.courses.map((c) => (
                      <tr key={c.id}>
                        <td className="font-semibold text-primary">{c.code}</td>
                        <td className="font-medium text-text">{c.title}</td>
                        <td className="text-text-secondary">{data.universities.find((u) => u.id === c.universityId)?.name ?? "—"}</td>
                        <td>
                          <select
                            aria-label={`Doctor for ${c.code}`}
                            value={data.assignments.find((a) => a.courseId === c.id)?.doctorUserId ?? ""}
                            onChange={(e) => void assignDoctor(c.id, e.target.value ? Number(e.target.value) : null)}
                            className="rounded-control border border-border bg-bg px-2.5 py-1.5 text-xs text-text outline-none focus:border-primary"
                          >
                            <option value="">Unassigned</option>
                            {data.doctors
                              .filter((d) => d.isActive && (!c.universityId || d.universityId === c.universityId))
                              .map((d) => (
                              <option key={d.id} value={d.id}>
                                {d.fullName}
                              </option>
                            ))}
                          </select>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}

          {tab === "graduation" && (
            <Card className="overflow-hidden">
              <CardHeader title={`Graduation Applications (${data.graduationApplications.length})`} />
              {data.graduationApplications.length === 0 ? (
                <EmptyState
                  icon="graduation-cap"
                  message="No students have submitted graduation applications yet."
                />
              ) : (
                <div className="divide-y divide-border">
                  {data.graduationApplications.map((ga) => (
                    <div key={ga.id} className="flex flex-col gap-3 px-5 py-4 text-sm">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-text">{ga.studentName}</span>
                            <StatusBadge
                              status={
                                ga.status === "Approved"
                                  ? "Passed"
                                  : ga.status === "Pending" || ga.status === "InReview"
                                    ? "In progress"
                                    : "Needs Attention"
                              }
                            />
                          </div>
                          <div className="mt-1 text-xs text-text-secondary">
                            {ga.email} · {ga.degree} · GPA{" "}
                            <strong className="dash-num text-text">{ga.gpaAtApplication.toFixed(2)}</strong> ·{" "}
                            {ga.coursesCompleted}/{ga.coursesRequired} courses · Applied {fmtDateTime(ga.submittedAtUtc)}
                          </div>
                          {ga.decisionNote && (
                            <p className="m-0 mt-1 text-xs text-text">
                              <strong>Review note:</strong> {ga.decisionNote}
                            </p>
                          )}
                        </div>
                      </div>

                      {(ga.status === "Pending" || ga.status === "InReview") && (
                        <div className="flex flex-wrap items-center gap-2">
                          <input
                            type="text"
                            value={reviewNotes[ga.id] ?? ""}
                            onChange={(e) => setReviewNotes((prev) => ({ ...prev, [ga.id]: e.target.value }))}
                            placeholder="Add optional approval congratulations or rejection feedback…"
                            className="min-w-[240px] flex-1 rounded-control border border-border bg-bg px-3 py-1.5 text-xs text-text outline-none focus:border-primary"
                          />
                          <Button size="sm" onClick={() => void reviewGraduation(ga.id, "Approved")}>
                            Approve
                          </Button>
                          <Button size="sm" variant="secondary" onClick={() => void reviewGraduation(ga.id, "Rejected")}>
                            Reject
                          </Button>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </Card>
          )}

          {tab === "events" && (
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
              <Card className="lg:col-span-5">
                <CardHeader title="Create Campus Event" />
                <form onSubmit={(e) => void createEvent(e)} className="flex flex-col gap-3 p-5 text-sm">
                  <input
                    type="text"
                    required
                    placeholder="Event title (e.g., Spring Hackathon & Career Mixer)"
                    value={eventTitle}
                    onChange={(e) => setEventTitle(e.target.value)}
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                  <input
                    type="datetime-local"
                    required
                    aria-label="Date and time"
                    value={eventDate}
                    onChange={(e) => setEventDate(e.target.value)}
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                  <input
                    type="text"
                    required
                    placeholder="Location (e.g., Main Auditorium / Online)"
                    value={eventLocation}
                    onChange={(e) => setEventLocation(e.target.value)}
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                  <select
                    required
                    aria-label="University"
                    value={eventUniversity}
                    onChange={(e) => setEventUniversity(e.target.value)}
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  >
                    <option value="">Select university…</option>
                    {data.universities.map((u) => (
                      <option key={u.id} value={u.id}>{u.name}</option>
                    ))}
                  </select>
                  <textarea
                    rows={3}
                    placeholder="Event description and registration guidance…"
                    value={eventDesc}
                    onChange={(e) => setEventDesc(e.target.value)}
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                  <button
                    type="submit"
                    className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold"
                  >
                    <UiIcon name="calendar-days" />
                    Publish Event
                  </button>
                </form>
              </Card>

              <Card className="overflow-hidden lg:col-span-7">
                <CardHeader title={`Live Campus Events (${data.events.length})`} />
                {data.events.length === 0 ? (
                  <EmptyState icon="calendar-days" message="No campus events scheduled." />
                ) : (
                  <div className="divide-y divide-border">
                    {data.events.map((ev) => (
                      <div key={ev.id} className="flex items-start justify-between gap-4 px-5 py-4 text-sm">
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-text">{ev.title}</span>
                          </div>
                          <div className="mt-1 text-xs text-text-secondary">
                            {fmtDateTime(ev.startsAtUtc)} · {ev.location}
                          </div>
                          <p className="m-0 mt-1 text-xs text-text-secondary">{ev.description}</p>
                        </div>
                        <Button size="sm" variant="secondary" onClick={() => void deleteEvent(ev.id)}>
                          Delete
                        </Button>
                      </div>
                    ))}
                  </div>
                )}
              </Card>
            </div>
          )}

          {tab === "announcements" && (
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
              <div className="flex flex-col gap-6 lg:col-span-5">
                <Card>
                  <CardHeader title="Broadcast Announcement" />
                  <form onSubmit={(e) => void postAnnouncement(e)} className="flex flex-col gap-3.5 p-5 text-sm">
                    <input
                      type="text"
                      required
                      placeholder="Announcement title"
                      value={annTitle}
                      onChange={(e) => setAnnTitle(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                    <select
                      required
                      aria-label="University"
                      value={annUniversity}
                      onChange={(e) => setAnnUniversity(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      <option value="">Select university…</option>
                      {data.universities.map((u) => (
                        <option key={u.id} value={u.id}>{u.name}</option>
                      ))}
                    </select>
                    <textarea
                      rows={3}
                      required
                      placeholder="Write the campus-wide message…"
                      value={annBody}
                      onChange={(e) => setAnnBody(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                    <button
                      type="submit"
                      className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold"
                    >
                      <UiIcon name="bell" />
                      Publish Announcement
                    </button>
                  </form>
                </Card>

                <Card>
                  <CardHeader title="Send Direct Notification" />
                  <form onSubmit={(e) => void sendDirectNotification(e)} className="flex flex-col gap-3 p-5 text-sm">
                    <select
                      value={notifTarget}
                      onChange={(e) => setNotifTarget(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      <optgroup label="By Role">
                        <option value="role:All">All Active Users</option>
                        <option value="role:Student">All Students</option>
                        <option value="role:Graduate">All Graduates</option>
                        <option value="role:Doctor">All Doctors</option>
                      </optgroup>
                      <optgroup label="Specific User">
                        {data.users.filter((u) => u.isActive && !u.isDeleted).map((u) => (
                          <option key={u.id} value={`user:${u.id}`}>
                            {u.fullName} ({u.role})
                          </option>
                        ))}
                      </optgroup>
                    </select>
                    <input
                      type="text"
                      required
                      placeholder="Notification subject"
                      value={notifTitle}
                      onChange={(e) => setNotifTitle(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                    <textarea
                      rows={3}
                      required
                      placeholder="Message delivered directly to recipient inbox…"
                      value={notifBody}
                      onChange={(e) => setNotifBody(e.target.value)}
                      className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                    <Button type="submit" size="sm">
                      <UiIcon name="send" />
                      Send Notification
                    </Button>
                  </form>
                </Card>
              </div>

              <Card className="overflow-hidden lg:col-span-7">
                <CardHeader title={`Active Announcements (${data.announcements.length})`} />
                {data.announcements.length === 0 ? (
                  <EmptyState icon="bell" message="No announcements posted." />
                ) : (
                  <div className="divide-y divide-border">
                    {data.announcements.map((a) => (
                      <div key={a.id} className="flex items-start justify-between gap-4 px-5 py-4 text-sm">
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-text">{a.title}</span>
                            <span className="rounded bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                              {a.category}
                            </span>
                          </div>
                          <p className="m-0 mt-1 text-text-secondary">{a.body}</p>
                          <div className="mt-1 text-[11px] text-text-muted">
                            {data.universities.find((u) => u.id === a.universityId)?.name ?? "All"} · {fmtDateTime(a.createdAtUtc)}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Card>
            </div>
          )}

          {tab === "audit" && (
            <Card className="overflow-hidden">
              <CardHeader title={`Audit Log (latest ${data.auditLogs.length} of ${data.auditTotal})`} />
              <div className="dash-table-wrap">
                <table className="dash-table">
                  <thead>
                    <tr>
                      <th>Time</th>
                      <th>Actor</th>
                      <th>Action</th>
                      <th>Detail</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.auditLogs.map((log) => (
                      <tr key={log.id}>
                        <td className="dash-num whitespace-nowrap text-xs text-text-secondary">
                          {fmtDateTime(log.timestamp)}
                        </td>
                        <td className="text-xs">
                          <div className="font-medium text-text">User #{log.userId}</div>
                          <div className="text-text-secondary">{log.ipAddress}</div>
                        </td>
                        <td>
                          <span className="rounded bg-primary-light px-2 py-0.5 font-mono text-[11px] font-semibold text-primary">
                            {log.method}
                          </span>
                        </td>
                        <td className="text-sm text-text">{log.path} · {log.statusCode}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
