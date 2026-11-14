import { EventList } from '../../components/EventList';
import { Async, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import { fmtDate } from '../../lib/format';
import type { EventDto } from '../../types/dto';
import type { AnnouncementItem } from '../../types/models';

const FAQS = [
  {
    q: 'How do I enroll in a course?',
    a: "Go to the Courses page, find a course you're interested in, and click the Enroll button. You can also ask your academic advisor for recommendations.",
  },
  {
    q: 'How is my GPA calculated?',
    a: 'GPA is calculated based on your grade points divided by total credits. Each grade letter corresponds to a point value (A=4.0, B+=3.3, B=3.0, etc.).',
  },
  {
    q: 'How do I change my study plan?',
    a: 'Navigate to the Study Planner page. You can add study items, tick them off and delete them. Changes are saved automatically.',
  },
  {
    q: 'How do I apply for graduation?',
    a: 'Open the Academic Advisor page. The Graduation card shows your degree audit; once every requirement is met you can submit your application for registrar review.',
  },
  { q: 'Who do I contact for academic support?', a: 'Reach out to your academic advisor or visit the Student Services office during working hours.' },
];

export default function StudentServices() {
  usePageTitle('Student Services');
  const announcements = useApi<AnnouncementItem[]>('/api/announcements');
  const events = useApi<EventDto[]>('/api/events?upcoming=true');

  return (
    <>
      <PageHeader title="Student Services" subtitle="Events, announcements, FAQs, and support." />

      <div className="card section-gap">
        <div className="card-header">
          <h3>Announcements</h3>
        </div>
        <Async res={announcements} loadingLabel="Loading announcements…" emptyIcon="megaphone" empty="No announcements at this time.">
          {(list) => (
            <div className="announcement-list">
              {list.map((a) => (
                <div className="announcement-item" key={a.id}>
                  <div className="announcement-header">
                    <h4>{a.title}</h4>
                    <span className="announcement-category">
                      {a.category ?? 'General'}
                      {a.courseName ? ` - ${a.courseName}` : ''}
                    </span>
                  </div>
                  <p>{a.body}</p>
                  <span className="announcement-date">{fmtDate(a.createdAtUtc)}</span>
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>

      <div className="card section-gap">
        <div className="card-header">
          <h3>Upcoming Events</h3>
        </div>
        <Async res={events} loadingLabel="Loading events…" emptyIcon="calendar-days" empty="No upcoming events.">
          {(list) => <EventList events={list} />}
        </Async>
      </div>

      <div className="card">
        <div className="card-header">
          <h3>Frequently Asked Questions</h3>
        </div>
        <div className="faq-list">
          {FAQS.map((f, fIdx) => (
            <details className="faq-item" key={`${f.q}-${fIdx}`}>
              <summary>
                <h4>{f.q}</h4>
              </summary>
              <p>{f.a}</p>
            </details>
          ))}
        </div>
      </div>
    </>
  );
}
