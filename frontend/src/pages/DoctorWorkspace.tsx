import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { UserAvatar } from "../components/UserAvatar";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface DoctorCourse {
  id: number;
  code: string;
  title: string;
  department: string;
  academicYear: number;
  creditHours: number;
  materialsCount: number;
}

interface DoctorStudent {
  id: number;
  fullName: string;
  email: string;
  department: string;
  academicYear: number | null;
  avatarUrl: string | null;
  enrolledCount: number;
  gpa: number;
  attendanceRate: number;
}

interface DoctorMaterial {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  title: string;
  type: string;
  description: string;
  createdAt: string;
}

interface DoctorOverview {
  coursesCount: number;
  materialsCount: number;
  publishedExamsCount: number;
  draftExamsCount: number;
  studentsCount: number;
  courses: DoctorCourse[];
  students: DoctorStudent[];
  recentMaterials: DoctorMaterial[];
}

const fmtDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { dateStyle: "medium" });

export function DoctorWorkspace() {
  const navigate = useNavigate();
  const [data, setData] = useState<DoctorOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [courseId, setCourseId] = useState<number>(0);
  const [title, setTitle] = useState("");
  const [type, setType] = useState("Lecture");
  const [description, setDescription] = useState("");
  const [content, setContent] = useState("");
  const [uploading, setUploading] = useState(false);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const get = (path: string) => fetch(`${API_BASE_URL}/api/doctor/${path}`, { headers: authHeaders() });
      const responses = await Promise.all(
        ["dashboard", "courses", "materials?pageSize=6", "exam-assistant/exams"].map(get),
      );
      if (responses.some((r) => r.status === 401)) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (responses.some((r) => r.status === 403)) {
        setError("Faculty Workspace is only available for doctors.");
        return;
      }
      const failed = responses.find((r) => !r.ok);
      if (failed) throw new Error(`HTTP ${failed.status}`);
      const [dashboard, courses, materials, exams] = await Promise.all(responses.map((r) => r.json()));

      const rosters = await Promise.all(
        courses.map(async (c: any) => {
          const res = await get(`courses/${c.id}/students`);
          return res.ok ? res.json() : [];
        }),
      );
      const byStudent = new Map<number, any[]>();
      for (const row of rosters.flat()) {
        byStudent.set(row.studentId, [...(byStudent.get(row.studentId) ?? []), row]);
      }
      const average = (rows: any[], key: string) => rows.reduce((sum, r) => sum + r[key], 0) / rows.length;
      const codeOf = new Map<number, string>(courses.map((c: any) => [c.id, c.code]));

      setData({
        coursesCount: dashboard.courseCount,
        studentsCount: dashboard.studentCount,
        materialsCount: materials.total,
        publishedExamsCount: exams.filter((x: any) => x.status === "Published").length,
        draftExamsCount: exams.filter((x: any) => x.status !== "Published").length,
        courses: courses.map((c: any) => ({ ...c, title: c.displayTitle || c.title, creditHours: c.credits })),
        students: [...byStudent.values()].map((rows) => {
          // Ungraded enrollments carry 0 points and must not drag the average down.
          const graded = rows.filter((r) => r.gradeLetter);
          return {
            id: rows[0].studentId,
            fullName: rows[0].fullName,
            email: rows[0].email,
            department: rows[0].department,
            academicYear: rows[0].academicYear,
            avatarUrl: null,
            enrolledCount: rows.length,
            gpa: graded.length ? average(graded, "gradePoints") : 0,
            attendanceRate: Math.round(average(rows, "attendancePercent")),
          };
        }),
        recentMaterials: materials.items.map((m: any) => ({ ...m, courseCode: codeOf.get(m.courseId) ?? "" })),
      });
      if (courses.length > 0) {
        setCourseId((prev) => (prev > 0 ? prev : courses[0].id));
      }
    } catch {
      setError("Could not load Faculty Workspace.");
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleUploadMaterial(e: FormEvent) {
    e.preventDefault();
    if (!courseId || !title.trim() || !content.trim()) {
      setToast({ text: "Please select a course and provide both title and lecture content.", isError: true });
      return;
    }
    setUploading(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/doctor/materials`, {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({
          courseId,
          title: title.trim(),
          type,
          description: description.trim(),
          content: content.trim(),
        }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Upload failed."), isError: true });
        return;
      }
      setTitle("");
      setDescription("");
      setContent("");
      setToast({ text: "Course material published.", isError: false });
      await load();
    } catch {
      setToast({ text: "Failed to upload material.", isError: true });
    } finally {
      setUploading(false);
    }
  }

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Faculty Workspace</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Manage assigned courses, publish lecture notes directly into the RAG knowledge base, and oversee enrolled students.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" to="/doctor/research">
            <UiIcon name="microscope" />
            Research Hub
          </Button>
          <Button variant="secondary" to="/doctor/exam-results">
            <UiIcon name="clipboard-check" />
            Exam Results &amp; Grading
          </Button>
          <Button to="/doctor/exam-assistant">
            <UiIcon name="sparkles" />
            AI Exam Assistant
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void load()} /></Card>
      ) : !data ? (
        <LoadingBlock label="Loading Faculty Workspace…" />
      ) : (
        <>
          <div className="grid grid-cols-1 gap-4 mb-6 sm:grid-cols-2 lg:grid-cols-4">
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="book-open" /></div>
              <span className="dash-stat-label">Assigned Courses</span>
              <span className="dash-stat-value">{data.coursesCount}</span>
            </div>
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="file-text" /></div>
              <span className="dash-stat-label">Course Materials</span>
              <span className="dash-stat-value">{data.materialsCount}</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="clipboard-check" /></div>
              <span className="dash-stat-label">Published Exams</span>
              <span className="dash-stat-value">{data.publishedExamsCount}</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="users" /></div>
              <span className="dash-stat-label">Active Students</span>
              <span className="dash-stat-value">{data.studentsCount}</span>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-6 mb-6 lg:grid-cols-12">
            {/* Upload Material Form */}
            <Card className="lg:col-span-5">
              <CardHeader title="Upload Course Material" />
              <form onSubmit={(e) => void handleUploadMaterial(e)} className="flex flex-col gap-3.5 p-5 text-sm">
                <div>
                  <label className="mb-1 block font-medium text-text">Course</label>
                  <select
                    value={courseId}
                    onChange={(e) => setCourseId(Number(e.target.value))}
                    className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  >
                    {data.courses.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.code} — {c.title}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="mb-1 block font-medium text-text">Material Title</label>
                    <input
                      type="text"
                      required
                      value={title}
                      onChange={(e) => setTitle(e.target.value)}
                      placeholder="e.g., Week 6 Neural Nets"
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block font-medium text-text">Type</label>
                    <select
                      value={type}
                      onChange={(e) => setType(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      <option value="Lecture">Lecture</option>
                      <option value="Summary">Summary</option>
                      <option value="Assignment">Assignment</option>
                      <option value="Announcement">Announcement</option>
                    </select>
                  </div>
                </div>

                <div>
                  <label className="mb-1 block font-medium text-text">Short Description</label>
                  <input
                    type="text"
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    placeholder="Key takeaways or lab instructions"
                    className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>

                <div>
                  <label className="mb-1 block font-medium text-text">Material Content / Notes</label>
                  <textarea
                    rows={5}
                    required
                    value={content}
                    onChange={(e) => setContent(e.target.value)}
                    placeholder="Paste lecture notes, syllabus bullets, or assignment rubric — automatically indexed for AI Study Assistant & Exam Generator…"
                    className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>

                <button
                  type="submit"
                  disabled={uploading}
                  className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold disabled:opacity-60"
                >
                  <UiIcon name="upload" />
                  {uploading ? "Uploading…" : "Publish Material"}
                </button>
              </form>
            </Card>

            {/* Assigned Courses */}
            <Card className="overflow-hidden lg:col-span-7">
              <CardHeader
                title={`Assigned Courses (${data.courses.length})`}
                action={<Button size="sm" variant="secondary" to="/doctor/ai">Teaching AI</Button>}
              />
              {data.courses.length === 0 ? (
                <EmptyState icon="book-open" message="No courses assigned yet." />
              ) : (
                <div className="max-h-[420px] overflow-y-auto">
                  <table className="dash-table">
                    <thead>
                      <tr>
                        <th>Code</th>
                        <th>Course Title</th>
                        <th>Year</th>
                        <th>Materials</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.courses.map((c) => (
                        <tr key={c.id}>
                          <td className="font-semibold text-primary">{c.code}</td>
                          <td className="font-medium text-text">{c.title}</td>
                          <td className="dash-num text-text-secondary">Year {c.academicYear}</td>
                          <td className="dash-num">
                            <span className="rounded-md bg-primary-light px-2 py-0.5 text-xs font-semibold text-primary">
                              {c.materialsCount}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            {/* Enrolled Students */}
            <Card className="overflow-hidden lg:col-span-7">
              <CardHeader title={`Enrolled Students (${data.students.length})`} />
              {data.students.length === 0 ? (
                <EmptyState icon="users" message="No students found." />
              ) : (
                <div className="dash-table-wrap">
                  <table className="dash-table">
                    <thead>
                      <tr>
                        <th>Student</th>
                        <th>Department</th>
                        <th>Avg. Grade Points</th>
                        <th>Attendance</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.students.map((s) => (
                        <tr key={s.id}>
                          <td>
                            <div className="flex items-center gap-2.5">
                              <UserAvatar fullName={s.fullName} avatarUrl={s.avatarUrl} size={32} />
                              <div>
                                <div className="font-medium text-text">{s.fullName}</div>
                                <div className="text-xs text-text-secondary">{s.email}</div>
                              </div>
                            </div>
                          </td>
                          <td className="text-text-secondary">{s.department}</td>
                          <td className="dash-num font-semibold text-text">{s.gpa.toFixed(2)}</td>
                          <td>
                            <StatusBadge status={s.attendanceRate >= 85 ? "Excellent" : "Needs attention"} />
                            <span className="dash-num ml-1.5 text-xs text-text-secondary">{s.attendanceRate}%</span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>

            {/* Recent Materials */}
            <Card className="overflow-hidden lg:col-span-5">
              <CardHeader title="Recently Published Materials" />
              <div className="divide-y divide-border">
                {data.recentMaterials.slice(0, 6).map((m) => (
                  <div key={m.id} className="flex items-start justify-between gap-3 px-5 py-3.5 text-sm">
                    <div className="min-w-0">
                      <div className="truncate font-medium text-text">{m.title}</div>
                      <div className="mt-0.5 text-xs text-text-secondary">
                        <span className="font-semibold text-primary">{m.courseCode}</span> · {m.type} · {fmtDate(m.createdAt)}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
