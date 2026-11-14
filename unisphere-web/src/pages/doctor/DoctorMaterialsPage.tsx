import { Async, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import type { DoctorCourseDto } from '../../types/dto';
import { AnnouncementsManager } from './AnnouncementsManager';
import { MaterialsManager } from './MaterialsManager';

export function DoctorMaterialsPage() {
  usePageTitle('Materials');
  const courses = useApi<DoctorCourseDto[]>('/api/doctor/courses');
  return (
    <>
      <PageHeader title="Materials" subtitle="Publish summaries, lectures, assignments and exams for your own courses." />
      <Async res={courses} loadingLabel="Loading your courses…" emptyIcon="book-open" empty="Material management becomes available when the administrator assigns you to a course.">
        {(list) => <MaterialsManager courses={list} />}
      </Async>
    </>
  );
}

export function DoctorAnnouncementsPage() {
  usePageTitle('Announcements');
  const courses = useApi<DoctorCourseDto[]>('/api/doctor/courses');
  return (
    <>
      <PageHeader title="Announcements" subtitle="Post updates to the students enrolled in your courses." />
      <Async res={courses} loadingLabel="Loading your courses…" emptyIcon="megaphone" empty="Announcements become available when the administrator assigns you to a course.">
        {(list) => <AnnouncementsManager courses={list} />}
      </Async>
    </>
  );
}
