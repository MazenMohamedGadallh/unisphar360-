import { useMemo, useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, Modal, PageHeader, Pagination } from '../../components/ui';
import { downloadFile, errorMessage, qs } from '../../lib/api';
import { useApi, useDebounced, usePageTitle } from '../../lib/hooks';
import { Link } from '../../lib/router';
import { fmtBytes, fmtDate } from '../../lib/format';
import type { MaterialDto, MyUniversityDto, PagedResultDto, UniversityCourseDto } from '../../types/dto';

const MATERIAL_TYPES = ['Summary', 'Lecture', 'Assignment', 'Exam', 'Announcement'];
const PAGE_SIZE = 10;

function statusClass(status: string | null | undefined): string {
  switch ((status ?? '').toLowerCase()) {
    case 'passed': return 'passed';
    case 'available': return 'available';
    case 'inprogress': return 'in-progress';
    case 'locked': return 'locked';
    default: return '';
  }
}

export default function University() {
  usePageTitle('My University');
  const info = useApi<MyUniversityDto>('/api/university/me');
  const hasUniversity = !!info.data?.universityName;
  const courses = useApi<UniversityCourseDto[]>(hasUniversity ? '/api/university/me/courses' : null);

  const [dept, setDept] = useState('');
  const [year, setYear] = useState('');
  const [courseSearch, setCourseSearch] = useState('');

  const [courseId, setCourseId] = useState('');
  const [type, setType] = useState('');
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounced(search, 350);
  const [page, setPage] = useState(1);
  const materialsPath = hasUniversity
    ? `/api/university/me/materials${qs({ page, pageSize: PAGE_SIZE, search: debouncedSearch.trim(), type, courseId })}`
    : null;
  const materials = useApi<PagedResultDto<MaterialDto>>(materialsPath);

  const [detail, setDetail] = useState<MaterialDto | null>(null);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);

  const all = courses.data ?? [];
  const departments = useMemo(() => Array.from(new Set(all.map((c) => c.department).filter(Boolean))).sort((a, b) => a.localeCompare(b)), [all]);
  const years = useMemo(() => Array.from(new Set(all.map((c) => c.academicYear).filter((y): y is number => y !== null))).sort((a, b) => a - b), [all]);
  const visible = useMemo(() => {
    const q = courseSearch.trim().toLowerCase();
    return all
      .filter((c) => !dept || c.department.toLowerCase() === dept.toLowerCase())
      .filter((c) => !year || c.academicYear === Number(year))
      .filter((c) => !q || c.title.toLowerCase().includes(q) || c.code.toLowerCase().includes(q))
      .sort((a, b) => Number(b.matchesStudentDepartmentYear) - Number(a.matchesStudentDepartmentYear) || a.code.localeCompare(b.code));
  }, [all, dept, year, courseSearch]);

  const download = async (m: MaterialDto) => {
    setDownloading(true);
    setDownloadError(null);
    try {
      await downloadFile(`/api/materials/${m.id}/file`, m.uploadedFileName ?? `${m.title}`);
    } catch (err) {
      setDownloadError(errorMessage(err, 'Could not download the attachment.'));
    } finally {
      setDownloading(false);
    }
  };

  const resetPage = <T,>(setter: (v: T) => void) => (v: T) => {
    setter(v);
    setPage(1);
  };

  return (
    <>
      <PageHeader title="My University" subtitle={info.data?.universityName ?? 'Course materials and summaries'} />
      <ErrorBox error={downloadError} />

      <Async res={info} loadingLabel="Loading university…" isEmpty={(u) => !u.universityName} emptyIcon="landmark" empty="No university assigned to your account yet.">
        {(u) => (
          <>
            <div className="summary-cards summary-cards-3">
              <div className="card">
                <div className="card-icon">
                  <Icon name="landmark" />
                </div>
                <div className="card-info">
                  <span className="card-label">Department</span>
                  <span className="card-value card-value-small">{u.department || '—'}</span>
                  <span className="card-delta">Year {u.academicYear ?? '-'}</span>
                </div>
              </div>
              <div className="card">
                <div className="card-icon">
                  <Icon name="book-open" />
                </div>
                <div className="card-info">
                  <span className="card-label">Courses</span>
                  <span className="card-value">{u.coursesCount}</span>
                </div>
              </div>
              <div className="card">
                <div className="card-icon">
                  <Icon name="file-text" />
                </div>
                <div className="card-info">
                  <span className="card-label">Materials</span>
                  <span className="card-value">{u.materialsCount}</span>
                </div>
              </div>
            </div>

            <div className="card section-gap">
              <div className="card-header">
                <h3>Courses</h3>
                <span className="rec-track">{courses.data ? `${courses.data.length} in your university` : 'All courses'}</span>
              </div>
              <Async res={courses} loadingLabel="Loading courses…" emptyIcon="book-open" empty="No courses are available for your university yet.">
                {() => (
                  <>
                    <div className="uni-course-filters">
                      <div className="form-group">
                        <label htmlFor="courseDepartment">Department</label>
                        <select id="courseDepartment" value={dept} onChange={(e) => setDept(e.target.value)}>
                          <option value="">All departments</option>
                          {departments.map((d, dIdx) => (
                            <option key={`${d}-${dIdx}`} value={d} dir="auto">
                              {d}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div className="form-group">
                        <label htmlFor="courseYear">Year</label>
                        <select id="courseYear" value={year} onChange={(e) => setYear(e.target.value)}>
                          <option value="">All years</option>
                          {years.map((y, yIdx) => (
                            <option key={`${y}-${yIdx}`} value={y}>
                              Year {y}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div className="form-group uni-course-search">
                        <label htmlFor="courseSearch">Search</label>
                        <input id="courseSearch" type="search" value={courseSearch} onChange={(e) => setCourseSearch(e.target.value)} placeholder="Course name or code…" dir="auto" />
                      </div>
                    </div>
                    <div className="uni-course-summary">
                      Showing {visible.length} of {all.length} courses
                    </div>
                    {visible.length === 0 ? (
                      <div className="empty-state">
                        <Icon name="search" /> No courses match the current filters.
                      </div>
                    ) : (
                      <div className="uni-course-list">
                        {visible.map((c) => (
                          <div className="uni-course-item" key={c.id}>
                            <div className="rec-info">
                              <span className="rec-code">{c.code}</span>
                              <span className="uni-course-name" dir="auto">
                                {c.title}
                              </span>
                              <span className="rec-track" dir="auto">
                                {c.department || 'All departments'} · Year {c.academicYear ?? '-'} · {c.semester}
                              </span>
                              {c.instructors && (
                                <span className="rec-track" dir="auto">
                                  Instructor: {c.instructors}
                                </span>
                              )}
                              {c.matchesStudentDepartmentYear && <span className="rec-track">Matches your department/year</span>}
                            </div>
                            <div className="uni-course-actions">
                              {c.status && <span className={`status-badge ${statusClass(c.status)}`}>{c.status === 'InProgress' ? 'In progress' : c.status}</span>}
                              <span className="rec-track">
                                {c.materialsCount} material{c.materialsCount === 1 ? '' : 's'}
                              </span>
                              <button
                                type="button"
                                className="rec-btn"
                                onClick={() => {
                                  setCourseId(String(c.id));
                                  setPage(1);
                                  document.getElementById('materials-card')?.scrollIntoView({ behavior: 'smooth' });
                                }}
                              >
                                Materials
                              </button>
                              <Link to={`/courses/${c.id}`} className="rec-btn">
                                Open
                              </Link>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </>
                )}
              </Async>
            </div>

            <div className="card" id="materials-card">
              <div className="card-header">
                <h3>Materials</h3>
              </div>
              <div className="form-inline">
                <div className="form-group">
                  <label htmlFor="courseFilter">Course</label>
                  <select id="courseFilter" value={courseId} onChange={(e) => resetPage(setCourseId)(e.target.value)}>
                    <option value="">All courses</option>
                    {all.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.title} ({c.materialsCount}){c.instructors ? ` - ${c.instructors}` : ''}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-group">
                  <label htmlFor="typeFilter">Type</label>
                  <select id="typeFilter" value={type} onChange={(e) => resetPage(setType)(e.target.value)}>
                    <option value="">All types</option>
                    {MATERIAL_TYPES.map((t, tIdx) => (
                      <option key={`${t}-${tIdx}`} value={t}>
                        {t}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-group">
                  <label htmlFor="materialSearch">Search</label>
                  <input id="materialSearch" value={search} onChange={(e) => resetPage(setSearch)(e.target.value)} placeholder="Title or description…" />
                </div>
              </div>
              <Async res={materials} loadingLabel="Loading materials…" isEmpty={(p) => p.items.length === 0} emptyIcon="file-text" empty="No materials match the current filters.">
                {(p) => (
                  <>
                    <div className="recommendation-list">
                      {p.items.map((m) => (
                        <div className={`recommendation-item clickable ${detail?.id === m.id ? 'selected' : ''}`} key={m.id} onClick={() => setDetail(m)}>
                          <div className="rec-info">
                            <span className="rec-code">{m.type}</span>
                            <span className="rec-title">{m.title}</span>
                            <span className="rec-track">
                              {m.courseName}
                              {m.instructorName ? ` · ${m.instructorName}` : ''} · {fmtDate(m.createdAt)}
                            </span>
                          </div>
                          {m.hasUploadedFile && (
                            <button
                              type="button"
                              className="rec-btn"
                              disabled={downloading}
                              onClick={(e) => {
                                e.stopPropagation();
                                void download(m);
                              }}
                            >
                              Download
                            </button>
                          )}
                          <button
                            type="button"
                            className="rec-btn"
                            onClick={(e) => {
                              e.stopPropagation();
                              setDetail(m);
                            }}
                          >
                            Open
                          </button>
                        </div>
                      ))}
                    </div>
                    <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
                  </>
                )}
              </Async>
            </div>
          </>
        )}
      </Async>

      {detail && <MaterialDetail id={detail.id} onClose={() => setDetail(null)} onDownload={download} downloading={downloading} />}
    </>
  );
}

/** Loads one material via GET /api/university/me/materials/{id} and shows it. */
function MaterialDetail({ id, onClose, onDownload, downloading }: { id: number; onClose: () => void; onDownload: (m: MaterialDto) => void; downloading: boolean }) {
  const res = useApi<MaterialDto>(`/api/university/me/materials/${id}`);
  const m = res.data;
  return (
    <Modal
      title={m?.title ?? 'Material'}
      onClose={onClose}
      wide
      footer={
        <>
          {m?.hasUploadedFile && (
            <button className="btn btn-primary" disabled={downloading} onClick={() => onDownload(m)}>
              <Icon name="download" /> {downloading ? 'Downloading…' : 'Download attachment'}
            </button>
          )}
          {m?.fileUrl && (
            <a className="btn btn-secondary" href={m.fileUrl} target="_blank" rel="noopener noreferrer">
              Open link
            </a>
          )}
          <button className="btn btn-secondary" onClick={onClose}>
            Back
          </button>
        </>
      }
    >
      <Async res={res} loadingLabel="Loading material…">
        {(d) => (
          <>
            <p>
              <span className="status-badge good">{d.type}</span> {d.courseName}
              {d.instructorName ? ` · ${d.instructorName}` : ''} · {fmtDate(d.createdAt)}
            </p>
            {!d.description && !d.content && !d.fileUrl && !d.hasUploadedFile ? (
              <div className="empty-state">This material has no description or content yet.</div>
            ) : (
              <>
                {d.description && <p dir="auto">{d.description}</p>}
                {d.content && (
                  <div className="material-content" dir="auto">
                    {d.content}
                  </div>
                )}
                {d.hasUploadedFile && (
                  <p className="course-meta" dir="auto">
                    Attachment: {d.uploadedFileName} {d.uploadedFileSizeBytes ? `(${fmtBytes(d.uploadedFileSizeBytes)})` : ''}
                  </p>
                )}
              </>
            )}
          </>
        )}
      </Async>
    </Modal>
  );
}
