import { useCallback, useEffect, useState } from "react";
import { NavLink, useNavigate, useSearchParams, type NavigateFunction } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import {
  Alert,
  Card,
  CardHeader,
  CardLink,
  EmptyState,
  ErrorState,
  Skeleton,
  StatusBadge,
} from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, getToken, type AuthUser } from "../lib/api";
import "./dashboard.css";

/*
 * Student Dashboard â€” faithful port of Pages/Dashboard.razor on the new
 * design system. Every card loads (and fails) independently, so one bad
 * call can never freeze the whole page on "Loading...". Endpoints, query
 * strings, empty-state messages and number formats match the Razor page.
 */

/* ---------- DTO shapes (camelCase JSON from the API) ---------- */

interface DashboardSummary {
  fullName: string;
  firstName: string;
  greeting: string;
  currentGpa: number;
  gpaDeltaVsLastSemester: number;
  completedCourses: number;
  totalCourses: number;
  completionPercent: number;
  attendanceRate: number;
  attendanceDelta: number;
  subjectsToImprove: number;
  recommendedCoursesCount: number;
  unreadNotifications: number;
}

interface PerformanceRow {
  courseId: number;
  courseName: string;
  gradeLetter: string;
  gradePoints: number;
  attendancePercent: number;
  status: string;
}

interface StudyDay {
  id: number;
  date: string;
  dayLabel: string;
  subject: string;
  hours: number;
  isCompleted: boolean;
  isFreeDay: boolean;
}

interface StudyPlan {
  weekLabel: string;
  weekStart: string;
  days: StudyDay[];
}

interface Insight {
  type: string;
  title: string;
  message: string;
  actionLabel?: string | null;
  actionRoute?: string | null;
}

interface RecommendedCourse {
  id: number;
  name: string;
  track: string;
  credits: number;
  tag: string;
}

interface ActivityItem {
  id: number;
  text: string;
  createdAt: string;
}

interface MyUniversity {
  universityName?: string | null;
  department?: string | null;
  academicYear?: number | null;
  coursesCount: number;
  materialsCount: number;
}

interface UniversityCourse {
  id: number;
  code: string;
  title: string;
  materialsCount: number;
}

interface Material {
  id: number;
  title: string;
  courseName: string;
  type: string;
  createdAt: string;
  instructorName?: string;
}

interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/* Sentinel thrown by fetchJson on a 401 (expired/revoked token). */
const UNAUTHORIZED = "UnauthorizedError";

async function fetchJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${url}`, { headers: authHeaders(), signal });
  if (response.status === 401) throw new Error(UNAUTHORIZED);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return (await response.json()) as T;
}

/* Same relative-time rules as TimeAgo in Dashboard.razor. */
function timeAgo(createdAtUtc: string): string {
  const created = new Date(createdAtUtc).getTime();
  if (Number.isNaN(created)) return "";
  const minutes = Math.floor((Date.now() - created) / 60000);
  if (minutes < 1) return "Just now";
  if (minutes < 60) return `${minutes} minutes ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return hours === 1 ? "1 hour ago" : `${hours} hours ago`;
  const days = Math.floor(hours / 24);
  return days === 1 ? "Yesterday" : `${days} days ago`;
}

/* Same icon mapping as GetInsightIcon in Dashboard.razor. */
function insightIcon(type: string): string {
  switch (type.toLowerCase()) {
    case "warning": return "triangle-alert";
    case "progress": return "trending-up";
    case "recommendation": return "lightbulb";
    default: return "lightbulb";
  }
}

/**
 * Runs one card load. A 401 means the token expired mid-session: clean
 * logout and explain why (same flow as Dashboard.razor's TryLoadAsync).
 */
async function tryLoad(navigate: NavigateFunction, what: string, setError: (message: string | null) => void, load: () => Promise<void>) {
  try {
    await load();
    setError(null);
  } catch (error) {
    if ((error as Error).message === UNAUTHORIZED) {
      clearToken();
      navigate("/login?expired=1");
      return;
    }
    if ((error as Error).name === "AbortError") return;
    setError(`Could not load ${what}. Check your connection and try again.`);
  }
}

/* Shimmer placeholder shown while a card's data is loading. */function CardSkeleton({ rows = 3 }: { rows?: number }) {
  const widths = ["w-2/3", "w-1/2", "w-3/4", "w-2/5", "w-1/2"];
  return (
    <div className="flex flex-col gap-3.5 px-5 py-5">
      {Array.from({ length: rows }, (_, index) => (
        <Skeleton key={index} className={`h-4 ${widths[index % widths.length]}`} />
      ))}
    </div>
  );
}

