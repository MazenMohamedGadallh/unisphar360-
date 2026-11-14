import { EventList } from '../../components/EventList';
import { Async, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import type { EventDto } from '../../types/dto';

export default function Events() {
  usePageTitle('Student Events');
  const events = useApi<EventDto[]>('/api/events?upcoming=true');
  return (
    <>
      <PageHeader title="Student Events" subtitle="Upcoming events at your university." />
      <div className="card">
        <Async res={events} loadingLabel="Loading events…" emptyIcon="calendar-days" empty="No upcoming events at your university right now.">
          {(list) => <EventList events={list} />}
        </Async>
      </div>
    </>
  );
}
