import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import "./dashboard.css";

/*
 * Student Performance Analysis: React port of Pages/Analytics.razor on top of
 * GET /api/analytics/me/performance and /me/gpa-trend (Student role only).
 */

interface Factor { factor: string; weight: number; value: number; contribution: number; description: string }
interface CourseRisk { course: string; riskLevel: string; reason: string }
interface Analysis {
  riskLevel: string;
  predictedGpa: number;
  confidence: number;
  score: number;
  strengths: string[];
  weaknesses: string[];
  recommendations: string[];
  courseRisks: CourseRisk[];
  factors: Factor[];
}
interface TrendPoint { label: string; gpa: number }

const RISK: Record<string, { label: string; tone: string }> = {
  OnTrack: { label: "On Track", tone: "bg-success-light text-success" },
  NeedsAttention: { label: "Needs Attention", tone: "bg-warning-light text-warning" },
  AtRisk: { label: "At Risk", tone: "bg-danger-light text-danger" },
};

function RiskBadge({ level }: { level: string }) {
  const risk = RISK[level] ?? { label: level, tone: "border border-border bg-bg text-text-secondary" };
  return <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${risk.tone}`}>{risk.label}</span>;
}

function Bar({ label, percent, value }: { label: string; percent: number; value: string }) {
  return (
    // Inline layout: app.generated.css is prebuilt and lacks these utilities.
    <div className="items-center gap-3 text-sm" style={{ display: "grid", gridTemplateColumns: "minmax(0,10rem) 1fr auto" }}>
      <span className="truncate" title={label}>{label}</span>
      <div className="overflow-hidden rounded-full bg-bg" style={{ height: 8 }}>
        <div className="rounded-full bg-primary" style={{ height: "100%", width: `${Math.max(0, Math.min(100, percent))}%` }} />
      </div>
      <span className="dash-num text-text-secondary">{value}</span>
    </div>
  );
}

function List({ items, icon, tone, empty }: { items: string[]; icon: string; tone: string; empty: string }) {
  if (items.length === 0) return <p className="text-sm text-text-secondary">{empty}</p>;
  return (
    <ul className="m-0 flex flex-col gap-2 p-0 text-sm" style={{ listStyle: "none" }}>
      {items.map((item) => (
        <li key={item} className="flex items-start gap-2">
          <span className={tone}><UiIcon name={icon} /></span>
          {item}
        </li>
      ))}
    </ul>
  );
}

interface PlanCourse { course: string; weeklyHours: number; priority: string; difficultyPercent: number; reason: string }
interface PlanSession { date: string; course: string; hours: number }
interface StudyPlan { weekStart: string; totalHours: number; courses: PlanCourse[]; sessions: PlanSession[] }

const PRIORITY_TONE: Record<string, string> = {
  High: "bg-danger-light text-danger",
  Medium: "bg-warning-light text-warning",
  Low: "bg-success-light text-success",
};

/* Personal weekly plan from GET /api/study-planner/suggestion; "Add to my
   planner" saves it as this week's study-plan items (dashboard planner). */
function StudyPlanCard() {
  const [plan, setPlan] = useState<StudyPlan | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/study-planner/suggestion`, { headers: authHeaders() });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setPlan((await res.json()) as StudyPlan);
    } catch {
      setError("Could not build your study plan. Please try again.");
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function apply() {
    setSaving(true);
    setSaved(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/study-planner/suggestion/apply`, { method: "POST", headers: authHeaders() });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const { added } = (await res.json()) as { added: number };
      setSaved(`Added ${added} study sessions to this week's planner.`);
    } catch {
      setError("Could not save the plan to your planner. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  const days = plan
    ? Object.entries(
        plan.sessions.reduce<Record<string, PlanSession[]>>((acc, s) => {
          (acc[s.date] ??= []).push(s);
          return acc;
        }, {}),
      )
    : [];

  return (
    <Card className="mb-6 overflow-hidden">
      <CardHeader
        title={plan ? `Your study plan this week · ${plan.totalHours}h` : "Your study plan this week"}
        action={plan && plan.courses.length > 0 ? (
          <Button size="sm" onClick={() => void apply()} disabled={saving}>
            <UiIcon name="calendar-days" /> {saving ? "Saving…" : "Add to my planner"}
          </Button>
        ) : undefined}
      />
      {error ? (
        <ErrorState message={error} onRetry={() => void load()} />
      ) : plan === null ? (
        <LoadingBlock label="Building your plan…" />
      ) : plan.courses.length === 0 ? (
        <EmptyState icon="calendar-days" message="No current courses to plan for." />
      ) : (
        <div className="px-5 py-4">
          {saved && <Alert kind="success">{saved}</Alert>}
          <p className="mb-3 mt-0 text-xs text-text-secondary">
            Hours go to the courses where your grade is lowest, the course is hardest for the class, it has more credits, or your attendance is low.
          </p>
          <div className="dash-table-wrap mb-4">
            <table className="dash-table">
              <thead><tr><th>Course</th><th>Hours / week</th><th>Priority</th><th>Difficulty</th><th>Why</th></tr></thead>
              <tbody>
                {plan.courses.map((c) => (
                  <tr key={c.course}>
                    <td>{c.course}</td>
                    <td className="dash-num">{c.weeklyHours}h</td>
                    <td><span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${PRIORITY_TONE[c.priority] ?? ""}`}>{c.priority}</span></td>
                    <td className="dash-num">{c.difficultyPercent}%</td>
                    <td>{c.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="gap-3" style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(150px, 1fr))" }}>
            {days.map(([date, sessions]) => (
              <div key={date} className="rounded-control border border-border p-3 text-sm">
                <div className="mb-2 font-semibold">
                  {new Date(date).toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "short" })}
                </div>
                {sessions.map((s, i) => (
                  <div key={i} className="flex justify-between gap-2 text-text-secondary">
                    <span className="truncate" title={s.course}>{s.course}</span>
                    <span className="dash-num">{s.hours}h</span>
                  </div>
                ))}
              </div>
            ))}
          </div>
        </div>
      )}
    </Card>
  );
}

export function Analytics() {
  const navigate = useNavigate();
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [trend, setTrend] = useState<TrendPoint[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [empty, setEmpty] = useState(false);

  const load = useCallback(async (signal?: AbortSignal) => {
    setError(null);
    try {
      const [perf, gpa] = await Promise.all([
        fetch(`${API_BASE_URL}/api/analytics/me/performance`, { headers: authHeaders(), signal }),
        fetch(`${API_BASE_URL}/api/analytics/me/gpa-trend`, { headers: authHeaders(), signal }),
      ]);
      if (perf.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (perf.status === 403) {
        setError("Performance analysis is available for students only.");
        return;
      }
      if (perf.status === 404) {
        setEmpty(true);
        return;
      }
      if (!perf.ok) throw new Error(`HTTP ${perf.status}`);
      setAnalysis((await perf.json()) as Analysis);
      setTrend(gpa.ok ? ((await gpa.json()) as TrendPoint[]) : []);
    } catch (e) {
      if ((e as Error).name === "AbortError") return;
      setError("Could not load your performance analysis. Check your connection and try again.");
    }
  }, [navigate]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <div className="dashboard">
      <div className="mb-6">
        <h1 className="m-0 text-2xl font-semibold">Student Performance Analysis</h1>
        <p className="mt-1 text-sm text-text-secondary">Are you on the right track? Here's what your data says.</p>
      </div>

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void load()} /></Card>
      ) : empty ? (
        <Card><EmptyState icon="chart-no-axes-combined" message="No academic data yet. Enroll in courses to see your analysis." /></Card>
      ) : analysis === null ? (
        <LoadingBlock label="Loading your analysis…" />
      ) : (
        <>
          <div className="dash-stats">
            <div className="dash-stat gpa">
              <span className="dash-stat-icon"><UiIcon name="award" /></span>
              <span className="dash-stat-label">Performance score</span>
              <span className="dash-stat-value">{analysis.score.toFixed(0)}<small>/100</small></span>
              <RiskBadge level={analysis.riskLevel} />
            </div>
            <div className="dash-stat courses">
              <span className="dash-stat-icon"><UiIcon name="trending-up" /></span>
              <span className="dash-stat-label">Predicted GPA</span>
              <span className="dash-stat-value">{analysis.predictedGpa.toFixed(2)}</span>
            </div>
            <div className="dash-stat attendance">
              <span className="dash-stat-icon"><UiIcon name="shield-check" /></span>
              <span className="dash-stat-label">Confidence</span>
              <span className="dash-stat-value">{(analysis.confidence * 100).toFixed(0)}%</span>
            </div>
          </div>

          <StudyPlanCard />

          <div className="dash-two mb-6">
            <Card className="overflow-hidden">
              <CardHeader title="Why this result" />
              <div className="flex flex-col gap-3 px-5 py-4">
                {analysis.factors.map((f) => (
                  <div key={f.factor}>
                    <Bar label={`${f.factor} (${(f.weight * 100).toFixed(0)}%)`} percent={f.value} value={`+${f.contribution.toFixed(1)}`} />
                    <p className="mt-1 text-xs text-text-secondary">{f.description}</p>
                  </div>
                ))}
              </div>
            </Card>
            <Card className="overflow-hidden">
              <CardHeader title="GPA trend by semester" />
              <div className="flex flex-col gap-3 px-5 py-4">
                {trend.length < 2 ? (
                  <p className="text-sm text-text-secondary">Finish this semester to start your trend line.</p>
                ) : (
                  trend.map((t) => <Bar key={t.label} label={t.label} percent={(t.gpa / 4) * 100} value={t.gpa.toFixed(2)} />)
                )}
              </div>
            </Card>
          </div>

          <Card className="mb-6 overflow-hidden">
            <CardHeader title="Course risk" />
            {analysis.courseRisks.length === 0 ? (
              <EmptyState icon="book-open" message="No enrolled courses this semester." />
            ) : (
              <div className="dash-table-wrap">
                <table className="dash-table">
                  <thead><tr><th>Course</th><th>Status</th><th>Why</th></tr></thead>
                  <tbody>
                    {analysis.courseRisks.map((c) => (
                      <tr key={c.course}>
                        <td>{c.course}</td>
                        <td><RiskBadge level={c.riskLevel} /></td>
                        <td>{c.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <div className="dash-two">
            <Card className="overflow-hidden">
              <CardHeader title="Strengths & areas to improve" />
              <div className="flex flex-col gap-4 px-5 py-4">
                <List items={analysis.strengths} icon="check" tone="text-success" empty="No standout strengths yet — your first win is coming." />
                <List items={analysis.weaknesses} icon="triangle-alert" tone="text-warning" empty="Nothing flagged. Keep it up!" />
              </div>
            </Card>
            <Card className="overflow-hidden">
              <CardHeader title="Recommendations" />
              <div className="px-5 py-4">
                <List items={analysis.recommendations} icon="lightbulb" tone="text-primary" empty="No recommendations right now." />
              </div>
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