export function Dashboard() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const denied = searchParams.get("denied") === "1";

  const [greeting, setGreeting] = useState("Good Morning");
  const [firstName, setFirstName] = useState("Student");

  // Data === null && error === null means "loading" (same convention as the
  // Razor page's nullable fields).
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [performance, setPerformance] = useState<PerformanceRow[] | null>(null);
  const [performanceError, setPerformanceError] = useState<string | null>(null);
  const [plan, setPlan] = useState<StudyPlan | null>(null);
  const [plannerError, setPlannerError] = useState<string | null>(null);
  const [recommended, setRecommended] = useState<RecommendedCourse[] | null>(null);
  const [recommendedError, setRecommendedError] = useState<string | null>(null);
  const [activity, setActivity] = useState<ActivityItem[] | null>(null);
  const [activityError, setActivityError] = useState<string | null>(null);
  const [insights, setInsights] = useState<Insight[] | null>(null);
  const [insightsError, setInsightsError] = useState<string | null>(null);
  const [university, setUniversity] = useState<MyUniversity | null>(null);
  const [universityError, setUniversityError] = useState<string | null>(null);
  const [uniCourses, setUniCourses] = useState<UniversityCourse[] | null>(null);
  const [uniMaterials, setUniMaterials] = useState<Material[] | null>(null);

  // Id of the planner day currently being toggled (for the spinner state).
  const [togglingId, setTogglingId] = useState<number | null>(null);

  /* ---------- Independent card loads (same endpoints as Dashboard.razor).
     Each is a stable useCallback so the mount effect can list them as
     dependencies without re-running. ---------- */

  const loadSummary = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "summary", setSummaryError, async () => {
      const data = await fetchJson<DashboardSummary>("/api/dashboard/summary", signal);
      setSummary(data);
      if (data) {
        setGreeting(data.greeting);
        setFirstName(data.firstName);
      }
    });
  }, [navigate]);

  const loadPerformance = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "performance", setPerformanceError, async () => {
      const rows = await fetchJson<PerformanceRow[]>("/api/dashboard/performance?semester=current", signal);
      setPerformance(rows ?? []);
    });
  }, [navigate]);

  const loadPlanner = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "study plan", setPlannerError, async () => {
      const data = await fetchJson<StudyPlan>("/api/dashboard/study-plan", signal);
      setPlan(data);
    });
  }, [navigate]);

  const loadRecommended = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "recommendations", setRecommendedError, async () => {
      const rows = await fetchJson<RecommendedCourse[]>("/api/dashboard/recommended-courses", signal);
      setRecommended(rows ?? []);
    });
  }, [navigate]);

  const loadActivity = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "activity", setActivityError, async () => {
      const rows = await fetchJson<ActivityItem[]>("/api/dashboard/recent-activity?take=4", signal);
      setActivity(rows ?? []);
    });
  }, [navigate]);

  const loadInsights = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "insights", setInsightsError, async () => {
      const rows = await fetchJson<Insight[]>("/api/dashboard/insights", signal);
      setInsights(rows ?? []);
    });
  }, [navigate]);

  const loadUniversity = useCallback(async (signal?: AbortSignal) => {
    await tryLoad(navigate, "university", setUniversityError, async () => {
      // 404 is a legit state, not an error: no university assigned yet.
      const response = await fetch(`${API_BASE_URL}/api/university/me`, { headers: authHeaders(), signal });
      if (response.status === 401) throw new Error(UNAUTHORIZED);
      if (response.status === 404) {
        setUniversity({ universityName: "", department: null, academicYear: null, coursesCount: 0, materialsCount: 0 });
      } else if (response.ok) {
        setUniversity((await response.json()) as MyUniversity);
      } else {
        throw new Error(`HTTP ${response.status}`);
      }
      const courses = await fetchJson<UniversityCourse[]>("/api/university/me/courses", signal);
      setUniCourses(courses ?? []);
      const page = await fetchJson<PagedResult<Material>>("/api/university/me/materials?page=1&pageSize=5", signal);
      setUniMaterials(page?.items ?? []);
    });
  }, [navigate]);

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      // Auth + role gate first (same as Dashboard.razor OnInitializedAsync):
      // Graduates get the dedicated career dashboard instead.
      if (!getToken()) {
        navigate("/login", { replace: true });
        return;
      }
      let me: AuthUser | null = null;
      try {
        me = await fetchJson<AuthUser>("/api/auth/me", controller.signal);
      } catch (error) {
        if ((error as Error).message === UNAUTHORIZED) {
          clearToken();
          navigate("/login?expired=1", { replace: true });
          return;
        }
        if ((error as Error).name === "AbortError") return;
        // Backend unreachable: the per-card errors below will say so.
      }
      if (controller.signal.aborted) return;
      if (me?.role === "Graduate") {
        navigate("/graduate", { replace: true });
        return;
      }
      // All cards load in parallel and fail independently.
      await Promise.all([
        loadSummary(controller.signal),
        loadPerformance(controller.signal),
        loadPlanner(controller.signal),
        loadRecommended(controller.signal),
        loadActivity(controller.signal),
        loadInsights(controller.signal),
        loadUniversity(controller.signal),
      ]);
    })();
    return () => controller.abort();
  }, [navigate, loadSummary, loadPerformance, loadPlanner, loadRecommended, loadActivity, loadInsights, loadUniversity]);

  useEffect(() => {
    document.title = "UniSphere 360+ - Dashboard";
  }, []);

  /* ---------- Planner day toggle (PATCH, response replaces the day) ---------- */

  async function toggleDay(day: StudyDay) {
    if (day.id === 0 || plan === null) return;
    setTogglingId(day.id);
    try {
      const response = await fetch(`${API_BASE_URL}/api/dashboard/study-plan/${day.id}/toggle`, {
        method: "PATCH",
        headers: authHeaders(),
      });
      if (response.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const updated = (await response.json()) as StudyDay;
      if (!updated) return;
      setPlan((current) =>
        current ? { ...current, days: current.days.map((item) => (item.id === day.id ? updated : item)) } : current,
      );
    } catch {
      setPlannerError("Could not update the study plan. Please try again.");
    } finally {
      setTogglingId(null);
    }
  }

  /* ---------- Derived values (same formats as Dashboard.razor) ---------- */

  const summaryLoading = summary === null && summaryError === null;

  return (
    <div className="dashboard">
      {/* Welcome banner */}
      <div className="dash-banner">
        <div>
          <h1>{greeting}, {firstName}!</h1>
          <p className="dash-banner-sub">Your academic journey matters. Let&apos;s make it better together.</p>
        </div>
        <div className="dash-banner-icon"><UiIcon name="graduation-cap" /></div>
      </div>

      {summaryError && <Alert kind="error">{summaryError}</Alert>}

      {denied && <Alert kind="error">Access denied: you don&apos;t have permission to view that page.</Alert>}

      {/* Academic summary cards */}
      <div className="dash-stats">
        {summaryLoading ? (
          // Loading placeholders for all five stats.
          Array.from({ length: 5 }, (_, index) => (
            <div key={index} className="dash-stat" data-accent={index}>
              <Skeleton className="mb-2.5 h-[38px] w-[38px] rounded-[10px]" />
              <Skeleton className="mb-2 h-3 w-24" />
              <Skeleton className="mb-2 h-7 w-16" />
              <Skeleton className="h-3 w-20" />
            </div>
          ))
        ) : (
          <>
            <div className="dash-stat gpa">
              <span className="dash-stat-icon"><UiIcon name="award" /></span>
              <span className="dash-stat-label">Current GPA</span>
              <span className="dash-stat-value">{summary ? summary.currentGpa.toFixed(2) : "--"}</span>
              {summary && (
                <span className={`dash-stat-delta ${summary.gpaDeltaVsLastSemester >= 0 ? "positive" : "negative"}`}>
                  {summary.gpaDeltaVsLastSemester >= 0 ? "+" : ""}{summary.gpaDeltaVsLastSemester.toFixed(2)} from last semester
                </span>
              )}
            </div>
            <div className="dash-stat courses">
              <span className="dash-stat-icon"><UiIcon name="book-open" /></span>
              <span className="dash-stat-label">Completed Courses</span>
              <span className="dash-stat-value">{summary?.completedCourses ?? 0}/{summary?.totalCourses ?? 0}</span>
              <span className="dash-stat-delta">{summary?.completionPercent ?? 0}% complete</span>
            </div>
            <div className="dash-stat attendance">
              <span className="dash-stat-icon"><UiIcon name="calendar-days" /></span>
              <span className="dash-stat-label">Attendance Rate</span>
              <span className="dash-stat-value">{summary?.attendanceRate ?? 0}%</span>
              {summary && (
                <span className={`dash-stat-delta ${summary.attendanceDelta >= 0 ? "positive" : "negative"}`}>
                  {summary.attendanceDelta >= 0 ? "+" : ""}{summary.attendanceDelta}% from last semester
                </span>
              )}
            </div>
            <div className="dash-stat improve">
              <span className="dash-stat-icon"><UiIcon name="lightbulb" /></span>
              <span className="dash-stat-label">Subjects to Improve</span>
              <span className="dash-stat-value">{summary?.subjectsToImprove ?? 0}</span>
              <span className="dash-stat-delta">Need attention</span>
            </div>
            <div className="dash-stat recommended">
              <span className="dash-stat-icon"><UiIcon name="graduation-cap" /></span>
              <span className="dash-stat-label">Recommended Courses</span>
              <span className="dash-stat-value">{summary?.recommendedCoursesCount ?? 0}</span>
              <span className="dash-stat-delta">For you</span>
            </div>
          </>
        )}
      </div>

      {/* Academic Performance */}
      <Card className="mb-6 overflow-hidden">
        <CardHeader title="Academic Performance" action={<CardLink to="/academic-advisor">View All <UiIcon name="arrow-right" /></CardLink>} />
        {performanceError ? (
          <ErrorState message={performanceError} onRetry={() => void loadPerformance()} />
        ) : performance === null ? (
          <CardSkeleton rows={3} />
        ) : performance.length === 0 ? (
          <EmptyState icon="chart-no-axes-combined" message="No academic records found. Enroll in courses to see your performance." />
        ) : (
          <div className="dash-table-wrap">
            <table className="dash-table">
              <thead>
                <tr>
                  <th>Course</th>
                  <th>Grade</th>
                  <th>Attendance</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {performance.map((row) => (
                  <tr key={row.courseId}>
                    <td>{row.courseName}</td>
                    <td className="dash-num">{row.gradeLetter} ({row.gradePoints.toFixed(1)})</td>
                    <td className="dash-num">{row.attendancePercent}%</td>
                    <td><StatusBadge status={row.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Study Planner + Recommended Courses */}
      <div className="dash-two">
        <Card className="overflow-hidden">
          <CardHeader title="Study Planner" action={<CardLink to="/study-planner">Edit <UiIcon name="arrow-right" /></CardLink>} />
          {plannerError ? (
            <ErrorState message={plannerError} onRetry={() => void loadPlanner()} />
          ) : plan === null ? (
            <CardSkeleton rows={4} />
          ) : (
            <>
              <div className="dash-planner-week">{plan.weekLabel}</div>
              <div>
                {plan.days.map((day) => (
                  <div key={day.id} className={`dash-plan-item${day.isCompleted ? " done" : ""}${day.isFreeDay ? " free" : ""}`}>
                    <div className="dash-plan-date">
                      <span className="dash-plan-day">{day.dayLabel}</span>
                      <span className="dash-plan-num">{new Date(day.date).getDate()}</span>
                    </div>
                    <div className="dash-plan-info">
                      <span className="dash-plan-title">{day.subject}</span>
                      <span className="dash-plan-hours">{day.hours} hrs</span>
                    </div>
                    {!day.isFreeDay && (
                      <button
                        type="button"
                        className={`dash-plan-check${day.isCompleted ? " on" : ""}`}
                        title="Toggle done"
                        aria-label={`Mark ${day.subject} as ${day.isCompleted ? "not done" : "done"}`}
                        disabled={togglingId === day.id}
                        onClick={() => void toggleDay(day)}
                      >
                        {day.isCompleted ? <span className="text-success"><UiIcon name="check" /></span> : <UiIcon name="circle" />}
                      </button>
                    )}
                  </div>
                ))}
              </div>
            </>
          )}
        </Card>

        <Card className="overflow-hidden">
          <CardHeader title="Recommended Courses" action={<CardLink to="/courses">View All <UiIcon name="arrow-right" /></CardLink>} />
          {recommendedError ? (
            <ErrorState message={recommendedError} onRetry={() => void loadRecommended()} />
          ) : recommended === null ? (
            <CardSkeleton rows={3} />
          ) : recommended.length === 0 ? (
            <EmptyState icon="lightbulb" message="No recommendations available at this time." />
          ) : (
            <div>
              {recommended.slice(0, 4).map((course) => (
                <div key={course.id} className="dash-rec-item">
                  <div className="dash-rec-info">
                    <span className="dash-rec-name">{course.name}</span>
                    <span className="dash-rec-tag">{course.tag}</span>
                    <span className="dash-rec-meta">{course.track} | {course.credits} credits</span>
                  </div>
                  <NavLink to={`/courses/${course.id}`} className="dash-rec-btn">View</NavLink>
                </div>
              ))}
            </div>
          )}
        </Card>
      </div>

      {/* My University */}
      <Card className="mb-6 overflow-hidden">
        <CardHeader title="My University" action={<CardLink to="/university">View all <UiIcon name="arrow-right" /></CardLink>} />
        {universityError ? (
          <ErrorState message={universityError} onRetry={() => void loadUniversity()} />
        ) : university === null ? (
          <CardSkeleton rows={3} />
        ) : !university.universityName ? (
          <EmptyState icon="landmark" message="No university assigned to your account yet." />
        ) : (
          <>
            <div className="dash-uni-sub">
              {university.department} آ· Year {university.academicYear?.toString() ?? "-"}
            </div>
            {uniCourses === null ? (
              <CardSkeleton rows={2} />
            ) : uniCourses.length === 0 ? (
              <EmptyState icon="book-open" message="No courses found for your university." />
            ) : (
              <div>
                {uniCourses.slice(0, 6).map((course) => (
                  <div key={course.id} className="dash-uni-course">
                    <span className="dash-uni-name">{course.title}</span>
                    <span className="dash-uni-count">{course.materialsCount}</span>
                  </div>
                ))}
              </div>
            )}
            <h4 className="dash-mini-heading">Latest summaries &amp; materials</h4>
            {uniMaterials === null ? (
              <CardSkeleton rows={2} />
            ) : uniMaterials.length === 0 ? (
              <EmptyState icon="file-text" message="No materials yet." />
            ) : (
              <div>
                {uniMaterials.slice(0, 5).map((material) => (
                  <div key={material.id} className="dash-activity-item">
                    <div className="min-w-0 flex-1">
                      <span className="dash-activity-msg">{material.title}</span>
                      <span className="dash-activity-time block">
                        {material.courseName}
                        {material.instructorName && material.instructorName.trim() !== "" && (
                          <> &middot; {material.instructorName}</>
                        )}
                        &middot; {material.type} &middot;{" "}
                        {new Date(material.createdAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
      </Card>

      {/* Quick Actions */}
      <Card className="mb-6 overflow-hidden">
        <CardHeader title="Quick Actions" />
        <div className="dash-actions-grid">
          <NavLink to="/academic-advisor" className="dash-action tile-grades">
            <span className="dash-action-icon"><UiIcon name="pencil" /></span>
            <span>Check My Grades</span>
          </NavLink>
          <NavLink to="/study-planner" className="dash-action tile-planner">
            <span className="dash-action-icon"><UiIcon name="calendar-days" /></span>
            <span>Plan My Study</span>
          </NavLink>
          <NavLink to="/courses" className="dash-action tile-courses">
            <span className="dash-action-icon"><UiIcon name="graduation-cap" /></span>
            <span>Course Recommendations</span>
          </NavLink>
          <NavLink to="/academic-advisor" className="dash-action tile-insights">
            <span className="dash-action-icon"><UiIcon name="lightbulb" /></span>
            <span>Academic Insights</span>
          </NavLink>
        </div>
      </Card>

      {/* Recent Activity + AI Insights */}
      <div className="dash-two">
        <Card className="overflow-hidden">
          <CardHeader title="Recent Activity" />
          {activityError ? (
            <ErrorState message={activityError} onRetry={() => void loadActivity()} />
          ) : activity === null ? (
            <CardSkeleton rows={3} />
          ) : activity.length === 0 ? (
            <EmptyState icon="history" message="No recent activity." />
          ) : (
            <div>
              {activity.map((item) => (
                <div key={item.id} className="dash-activity-item">
                  <div className="min-w-0 flex-1">
                    <span className="dash-activity-msg">{item.text}</span>
                    <span className="dash-activity-time block">{timeAgo(item.createdAt)}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>

        <Card className="overflow-hidden">
          <CardHeader title="AI Insights" />
          {insightsError ? (
            <ErrorState message={insightsError} onRetry={() => void loadInsights()} />
          ) : insights === null ? (
            <CardSkeleton rows={3} />
          ) : insights.length === 0 ? (
            <EmptyState icon="chart-no-axes-combined" message="No insights available. Keep learning!" />
          ) : (
            <div>
              {insights.map((insight, index) => (
                <div key={index} className={`dash-insight-item ${insight.type.toLowerCase()}`}>
                  <div className="dash-insight-icon"><UiIcon name={insightIcon(insight.type)} /></div>
                  <div className="dash-insight-content">
                    <span className="dash-insight-title">{insight.title}</span>
                    <span className="dash-insight-msg">{insight.message}</span>
                    {insight.actionLabel && insight.actionRoute && (
                      <NavLink to={insight.actionRoute} className="dash-insight-action">
                        {insight.actionLabel} <UiIcon name="arrow-right" />
                      </NavLink>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>
      </div>
    </div>
  );
}
