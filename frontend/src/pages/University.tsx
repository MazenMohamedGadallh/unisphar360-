import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, type NavigateFunction } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { Alert } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, getToken, type AuthUser } from "../lib/api";
import "./university.css";

/*
 * My University — faithful port of Pages/University.razor on the new design
 * system: university-scoped course catalog with client-side filters, the
 * server-paged materials list with course/type/search filters, the detail
 * modal and the attachment download (fetch + Blob replaces the Blazor
 * JS-interop). Endpoints, query strings, error messages, the Graduate
 * redirect and the 401 flow match the Razor page exactly.
 */

/* ---------- DTO shapes (camelCase JSON from the API) ---------- */

interface MyUniversity {
  universityName: string | null;
  department: string | null;
  academicYear: number | null;
  coursesCount: number;
  materialsCount: number;
}

interface UniversityCourse {
  id: number;
  code: string;
  title: string;
  department: string;
  academicYear: number | null;
  semester: string;
  materialsCount: number;
  latestMaterialDate: string | null;
  instructors: string;
  status: string;
  matchesStudentDepartmentYear: boolean;
}

interface Material {
  id: number;
  courseId: number;
  courseName: string;
  title: string;
  type: string;
  description: string;
  content: string;
  fileUrl: string | null;
  createdAt: string;
  instructorName: string;
  hasUploadedFile: boolean;
  uploadedFileName: string | null;
  uploadedFileSizeBytes: number | null;
}

interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

const UNAUTHORIZED = "UnauthorizedError";

async function fetchJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${url}`, { headers: authHeaders(), signal });
  if (response.status === 401) throw new Error(UNAUTHORIZED);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return (await response.json()) as T;
}

const PAGE_SIZE = 10;

const MaterialTypes = ["Summary", "Lecture", "Assignment", "Exam", "Announcement"];

/* Same class rule as StatusClass in University.razor. */
function statusClass(status: string): string {
  switch (status.toLowerCase()) {
    case "passed": return "passed";
    case "available": return "available";
    case "inprogress": return "in-progress";
    case "locked": return "locked";
    default: return "";
  }
}

/* Browser-locale equivalent of Razor's CreatedAt.ToString("MMM d, yyyy").
   (Client-side timezone rendering, accepted on the Dashboard as well.) */
function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? ""
    : date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

/* Port of SafeFileName in University.razor: strip invalid filename
   characters (web equivalent of Path.GetInvalidFileNameChars), trim,
   cap at 180 chars while preserving the extension. */
function safeFileName(original: string | null, id: number): string {
  if (!original || !original.trim()) return `material-${id}`;
  const invalid = ['<', '>', ':', '"', "/", "\\", "|", "?", "*"];
  const cleaned = original
    .split("")
    .filter((c) => !invalid.includes(c) && c !== "\r" && c !== "\n" && c.charCodeAt(0) >= 32)
    .join("")
    .trim();
  if (cleaned.length > 180) {
    const dot = cleaned.lastIndexOf(".");
    const extension = dot > 0 ? cleaned.slice(dot) : "";
    const stem = extension ? cleaned.slice(0, dot) : cleaned;
    const maxStem = Math.max(1, 180 - extension.length);
    return stem.slice(0, Math.min(stem.length, maxStem)) + extension;
  }
  return cleaned || `material-${id}`;
}

/* Port of FormatSize in University.razor ("0.#" style: no trailing .0). */
function formatSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1).replace(/\.0$/, "")} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1).replace(/\.0$/, "")} KB`;
  return `${bytes} B`;
}

/**
 * Shared error surface: a 401 means the token expired mid-session (clean
 * logout + explain why), anything else lands in the page error banner with
 * a Retry button — the same two outcomes the Razor page produced.
 */
function handleLoadError(error: unknown, navigate: NavigateFunction, setError: (message: string | null) => void, message: string) {
  if ((error as Error).message === UNAUTHORIZED) {
    clearToken();
    navigate("/login?expired=1");
    return;
  }
  if ((error as Error).name === "AbortError") return;
  setError(message);
}

