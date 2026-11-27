import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface CatalogCourse {
  id: number;
  code: string;
  title: string;
  description: string;
  track: string;
  credits: number;
  department: string;
  academicYear: number | null;
  semester: string;
  enrolledSemester?: string;
  prerequisites: string;
  instructors: string;
  materialsCount: number;
  enrolled: boolean;
  gradeLetter: string | null;
  gradePoints?: number | null;
  attendancePercent: number | null;
  enrollmentStatus: string | null;
  recommended: boolean;
  enrollmentId: number | null;
}

interface CatalogResponse {
  universityName: string;
  enrolledCount: number;
  totalCredits: number;
  courses: CatalogCourse[];
}

const GRADE_OPTIONS = ["In Progress", "A", "A-", "B+", "B", "B-", "C+", "C", "C-", "D+", "D", "F"];

export function CoursesCatalog() {
  const navigate = useNavigate();
  const [data, setData] = useState<CatalogResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);

  // Inline self-reported record editor state
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editSemester, setEditSemester] = useState("");
  const [editGrade, setEditGrade] = useState("In Progress");
  const [editAttendance, setEditAttendance] = useState("90");

  const [query, setQuery] = useState("");
  const [filterMode, setFilterMode] = useState<"all" | "enrolled" | "recommended">("all");
  const [yearFilter, setYearFilter] = useState<string>("all");

  useEffect(() => {
    document.title = "Courses & Enrollment — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const paths = ["courses", "enrollments", "courses/recommended", "university/me", "university/me/courses"];
      const responses = await Promise.all(
        paths.map((path) => fetch(`${API_BASE_URL}/api/${path}`, { headers: authHeaders() })),
      );
      if (responses.some((r) => r.status === 401)) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      const failed = responses.find((r) => !r.ok);
      if (failed) throw new Error(`HTTP ${failed.status}`);
      const [courses, enrollments, recommended, university, mine] = await Promise.all(responses.map((r) => r.json()));
      const enrolledBy = new Map<number, any>(enrollments.map((e: any) => [e.courseId, e]));
      const detailBy = new Map<number, any>(mine.map((c: any) => [c.id, c]));
      const recommendedIds = new Set<number>(recommended.map((c: any) => c.id));
      const list: CatalogCourse[] = courses.map((c: any) => {
        const e = enrolledBy.get(c.id);
        const d = detailBy.get(c.id);
        return {
          ...c,
          department: d?.department ?? "",
          academicYear: d?.academicYear ?? null,
          semester: d?.semester ?? "Current",
          enrolledSemester: e?.semester,
          prerequisites: "",
          materialsCount: d?.materialsCount ?? 0,
          enrolled: Boolean(e),
          enrollmentId: e?.id ?? null,
          gradeLetter: e ? e.gradeLetter || "In Progress" : null,
          gradePoints: e?.gradePoints ?? null,
          attendancePercent: e?.attendancePercent ?? null,
          enrollmentStatus: e?.status ?? null,
          recommended: recommendedIds.has(c.id),
        };
      });
      setData({
        universityName: university.universityName ?? "",
        enrolledCount: enrollments.length,
        totalCredits: list.filter((c) => c.enrolled).reduce((sum, c) => sum + c.credits, 0),
        courses: list,
      });
    } catch {
      setError("Could not load the course catalog.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function toggleEnrollment(course: CatalogCourse) {
    setBusyId(course.id);
    setToast(null);
    try {
      const res = course.enrolled
        ? await fetch(`${API_BASE_URL}/api/enrollments/${course.enrollmentId}`, {
            method: "DELETE",
            headers: authHeaders(),
          })
        : await fetch(`${API_BASE_URL}/api/enrollments`, {
            method: "POST",
            headers: { ...authHeaders(), "Content-Type": "application/json" },
            body: JSON.stringify({ courseId: course.id }),
          });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Could not update course enrollment."), isError: true });
        return;
      }
      setToast({
        text: course.enrolled
          ? `Dropped ${course.code} (${course.title}) from your active enrollments.`
          : `Enrolled in ${course.code} (${course.title}). Dashboard & study materials synced.`,
        isError: false,
      });
      if (editingId === course.id) setEditingId(null);
      await load();
    } catch {
      setToast({ text: "Could not update course enrollment.", isError: true });
    } finally {
      setBusyId(null);
    }
  }

  function startEditRecord(course: CatalogCourse) {
    setEditingId(course.id);
    setEditSemester(course.enrolledSemester ?? course.semester);
    setEditGrade(course.gradeLetter ?? "In Progress");
    setEditAttendance(String(course.attendancePercent ?? 90));
  }

  async function saveRecord(course: CatalogCourse) {
    setBusyId(course.id);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/enrollments/${course.enrollmentId}/my-record`, {
        method: "PUT",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({
          semester: editSemester.trim() || course.semester,
          gradeLetter: editGrade === "In Progress" ? "" : editGrade,
          attendancePercent: Number(editAttendance),
        }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Could not update your course record."), isError: true });
        return;
      }
      setEditingId(null);
      setToast({
        text: `Updated ${course.code} record (${editGrade} · ${editAttendance}% attendance). GPA & Graduation eligibility synced.`,
        isError: false,
      });
      await load();
    } catch {
      setToast({ text: "Could not update your course record.", isError: true });
    } finally {
      setBusyId(null);
    }
  }

  const filteredCourses = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (data?.courses ?? []).filter((c) => {
      if (filterMode === "enrolled" && !c.enrolled) return false;
      if (filterMode === "recommended" && !c.recommended) return false;
      if (yearFilter !== "all" && String(c.academicYear ?? "") !== yearFilter) return false;
      if (!q) return true;
      return (
        c.code.toLowerCase().includes(q) ||
        c.title.toLowerCase().includes(q) ||
        c.track.toLowerCase().includes(q) ||
        c.department.toLowerCase().includes(q)
      );
    });
  }, [data, filterMode, yearFilter, query]);

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Courses &amp; Enrollment</h1>
          <p className="mt-1 text-sm text-text-secondary">
            {data
              ? `${data.universityName} — browse the full 4-year catalog, enroll or drop courses, and update your semester grade & attendance record.`
              : "Browse university courses and manage your active semester schedule."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/graduation">
            <UiIcon name="award" />
            Graduation &amp; Honors
          </Button>
          <Button size="sm" variant="secondary" to="/university">
            <UiIcon name="landmark" />
            Lecture Materials
          </Button>
          <Button size="sm" to="/academic-advisor">
            <UiIcon name="graduation-cap" />
            Academic Advisor
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !data ? (
        <LoadingBlock label="Loading university course catalog…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="book-open" /></div>
              <span className="dash-stat-label">Active Enrollments</span>
              <span className="dash-stat-value">{data.enrolledCount}</span>
            </div>
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="award" /></div>
              <span className="dash-stat-label">Registered Credit Hours</span>
              <span className="dash-stat-value">{data.totalCredits} cr</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="sparkles" /></div>
              <span className="dash-stat-label">Catalog Courses</span>
              <span className="dash-stat-value">{data.courses.length}</span>
            </div>
          </div>

          <Card className="mb-6 p-4">
            <div className="flex flex-wrap items-center gap-3">
              <div className="relative min-w-[220px] flex-1">
                <span className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-text-secondary">
                  <UiIcon name="search" />
                </span>
                <input
                  type="search"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Filter by course code, title, or track (AI, CS, Systems)…"
                  className="w-full rounded-control border border-border bg-bg py-2 pr-3 pl-9 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <div className="flex rounded-control border border-border bg-bg p-1">
                {(
                  [
                    ["all", "All"],
                    ["enrolled", "My Enrollments"],
                    ["recommended", "Recommended"],
                  ] as const
                ).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setFilterMode(key)}
                    className={`cursor-pointer rounded-md border-0 px-3 py-1 text-xs font-semibold transition ${
                      filterMode === key ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <select
                value={yearFilter}
                onChange={(e) => setYearFilter(e.target.value)}
                className="rounded-control border border-border bg-bg px-3 py-2 text-xs font-medium text-text outline-none focus:border-primary"
              >
                <option value="all">All Years</option>
                <option value="1">Year 1</option>
                <option value="2">Year 2</option>
                <option value="3">Year 3</option>
                <option value="4">Year 4</option>
              </select>
            </div>
          </Card>

          {filteredCourses.length === 0 ? (
            <Card>
              <EmptyState icon="book-open" message="No courses match your current filters." />
            </Card>
          ) : (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {filteredCourses.map((c) => {
                const isEditing = editingId === c.id;
                const isGraded =
                  c.enrolled &&
                  Boolean(c.gradeLetter) &&
                  c.gradeLetter!.toLowerCase() !== "in progress" &&
                  c.gradeLetter!.toLowerCase() !== "pending";
                return (
                  <Card key={c.id} className="flex flex-col justify-between p-5">
                    <div>
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="rounded-md bg-primary-light px-2.5 py-0.5 text-xs font-bold text-primary">
                            {c.code}
                          </span>
                          <span className="text-xs text-text-secondary">
                            Year {c.academicYear ?? "-"} · {c.enrolledSemester ?? c.semester} · {c.credits} cr
                          </span>
                          {c.recommended && (
                            <span className="rounded-md bg-success-light px-2 py-0.5 text-[11px] font-semibold text-success">
                              Recommended
                            </span>
                          )}
                        </div>
                        {c.enrolled && <StatusBadge status={c.enrollmentStatus ?? "Good"} />}
                      </div>

                      <h3 className="m-0 mt-2.5 text-base font-semibold text-text">{c.title}</h3>
                      <p className="m-0 mt-1.5 text-xs leading-relaxed text-text-secondary">{c.description}</p>

                      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-text-secondary">
                        <span>Instructor: <strong className="text-text">{c.instructors}</strong></span>
                        <span>Materials: <strong className="dash-num text-text">{c.materialsCount}</strong></span>
                        {c.prerequisites && <span>Prereq: <strong className="text-text">{c.prerequisites}</strong></span>}
                      </div>

                      {c.enrolled && isEditing && (
                        <div className="mt-3.5 rounded-control border border-primary/30 bg-bg p-3 text-xs">
                          <div className="mb-2 font-semibold text-primary">Update My Academic Record</div>
                          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                            <label className="flex flex-col gap-1">
                              <span className="text-text-secondary">Semester</span>
                              <input
                                type="text"
                                value={editSemester}
                                onChange={(e) => setEditSemester(e.target.value)}
                                className="rounded-control border border-border bg-card px-2.5 py-1.5 text-xs text-text outline-none focus:border-primary"
                              />
                            </label>
                            <label className="flex flex-col gap-1">
                              <span className="text-text-secondary">Grade</span>
                              <select
                                value={editGrade}
                                onChange={(e) => setEditGrade(e.target.value)}
                                className="rounded-control border border-border bg-card px-2.5 py-1.5 text-xs text-text outline-none focus:border-primary"
                              >
                                {GRADE_OPTIONS.map((g) => (
                                  <option key={g} value={g}>
                                    {g}
                                  </option>
                                ))}
                              </select>
                            </label>
                            <label className="flex flex-col gap-1">
                              <span className="text-text-secondary">Attendance %</span>
                              <input
                                type="number"
                                min={0}
                                max={100}
                                value={editAttendance}
                                onChange={(e) => setEditAttendance(e.target.value)}
                                className="rounded-control border border-border bg-card px-2.5 py-1.5 text-xs text-text outline-none focus:border-primary"
                              />
                            </label>
                          </div>
                          <div className="mt-2.5 flex justify-end gap-2">
                            <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>
                              Cancel
                            </Button>
                            <Button size="sm" disabled={busyId === c.id} onClick={() => void saveRecord(c)}>
                              Save Record
                            </Button>
                          </div>
                        </div>
                      )}
                    </div>

                    <div className="mt-4 flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3.5">
                      {c.enrolled ? (
                        <div className="text-xs text-text-secondary">
                          Grade: <strong className="text-primary">{c.gradeLetter}</strong> · Attendance:{" "}
                          <strong className="dash-num text-text">{c.attendancePercent}%</strong>
                        </div>
                      ) : (
                        <span className="text-xs text-text-muted">Track: {c.track}</span>
                      )}
                      <div className="flex flex-wrap items-center gap-2">
                        {c.enrolled && !isEditing && (
                          <Button size="sm" variant="ghost" onClick={() => startEditRecord(c)}>
                            Update Record
                          </Button>
                        )}
                        <Button size="sm" variant="secondary" to="/university">
                          Notes
                        </Button>
                        <Button
                          size="sm"
                          variant={c.enrolled ? "secondary" : "primary"}
                          disabled={busyId === c.id}
                          title={isGraded ? "Graded courses cannot be dropped" : undefined}
                          onClick={() => void toggleEnrollment(c)}
                        >
                          {busyId === c.id ? "Updating…" : c.enrolled ? "Drop Course" : "Enroll"}
                        </Button>
                      </div>
                    </div>
                  </Card>
                );
              })}
            </div>
          )}
        </>
      )}
    </div>
  );
}
