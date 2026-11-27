import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface StudyDay {
  id: number;
  date: string;
  dayLabel: string;
  subject: string;
  hours: number;
  isCompleted: boolean;
  isFreeDay: boolean;
}

interface StudyPlanResponse {
  weekLabel: string;
  weekStart: string;
  days: StudyDay[];
}

/* GET /api/study-planner/suggestion */
interface StudySuggestion {
  totalHours: number;
  sessions: { date: string; course: string; hours: number }[];
}

export function StudyPlanner() {
  const navigate = useNavigate();
  const [plan, setPlan] = useState<StudyPlanResponse | null>(null);
  const [suggestion, setSuggestion] = useState<StudySuggestion | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const [applyingSuggestion, setApplyingSuggestion] = useState(false);

  useEffect(() => {
    document.title = "Study Planner — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [planRes, sugRes] = await Promise.all([
        fetch(`${API_BASE_URL}/api/dashboard/study-plan`, { headers: authHeaders() }),
        fetch(`${API_BASE_URL}/api/study-planner/suggestion`, { headers: authHeaders() }),
      ]);
      if (planRes.status === 401 || sugRes.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!planRes.ok) throw new Error(`HTTP ${planRes.status}`);
      setPlan((await planRes.json()) as StudyPlanResponse);
      if (sugRes.ok) {
        setSuggestion((await sugRes.json()) as StudySuggestion);
      }
    } catch {
      setError("Could not load your weekly study planner.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function toggleDay(day: StudyDay) {
    if (day.isFreeDay) return;
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/dashboard/study-plan/${day.id}/toggle`, {
        method: "PATCH",
        headers: authHeaders(),
      });
      if (!res.ok) throw new Error();
      const updated = (await res.json()) as StudyDay;
      setPlan((prev) =>
        prev ? { ...prev, days: prev.days.map((d) => (d.id === updated.id ? updated : d)) } : prev,
      );
    } catch {
      setToast({ text: "Could not update session status.", isError: true });
    }
  }

  async function applyAiSuggestion() {
    setApplyingSuggestion(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/study-planner/suggestion/apply`, {
        method: "POST",
        headers: authHeaders(),
      });
      if (!res.ok) throw new Error();
      setToast({ text: "AI-recommended weekly study schedule applied to your planner.", isError: false });
      await load();
    } catch {
      setToast({ text: "Could not apply suggested schedule.", isError: true });
    } finally {
      setApplyingSuggestion(false);
    }
  }

  const studyDays = plan?.days.filter((d) => !d.isFreeDay) ?? [];
  const completedDays = studyDays.filter((d) => d.isCompleted).length;
  const totalHours = studyDays.reduce((sum, d) => sum + d.hours, 0);
  const completedHours = studyDays.filter((d) => d.isCompleted).reduce((sum, d) => sum + d.hours, 0);
  const progressPct = studyDays.length ? Math.round((completedDays / studyDays.length) * 100) : 0;

  const suggestionRows = (suggestion?.sessions ?? []).map((s) => ({
    day: new Date(s.date).toLocaleDateString(undefined, { weekday: "short" }),
    subject: s.course,
    hours: s.hours,
  }));

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Weekly Study Planner</h1>
          <p className="mt-1 text-sm text-text-secondary">
            {plan ? `Active week: ${plan.weekLabel} — track your daily completion.` : "Organize your weekly academic focus sessions."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/analytics">
            <UiIcon name="chart-no-axes-combined" />
            Performance Analysis
          </Button>
          <Button size="sm" to="/chat">
            <UiIcon name="sparkles" />
            Ask AI Study Coach
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !plan ? (
        <LoadingBlock label="Loading your weekly study planner…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="calendar-days" /></div>
              <span className="dash-stat-label">Planned Sessions</span>
              <span className="dash-stat-value">{studyDays.length} days</span>
            </div>
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="check-circle" /></div>
              <span className="dash-stat-label">Weekly Completion</span>
              <span className="dash-stat-value">{progressPct}%</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="trending-up" /></div>
              <span className="dash-stat-label">Hours Logged</span>
              <span className="dash-stat-value">{completedHours} / {totalHours}h</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="sparkles" /></div>
              <span className="dash-stat-label">Rest Days</span>
              <span className="dash-stat-value">{plan.days.filter((d) => d.isFreeDay).length}</span>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <Card className="overflow-hidden lg:col-span-8">
              <CardHeader
                title={`Schedule (${plan.weekLabel})`}
                action={<span className="text-xs text-text-secondary">Click check to complete or Edit to customize</span>}
              />
              {plan.days.length === 0 ? (
                <EmptyState icon="calendar-days" message="No study days scheduled for this week." />
              ) : (
                <div className="divide-y divide-border">
                  {plan.days.map((day) => {
                    return (
                      <div key={day.id} className="px-5 py-4">
                          <div className="flex flex-wrap items-center justify-between gap-3">
                            <div className="flex items-center gap-3.5">
                              {day.isFreeDay ? (
                                <span className="flex h-6 w-6 items-center justify-center rounded-md border border-dashed border-border text-text-muted">
                                  ·
                                </span>
                              ) : (
                                <button
                                  type="button"
                                  onClick={() => void toggleDay(day)}
                                  className={`dash-plan-check ${day.isCompleted ? "on" : ""}`}
                                  aria-label={`Mark ${day.dayLabel} complete`}
                                >
                                  <UiIcon name="check" />
                                </button>
                              )}
                              <div>
                                <div className="flex items-center gap-2">
                                  <span className="text-xs font-semibold uppercase tracking-wide text-primary">
                                    {day.dayLabel}
                                  </span>
                                  <span className="dash-num text-xs text-text-muted">{day.date}</span>
                                </div>
                                <div
                                  className={`mt-0.5 text-sm font-semibold ${
                                    day.isCompleted
                                      ? "line-through text-text-secondary"
                                      : day.isFreeDay
                                        ? "italic text-text-secondary"
                                        : "text-text"
                                  }`}
                                >
                                  {day.subject}
                                </div>
                              </div>
                            </div>
                            <div className="flex items-center gap-3">
                              {day.isFreeDay ? (
                                <StatusBadge status="Upcoming" />
                              ) : (
                                <>
                                  <span className="dash-num rounded-md bg-primary-light px-2.5 py-1 text-xs font-semibold text-primary">
                                    {day.hours}h
                                  </span>
                                  <StatusBadge status={day.isCompleted ? "Passed" : "In progress"} />
                                </>
                              )}
                            </div>
                          </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </Card>

            <Card className="lg:col-span-4">
              <CardHeader title="AI-Recommended Plan" />
              <div className="flex flex-col gap-4 p-5 text-sm">
                <p className="m-0 text-xs leading-relaxed text-text-secondary">
                  {"Tailored to your lowest-scoring and lowest-attendance courses so you focus extra hours where they improve your GPA most."}
                </p>

                {suggestionRows.length > 0 && (
                  <div className="flex flex-col gap-2">
                    {suggestionRows.slice(0, 7).map((row, i) => {
                      const label = row.day;
                      return (
                        <div
                          key={`${label}-${i}`}
                          className="flex items-center justify-between gap-2 rounded-control border border-border bg-bg/70 px-3 py-2 text-xs"
                        >
                          <div>
                            <span className="font-semibold text-primary">{label}:</span>{" "}
                            <span className="text-text">{row.subject}</span>
                          </div>
                          <span className="dash-num font-semibold text-text-secondary">
                            {row.hours > 0 ? `${row.hours}h` : "Rest"}
                          </span>
                        </div>
                      );
                    })}
                  </div>
                )}

                <button
                  type="button"
                  disabled={applyingSuggestion}
                  onClick={() => void applyAiSuggestion()}
                  className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold disabled:opacity-60"
                >
                  <UiIcon name="sparkles" />
                  {applyingSuggestion ? "Applying Schedule…" : "Apply Recommended Schedule"}
                </button>
              </div>
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
