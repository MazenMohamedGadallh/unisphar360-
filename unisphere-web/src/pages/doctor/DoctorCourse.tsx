import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, BarList, PageHeader, StatCard, Tabs } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { Link, useParams } from '../../lib/router';
import type { DoctorCourseAnalyticsDto, DoctorCourseDetailDto } from '../../types/dto';
import { AnnouncementsManager } from './AnnouncementsManager';
import { AlertsTable } from './DoctorDashboard';
import { MaterialsManager } from './MaterialsManager';
import { StudentsTable } from './CourseStudents';

type Tab = 'students' | 'analytics' | 'materials' | 'announcements';

function Analytics({ courseId }: { courseId: number }) {
  const res = useApi<DoctorCourseAnalyticsDto>(`/api/doctor/courses/${courseId}/analytics`);
  return (
    <Async res={res} loadingLabel="Loading analytics…">
      {(a) => (
        <>
          <div className="summary-cards summary-cards-4">
            <StatCard icon="users" label="Enrolled" value={a.enrollmentCount} />
            <StatCard icon="check" label="Graded" value={a.gradedCount} />
            <StatCard icon="award" label="Average Grade" value={a.averageGradePoints?.toFixed(2) ?? '—'} />
            <StatCard icon="calendar-days" label="Average Attendance" value={a.averageAttendance !== null ? `${a.averageAttendance.toFixed(1)}%` : '—'} />
          </div>
          <div className="two-column">
            <div className="card">
              <div className="card-header">
                <h3>Grade Distribution</h3>
              </div>
              <BarList items={a.gradeDistribution.map((b) => ({ label: b.label, value: b.count }))} max={Math.max(1, a.enrollmentCount)} />
            </div>
            <div className="card">
              <div className="card-header">
                <h3>Attendance Distribution</h3>
              </div>
              <BarList items={a.attendanceDistribution.map((b) => ({ label: b.label, value: b.count }))} max={Math.max(1, a.enrollmentCount)} />
            </div>
          </div>
          <div className="card">
            <div className="card-header">
              <h3>Students Needing Support</h3>
            </div>
            <AlertsTable alerts={a.alerts} />
          </div>
        </>
      )}
    </Async>
  );
}

export default function DoctorCourse() {
  const { courseId } = useParams<{ courseId: string }>();
  const id = Number(courseId);
  const course = useApi<DoctorCourseDetailDto>(`/api/doctor/courses/${id}`);
  const [tab, setTab] = useState<Tab>('students');
  usePageTitle(course.data?.code ?? 'Course');

  return (
    <>
      <div className="page-header">
        <Link to="/doctor/courses" className="back-link">
          <Icon name="arrow-left" /> My Courses
        </Link>
      </div>
      <Async res={course} loadingLabel="Loading course…">
        {(c) => (
          <>
            <PageHeader
              title={`${c.code} — ${c.title}`}
              subtitle={
                <>
                  {c.description}
                  <br />
                  <span className="rec-track">
                    {[c.department, c.academicYear ? `Year ${c.academicYear}` : null, c.semester, `${c.credits} credits`, c.track].filter(Boolean).join(' · ')}
                  </span>
                </>
              }
            />
            <Tabs
              tabs={[
                { key: 'students', label: 'Students' },
                { key: 'analytics', label: 'Analytics' },
                { key: 'materials', label: 'Materials & Assignments' },
                { key: 'announcements', label: 'Announcements' },
              ]}
              value={tab}
              onChange={setTab}
            />
            {tab === 'students' && <StudentsTable courseId={id} />}
            {tab === 'analytics' && <Analytics courseId={id} />}
            {tab === 'materials' && <MaterialsManager fixedCourseId={id} />}
            {tab === 'announcements' && <AnnouncementsManager fixedCourseId={id} />}
          </>
        )}
      </Async>
    </>
  );
}
