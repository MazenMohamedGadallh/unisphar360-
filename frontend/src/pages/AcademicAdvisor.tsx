import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface DashboardSummary {
  fullName: string;
  currentGpa: number;
  gpaDeltaVsLastSemester: number;
  completedCourses: number;
  totalCourses: number;
  completionPercent: number;
  attendanceRate: number;
  subjectsToImprove: number;
}

interface PerformanceRow {
  courseId: number;
  courseName: string;
  gradeLetter: string;
  gradePoints: number;
  attendancePercent: number;
  status: string;
}

interface AdvisorInsight {
  type: "warning" | "progress" | "recommendation";
  title: string;
  message: string;
  actionLabel: string;
  actionRoute: string;
}

interface GpaPoint {
  label: string;
  gpa: number;
}

export function AcademicAdvisor() {
  const navigate = useNavigate();
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [performance, setPerformance] = useState<PerformanceRow[]>([]);
  const [insights, setInsights] = useState<AdvisorInsight[]>([]);
  const [trend, setTrend] = useState<GpaPoint[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    document.title = "Academic Advisor — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [sumRes, perfRes, insRes, trendRes] = await Promise.all([
        fetch(`${API_BASE_URL}/api/dashboard/summary`, { headers: authHeaders() }),
        fetch(`${API_BASE_URL}/api/dashboard/performance`, { headers: authHeaders() }),
        fetch(`${API_BASE_URL}/api/dashboard/insights`, { headers: authHeaders() }),
        fetch(`${API_BASE_URL}/api/analytics/me/gpa-trend`, { headers: authHeaders() }),
      ]);
      if ([sumRes, perfRes, insRes].some((r) => r.status === 401)) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!sumRes.ok || !perfRes.ok) throw new Error("Failed to load academic data.");
      setSummary((await sumRes.json()) as DashboardSummary);
      setPerformance((await perfRes.json()) as PerformanceRow[]);
      if (insRes.ok) setInsights((await insRes.json()) as AdvisorInsight[]);
      if (trendRes.ok) {
        const raw = (await trendRes.json()) as GpaPoint[] | { points?: GpaPoint[] };
        setTrend(Array.isArray(raw) ? raw : raw.points ?? []);
      }
    } catch {
      setError("Could not load Academic Advisor data.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  const atRisk = performance.filter((p) => p.gradePoints < 2.8 || p.attendancePercent < 82);
  const standing =
    (summary?.currentGpa ?? 0) >= 3.5
      ? "Dean's Honors List"
      : (summary?.currentGpa ?? 0) >= 3.0
        ? "Good Academic Standing"
        : "Advisor Follow-Up Recommended";

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Academic Advisor &amp; Grades</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Full transcript snapshot, course-by-course GPA standing, attendance alerts, and personalized academic recommendations.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/study-planner">
            <UiIcon name="calendar-days" />
            Adjust Study Plan
          </Button>
          <Button size="sm" to="/chat">
            <UiIcon name="sparkles" />
            Ask AI Advisor
          </Button>
        </div>
      </div>

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !summary ? (
        <LoadingBlock label="Loading your academic record…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="award" /></div>
              <span className="dash-stat-label">Cumulative GPA</span>
              <span className="dash-stat-value">{summary.currentGpa.toFixed(2)}</span>
              <span className="mt-1 text-xs font-medium text-success">
                +{summary.gpaDeltaVsLastSemester.toFixed(2)} vs last term
              </span>
            </div>
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="graduation-cap" /></div>
              <span className="dash-stat-label">Degree Progress</span>
              <span className="dash-stat-value">{summary.completionPercent}%</span>
              <span className="mt-1 text-xs text-text-secondary">
                {summary.completedCourses} of {summary.totalCourses} courses
              </span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="check-circle" /></div>
              <span className="dash-stat-label">Avg Attendance</span>
              <span className="dash-stat-value">{summary.attendanceRate}%</span>
              <span className="mt-1 text-xs text-text-secondary">{standing}</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="triangle-alert" /></div>
              <span className="dash-stat-label">Courses to Watch</span>
              <span className="dash-stat-value">{atRisk.length}</span>
              <span className="mt-1 text-xs text-text-secondary">
                {atRisk.length === 0 ? "All courses on track" : "Extra focus advised"}
              </span>
            </div>
          </div>

          <div className="mb-6 grid grid-cols-1 gap-6 lg:grid-cols-12">
            <Card className="overflow-hidden lg:col-span-8">
              <CardHeader
                title={`Current Semester Grades (${performance.length})`}
                action={
                  <Button size="sm" variant="secondary" to="/courses">
                    Manage Enrollments
                  </Button>
                }
              />
              {performance.length === 0 ? (
                <EmptyState icon="book-open" message="No enrolled courses found yet." />
              ) : (
                <div className="dash-table-wrap">
                  <table className="dash-table">
                    <thead>
                      <tr>
                        <th>Course</th>
                        <th>Grade</th>
                        <th>Points</th>
                        <th>Attendance</th>
                        <th>Standing</th>
                      </tr>
                    </thead>
                    <tbody>
                      {performance.map((row) => (
                        <tr key={row.courseId}>
                          <td className="font-medium text-text">{row.courseName}</td>
                          <td>
                            <span className="rounded-md bg-primary-light px-2.5 py-0.5 text-xs font-bold text-primary">
                              {row.gradeLetter}
                            </span>
                          </td>
                          <td className="dash-num font-semibold text-text">{row.gradePoints.toFixed(2)}</td>
                          <td className="dash-num text-text-secondary">{row.attendancePercent}%</td>
                          <td>
                            <StatusBadge status={row.status} />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>

            <Card className="lg:col-span-4">
              <CardHeader title="Advisor Recommendations" />
              <div className="flex flex-col gap-3 p-5">
                {insights.length === 0 ? (
                  <EmptyState icon="lightbulb" message="No advisor alerts right now." />
                ) : (
                  insights.map((ins, idx) => (
                    <div key={idx} className="rounded-control border border-border bg-bg/70 p-4 text-sm">
                      <div className="flex items-center gap-2 font-semibold text-text">
                        <span className="text-primary">
                          <UiIcon
                            name={
                              ins.type === "warning"
                                ? "triangle-alert"
                                : ins.type === "progress"
                                  ? "trending-up"
                                  : "lightbulb"
                            }
                          />
                        </span>
                        {ins.title}
                      </div>
                      <p className="m-0 mt-1.5 text-xs leading-relaxed text-text-secondary">{ins.message}</p>
                      <div className="mt-3">
                        <Button size="sm" variant="secondary" to={ins.actionRoute}>
                          {ins.actionLabel}
                        </Button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </Card>
          </div>

          {trend.length > 0 && (
            <Card className="overflow-hidden">
              <CardHeader
                title="Semester GPA Progression"
                action={<Button size="sm" variant="secondary" to="/analytics">Full Analytics</Button>}
              />
              <div className="grid grid-cols-2 gap-3 p-5 sm:grid-cols-3 md:grid-cols-6">
                {trend.map((pt, idx) => (
                  <div
                    key={`${pt.label}-${idx}`}
                    className="rounded-control border border-border bg-bg/70 p-3.5 text-center"
                  >
                    <div className="text-xs font-medium text-text-secondary">{pt.label}</div>
                    <div className="dash-num mt-1 text-xl font-bold text-primary">{pt.gpa.toFixed(2)}</div>
                  </div>
                ))}
              </div>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
