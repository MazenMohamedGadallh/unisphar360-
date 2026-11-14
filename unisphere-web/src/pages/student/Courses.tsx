import { useMemo, useState } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ErrorBox, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import { Link, useNavigate, useParams } from '../../lib/router';
import type { CourseDto, EnrollmentDto } from '../../types/dto';

export function CourseList() {
  usePageTitle('Courses');
  const navigate = useNavigate();
  const courses = useApi<CourseDto[]>('/api/courses');
  const recommended = useApi<CourseDto[]>('/api/courses/recommended');
  const [query, setQuery] = useState('');
  const [track, setTrack] = useState('');

  const tracks = useMemo(() => Array.from(new Set((courses.data ?? []).map((c) => c.track).filter(Boolean))).sort(), [courses.data]);
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (courses.data ?? []).filter(
      (c) =>
        (!track || c.track === track) &&
        (!q || c.title.toLowerCase().includes(q) || c.code.toLowerCase().includes(q) || c.description.toLowerCase().includes(q)),
    );
  }, [courses.data, query, track]);

  return (
    <>
      <PageHeader
        title="Courses"
        subtitle="Browse available courses and enroll."
        actions={
          <Link to="/enrollments" className="btn btn-secondary btn-sm">
            My enrollments
          </Link>
        }
      />

      {recommended.data && recommended.data.length > 0 && (
        <div className="card section-gap">
          <div className="card-header">
            <h3>
              <Icon name="lightbulb" /> Recommended for you
            </h3>
          </div>
          <div className="recommendation-list">
            {recommended.data.slice(0, 4).map((c) => (
              <div className="recommendation-item" key={c.id}>
                <div className="rec-info">
                  <span className="rec-code">{c.code}</span>
                  <span className="rec-title">{c.title}</span>
                  <span className="rec-track">
                    {c.track} | {c.credits} credits
                  </span>
                </div>
                <Link to={`/courses/${c.id}`} className="rec-btn">
                  View
                </Link>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="search-section filter-row">
        <div className="search-box-large">
          <span className="search-icon">
            <Icon name="search" />
          </span>
          <input type="text" placeholder="Search courses…" value={query} onChange={(e) => setQuery(e.target.value)} />
        </div>
        {tracks.length > 1 && (
          <select value={track} onChange={(e) => setTrack(e.target.value)} aria-label="Filter by track">
            <option value="">All tracks</option>
            {tracks.map((t, tIdx) => (
              <option key={`${t}-${tIdx}`} value={t}>
                {t}
              </option>
            ))}
          </select>
        )}
      </div>

      <div className="card">
        <div className="card-header">
          <h3>Available Courses</h3>
          {courses.data && <span className="muted">{filtered.length} shown</span>}
        </div>
        <Async res={courses} loadingLabel="Loading courses…" emptyIcon="book-open" empty="No courses found.">
          {() =>
            filtered.length === 0 ? (
              <div className="empty-state">No courses match your search.</div>
            ) : (
              <div className="course-grid">
                {filtered.map((c) => (
                  <div
                    className="course-card"
                    key={c.id}
                    role="link"
                    tabIndex={0}
                    onClick={() => navigate(`/courses/${c.id}`)}
                    onKeyDown={(e) => e.key === 'Enter' && navigate(`/courses/${c.id}`)}
                  >
                    <div className="course-card-header">
                      <span className="course-code">{c.code}</span>
                      <span className="course-track">{c.track}</span>
                    </div>
                    <h4>{c.title}</h4>
                    <p>{c.description}</p>
                    {c.instructors && <p className="course-meta">Instructor: {c.instructors}</p>}
                    <div className="course-footer">
                      <span>{c.credits} credits</span>
                      <span className="view-link">
                        View <Icon name="arrow-right" />
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            )
          }
        </Async>
      </div>
    </>
  );
}

export function CourseDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const course = useApi<CourseDto>(`/api/courses/${id}`);
  const action = useAction();
  usePageTitle(course.data ? course.data.title : 'Course');

  const enroll = () =>
    action.run(async () => {
      await api.post<EnrollmentDto>('/api/enrollments', { courseId: Number(id) });
      navigate('/enrollments');
    });

  return (
    <>
      <div className="page-header">
        <Link to="/courses" className="back-link">
          <Icon name="arrow-left" /> Back to Courses
        </Link>
      </div>
      <ErrorBox error={action.error} />
      <Async res={course} loadingLabel="Loading course…">
        {(c) => (
          <div className="card course-detail">
            <div className="card-header">
              <div>
                <h2>
                  {c.code} - {c.title}
                </h2>
                <p className="course-meta">
                  {c.track} | {c.credits} Credits
                </p>
                {c.instructors && <p className="course-meta">Instructor: {c.instructors}</p>}
              </div>
              <button className="btn btn-primary" onClick={() => void enroll()} disabled={action.busy}>
                <Icon name="plus" /> {action.busy ? 'Enrolling…' : 'Enroll'}
              </button>
            </div>
            <div className="card-body">
              <h4>Description</h4>
              <p>{c.description || 'No description provided.'}</p>
            </div>
          </div>
        )}
      </Async>
      {action.error?.toLowerCase().includes('already') && (
        <Alert kind="info">
          You are already enrolled. <Link to="/enrollments">View your enrollments</Link>.
        </Alert>
      )}
    </>
  );
}