export function University() {
  const navigate = useNavigate();

  // info === null means "loading" (nullable-field convention of the Razor page).
  const [info, setInfo] = useState<MyUniversity | null>(null);
  const [courses, setCourses] = useState<UniversityCourse[] | null>(null);
  const [materials, setMaterials] = useState<Material[] | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageError, setPageError] = useState<string | null>(null);

  // Course catalog filters (client-side, like the Razor page).
  const [departmentFilter, setDepartmentFilter] = useState("");
  const [yearFilter, setYearFilter] = useState("");
  const [courseSearch, setCourseSearch] = useState("");

  // Material filters (server-side; search applies on Enter).
  const [courseId, setCourseId] = useState("");
  const [type, setType] = useState("");
  const [search, setSearch] = useState("");

  // Detail modal.
  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [detail, setDetail] = useState<Material | null>(null);
  const [detailId, setDetailId] = useState(0);

  // Attachment download.
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  // Last filter values used by a materials fetch — lets Retry repeat the
  // exact same request (the Razor fields keep their values across reloads).
  const appliedFiltersRef = useRef({ courseId: "", type: "", search: "" });

  /* ---------- Data loads ---------- */

  const loadMaterialsInto = useCallback(
    async (
      targetPage: number,
      filterValues: { courseId: string; type: string; search: string },
      signal?: AbortSignal,
    ): Promise<{ items: Material[]; total: number }> => {
      let query =
        `/api/university/me/materials?page=${targetPage}&pageSize=${PAGE_SIZE}` +
        `&search=${encodeURIComponent(filterValues.search)}&type=${encodeURIComponent(filterValues.type)}`;
      if (filterValues.courseId) query += `&courseId=${filterValues.courseId}`;
      appliedFiltersRef.current = { ...filterValues };
      const result = await fetchJson<PagedResult<Material>>(query, signal);
      return { items: result.items ?? [], total: result.total ?? 0 };
    },
    [], // pure fetch composition; only stable helpers referenced
  );

  /** Event-driven materials reload (filter changes and paging), mirroring
      LoadMaterialsSafeAsync: 401 -> logout, everything else -> page banner. */
  async function loadMaterialsSafe(targetPage: number, filterValues: { courseId: string; type: string; search: string }) {
    try {
      let result = await loadMaterialsInto(targetPage, filterValues);
      const totalPages = Math.max(1, Math.ceil(result.total / PAGE_SIZE));
      // A filter change can shrink the list while the user is on a later
      // page: land on the last valid page instead of an empty one.
      if (targetPage > totalPages && result.total > 0) {
        result = await loadMaterialsInto(totalPages, filterValues);
      }
      setMaterials(result.items);
      setTotal(result.total);
      setPage(Math.min(targetPage, Math.max(1, Math.ceil(result.total / PAGE_SIZE))));
      setPageError(null);
    } catch (error) {
      handleLoadError(error, navigate, setPageError, "Could not load materials. Check your connection and try again.");
    }
  }

  /** Full page load: /me (404 = no university) -> courses -> materials.
      The materials step uses the last applied filters so the Retry button
      repeats the same request (Razor ReloadAsync semantics). */
  const initialLoad = useCallback(
    async (signal?: AbortSignal) => {
      setPageError(null);
      try {
        // 404 is a legit state, not an error: no university assigned yet.
        const response = await fetch(`${API_BASE_URL}/api/university/me`, { headers: authHeaders(), signal });
        if (response.status === 401) throw new Error(UNAUTHORIZED);
        if (response.status === 404) {
          setInfo({ universityName: "", department: null, academicYear: null, coursesCount: 0, materialsCount: 0 });
          return;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        setInfo((await response.json()) as MyUniversity);

        const courseList = await fetchJson<UniversityCourse[]>("/api/university/me/courses", signal);
        setCourses(courseList ?? []);

        const result = await loadMaterialsInto(1, appliedFiltersRef.current, signal);
        setMaterials(result.items);
        setTotal(result.total);
        setPage(1);
      } catch (error) {
        handleLoadError(error, navigate, setPageError, "Could not load university data. Check your connection and try again.");
      }
    },
    [navigate, loadMaterialsInto],
  );

  /* ---------- Mount: auth gate, Graduate redirect, then load ---------- */

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      if (!getToken()) {
        navigate("/login", { replace: true });
        return;
      }
      // Role gate first (same as GuardAndLoadAsync in University.razor):
      // Graduates use the alumni & career-center view instead of student
      // materials.
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
        // Backend unreachable: keep loading; the data calls will surface it.
      }
      if (controller.signal.aborted) return;
      if (me?.role?.trim().toLowerCase() === "graduate") {
        navigate("/graduate/alumni", { replace: true });
        return;
      }
      await initialLoad(controller.signal);
    })();
    return () => controller.abort();
  }, [navigate, initialLoad]);

  useEffect(() => {
    document.title = "My University - UniSphere 360+";
  }, []);

  /* ---------- Derived course data (client-side filters + ordering) ---------- */

  const departments = useMemo(() => {
    const values = (courses ?? [])
      .map((c) => c.department)
      .filter((d) => d && d.trim());
    return Array.from(new Set(values.map((d) => d.toLowerCase())))
      .sort((a, b) => a.localeCompare(b))
      .map(
        (lower) => values.find((d) => d.toLowerCase() === lower) as string,
      );
  }, [courses]);

  const years = useMemo(() => {
    return Array.from(
      new Set((courses ?? []).filter((c) => c.academicYear !== null).map((c) => c.academicYear as number)),
    ).sort((a, b) => a - b);
  }, [courses]);

  const visibleCourses = useMemo(() => {
    if (!courses) return [];
    let list = courses;
    if (departmentFilter.trim()) {
      const requested = departmentFilter.toLowerCase();
      list = list.filter((c) => c.department.toLowerCase() === requested);
    }
    if (yearFilter !== "") {
      const year = Number(yearFilter);
      if (!Number.isNaN(year)) list = list.filter((c) => c.academicYear === year);
    }
    const q = courseSearch.trim().toLowerCase();
    if (q) {
      list = list.filter((c) => c.code.toLowerCase().includes(q) || c.title.toLowerCase().includes(q));
    }
    // Own department/year first, then the catalog order.
    return [...list].sort((a, b) => {
      if (a.matchesStudentDepartmentYear !== b.matchesStudentDepartmentYear) {
        return a.matchesStudentDepartmentYear ? -1 : 1;
      }
      return a.code.toLowerCase().localeCompare(b.code.toLowerCase());
    });
  }, [courses, departmentFilter, yearFilter, courseSearch]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  /* ---------- Detail modal ---------- */

  /// Opens the detail modal by fetching the full DTO through
  /// GET /api/university/me/materials/{id}: the server re-checks that the
  /// material belongs to the caller's university (foreign ids are 404).
  async function loadMaterialDetail(id: number) {
    setDetailId(id);
    setDetailOpen(true);
    setDetailLoading(true);
    setDetailError(null);
    setDownloadError(null);
    setDetail(null);
    // Modal paints its loading state first (Task.Yield equivalent).
    await new Promise((resolve) => setTimeout(resolve, 0));
    try {
      const dto = await fetchJson<Material>(`/api/university/me/materials/${id}`);
      setDetail(dto);
    } catch (error) {
      if ((error as Error).message === UNAUTHORIZED) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      const notFound = (error as Error).message === "HTTP 404";
      setDetailError(
        notFound
          ? "This material is not in your university's materials."
          : "Could not load material. Check your connection and try again.",
      );
    } finally {
      setDetailLoading(false);
    }
  }

  function retryMaterialDetail() {
    if (detailId > 0) void loadMaterialDetail(detailId);
  }

  function closeMaterialDetail() {
    setDetailOpen(false);
    setDetail(null);
    setDetailId(0);
    setDownloadError(null);
  }

  // Escape closes the modal from anywhere (Razor bound it to the modal;
  // a window listener is the React equivalent that needs no focus trap).
  useEffect(() => {
    if (!detailOpen) return;
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setDetailOpen(false);
        setDetail(null);
        setDetailId(0);
        setDownloadError(null);
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [detailOpen]);

  /* ---------- Attachment download (fetch + Blob replaces JS interop) ---------- */

  async function downloadAttachment(material: Material) {
    if (downloading || !material.hasUploadedFile) return;
    setDownloading(true);
    setDownloadError(null);
    try {
      const response = await fetch(`${API_BASE_URL}/api/materials/${material.id}/file`, {
        headers: authHeaders(),
      });
      if (response.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (response.status === 404) {
        setDownloadError("The attachment is no longer available.");
        return;
      }
      if (!response.ok) {
        setDownloadError(`Could not download the attachment (${response.status}).`);
        return;
      }
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = safeFileName(material.uploadedFileName, material.id);
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
    } catch {
      setDownloadError("Could not download the attachment. Please try again.");
    } finally {
      setDownloading(false);
    }
  }

  /* ---------- Render ---------- */

  const noUniversity = info !== null && !info.universityName;

  return (
    <div className="university">
      {/* Page header */}
      <div className="uni-header">
        <h1 className="uni-title">My University</h1>
        <p className="uni-subtitle">{info?.universityName ?? "Course materials and summaries"}</p>
        {info !== null && !noUniversity && (
          <div className="uni-meta">
            {info.department && (
              <span className="uni-chip"><UiIcon name="book-open" />{info.department}</span>
            )}
            {info.academicYear !== null && (
              <span className="uni-chip"><UiIcon name="graduation-cap" />Year {info.academicYear}</span>
            )}
            <span className="uni-chip"><UiIcon name="search" />{info.coursesCount} courses</span>
            <span className="uni-chip"><UiIcon name="file-text" />{info.materialsCount} materials</span>
          </div>
        )}
      </div>

      {downloadError && (
        <Alert kind="error">
          <span className="min-w-0 flex-1">{downloadError}</span>
          <button type="button" className="uni-btn" onClick={() => setDownloadError(null)}>Dismiss</button>
        </Alert>
      )}

      {pageError && (
        <Alert kind="error">
          <span className="min-w-0 flex-1">{pageError}</span>
          <button type="button" className="uni-btn" onClick={() => void initialLoad()}>Retry</button>
        </Alert>
      )}

      {info === null && !pageError && (
        <div className="uni-loading"><span className="uni-spin" aria-hidden="true" />Loading university...</div>
      )}

      {noUniversity && (
        <div className="uni-empty"><UiIcon name="landmark" />No university assigned to your account yet.</div>
      )}

      {info !== null && !noUniversity && (
        <>
          {/* ---------- Courses card ---------- */}
          <section className="uni-card">
            <div className="uni-card-header">
              <h3 className="uni-card-title">Courses</h3>
              <span className="uni-card-count">
                {courses === null ? "All courses" : `${courses.length} in your university`}
              </span>
            </div>
            <div className="uni-card-body">
              {courses === null ? (
                <div className="uni-loading"><span className="uni-spin" aria-hidden="true" />Loading courses...</div>
              ) : (
                <>
                  <div className="uni-filters">
                    <div className="uni-field">
                      <label htmlFor="courseDepartment" className="uni-label">Department</label>
                      <select
                        id="courseDepartment"
                        value={departmentFilter}
                        onChange={(e) => setDepartmentFilter(e.target.value)}
                        className="uni-input"
                      >
                        <option value="">All departments</option>
                        {departments.map((dept) => (
                          <option key={dept} value={dept}>{dept}</option>
                        ))}
                      </select>
                    </div>
                    <div className="uni-field">
                      <label htmlFor="courseYear" className="uni-label">Year</label>
                      <select
                        id="courseYear"
                        value={yearFilter}
                        onChange={(e) => setYearFilter(e.target.value)}
                        className="uni-input"
                      >
                        <option value="">All years</option>
                        {years.map((year) => (
                          <option key={year} value={String(year)}>Year {year}</option>
                        ))}
                      </select>
                    </div>
                    <div className="uni-field">
                      <label htmlFor="courseSearch" className="uni-label">Search</label>
                      <input
                        id="courseSearch"
                        type="search"
                        value={courseSearch}
                        onChange={(e) => setCourseSearch(e.target.value)}
                        placeholder="Course name or code..."
                        className="uni-input"
                      />
                    </div>
                  </div>
                  <div className="uni-card-count">Showing {visibleCourses.length} of {courses.length} courses</div>
                  {courses.length === 0 ? (
                    <div className="uni-empty"><UiIcon name="book-open" />No courses are available for your university yet.</div>
                  ) : visibleCourses.length === 0 ? (
                    <div className="uni-empty"><UiIcon name="search" />No courses match the current filters.</div>
                  ) : (
                    <div className="uni-course-list">
                      {visibleCourses.map((course) => (
                        <div key={course.id} className="uni-course-item">
                          <div className="uni-course-main">
                            <span className="uni-course-code">{course.code}</span>
                            <span className="uni-course-name">{course.title}</span>
                            <span className="uni-course-track">
                              {course.department.trim() ? course.department : "All departments"}
                              {" · Year "}{course.academicYear?.toString() ?? "-"}
                              {" · "}{course.semester}
                            </span>
                            {course.instructors.trim() && (
                              <span className="uni-course-track">Instructor: {course.instructors}</span>
                            )}
                            {course.matchesStudentDepartmentYear && (
                              <span className="uni-match">Matches your department/year</span>
                            )}
                          </div>
                          <div className="uni-course-actions">
                            <span className={`uni-status ${statusClass(course.status)}`}>{course.status}</span>
                            <span className="uni-materials-count">
                              {course.materialsCount} material{course.materialsCount === 1 ? "" : "s"}
                            </span>
                            <Link to={`/courses/${course.id}`} className="uni-btn">Open</Link>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
          </section>

          {/* ---------- Materials card ---------- */}
          <section className="uni-card">
            <div className="uni-card-header">
              <h3 className="uni-card-title">Materials</h3>
            </div>
            <div className="uni-card-body">
              <div className="uni-filters">
                <div className="uni-field">
                  <label htmlFor="courseFilter" className="uni-label">Course</label>
                  <select
                    id="courseFilter"
                    value={courseId}
                    onChange={(e) => {
                      setCourseId(e.target.value);
                      // Filter changes intentionally restart at page 1.
                      void loadMaterialsSafe(1, { courseId: e.target.value, type, search });
                    }}
                    className="uni-input"
                  >
                    <option value="">All courses</option>
                    {(courses ?? []).map((c) => (
                      <option key={c.id} value={String(c.id)}>
                        {c.title} ({c.materialsCount}){c.instructors.trim() ? ` - ${c.instructors}` : ""}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="uni-field">
                  <label htmlFor="typeFilter" className="uni-label">Type</label>
                  <select
                    id="typeFilter"
                    value={type}
                    onChange={(e) => {
                      setType(e.target.value);
                      void loadMaterialsSafe(1, { courseId, type: e.target.value, search });
                    }}
                    className="uni-input"
                  >
                    <option value="">All types</option>
                    {MaterialTypes.map((t) => (
                      <option key={t} value={t}>{t}</option>
                    ))}
                  </select>
                </div>
                <div className="uni-field">
                  <label htmlFor="materialSearch" className="uni-label">Search</label>
                  <input
                    id="materialSearch"
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") void loadMaterialsSafe(1, { courseId, type, search });
                    }}
                    placeholder="Title or description..."
                    className="uni-input"
                  />
                </div>
              </div>

              {materials === null ? (
                <div className="uni-loading"><span className="uni-spin" aria-hidden="true" />Loading materials...</div>
              ) : materials.length === 0 ? (
                <div className="uni-empty"><UiIcon name="file-text" />No materials match the current filters.</div>
              ) : (
                <>
                  <div className="uni-material-list">
                    {materials.map((m) => (
                      <div
                        key={m.id}
                        role="button"
                        tabIndex={0}
                        className={detail?.id === m.id ? "uni-material-row is-selected" : "uni-material-row"}
                        onClick={() => void loadMaterialDetail(m.id)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            void loadMaterialDetail(m.id);
                          }
                        }}
                      >
                        <div className="uni-material-main">
                          <span className="uni-material-type">{m.type}</span>
                          <span className="uni-material-title">{m.title}</span>
                          <span className="uni-material-track">
                            {m.courseName}
                            {m.instructorName.trim() && ` · ${m.instructorName}`}
                            {" · "}{formatDate(m.createdAt)}
                          </span>
                        </div>
                        <div className="uni-material-actions">
                          {m.hasUploadedFile && (
                            <button
                              type="button"
                              className="uni-btn"
                              disabled={downloading}
                              onClick={(e) => {
                                e.stopPropagation();
                                void downloadAttachment(m);
                              }}
                            >
                              Download
                            </button>
                          )}
                          <button
                            type="button"
                            className="uni-btn"
                            onClick={(e) => {
                              e.stopPropagation();
                              void loadMaterialDetail(m.id);
                            }}
                          >
                            Open
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                  <div className="uni-pagination">
                    <button
                      type="button"
                      className="uni-btn"
                      disabled={page <= 1}
                      onClick={() => void loadMaterialsSafe(page - 1, { courseId, type, search })}
                    >
                      Prev
                    </button>
                    <span>Page {page} of {totalPages} ({total} total)</span>
                    <button
                      type="button"
                      className="uni-btn"
                      disabled={page >= totalPages}
                      onClick={() => void loadMaterialsSafe(page + 1, { courseId, type, search })}
                    >
                      Next
                    </button>
                  </div>
                </>
              )}
            </div>
          </section>
        </>
      )}

      {/* ---------- Material detail modal ---------- */}
      {detailOpen && (
        <div className="uni-modal-overlay" onClick={closeMaterialDetail}>
          <div
            className="uni-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="material-detail-title"
            onClick={(e) => e.stopPropagation()}
          >
            {detailLoading ? (
              <div className="uni-modal-body">
                <div className="uni-loading"><span className="uni-spin" aria-hidden="true" />Loading material...</div>
              </div>
            ) : detailError ? (
              <>
                <div className="uni-modal-header">
                  <h3 id="material-detail-title" className="uni-modal-title">Material</h3>
                  <button type="button" className="uni-modal-close" onClick={closeMaterialDetail} aria-label="Close">
                    <UiIcon name="x" />
                  </button>
                </div>
                <div className="uni-modal-body"><Alert kind="error">{detailError}</Alert></div>
                <div className="uni-modal-actions">
                  <button type="button" className="uni-btn" onClick={retryMaterialDetail}>Retry</button>
                  <button type="button" className="uni-btn" onClick={closeMaterialDetail}>Back</button>
                </div>
              </>
            ) : detail ? (
              <>
                <div className="uni-modal-header">
                  <h3 id="material-detail-title" className="uni-modal-title">{detail.title}</h3>
                  <button type="button" className="uni-modal-close" onClick={closeMaterialDetail} aria-label="Close">
                    <UiIcon name="x" />
                  </button>
                </div>
                <div className="uni-modal-body">
                  <div className="uni-modal-meta">
                    <span className="uni-status available">{detail.type}</span>
                    <span>{detail.courseName}</span>
                    {detail.instructorName.trim() && <span>· {detail.instructorName}</span>}
                    <span>· {formatDate(detail.createdAt)}</span>
                  </div>
                  {!detail.description.trim() && !detail.content.trim() && !detail.fileUrl && !detail.hasUploadedFile ? (
                    <div className="uni-empty">This material has no description or content yet.</div>
                  ) : (
                    <>
                      {detail.description.trim() && <p style={{ margin: "0 0 10px" }}>{detail.description}</p>}
                      {detail.content.trim() && <div className="uni-modal-content">{detail.content}</div>}
                      {detail.hasUploadedFile && (
                        <p className="uni-material-track" style={{ margin: "10px 0 0" }}>
                          Attachment: {detail.uploadedFileName}
                          {detail.uploadedFileSizeBytes !== null &&
                            ` (${formatSize(detail.uploadedFileSizeBytes)})`}
                        </p>
                      )}
                    </>
                  )}
                </div>
                <div className="uni-modal-actions">
                  {detail.hasUploadedFile && (
                    <button
                      type="button"
                      className="uni-btn uni-btn-primary"
                      disabled={downloading}
                      onClick={() => void downloadAttachment(detail)}
                    >
                      <UiIcon name="download" />
                      {downloading ? "Downloading..." : "Download attachment"}
                    </button>
                  )}
                  {detail.fileUrl && (
                    <a className="uni-btn" href={detail.fileUrl} target="_blank" rel="noopener">Open link</a>
                  )}
                  <button type="button" className="uni-btn" onClick={closeMaterialDetail}>Back</button>
                </div>
              </>
            ) : null}
          </div>
        </div>
      )}

      {downloadError && (
        <Alert kind="error">{downloadError}</Alert>
      )}
    </div>
  );
}
