import { EventList } from '../../components/EventList';
import { Async, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import type { EventDto } from '../../types/dto';

export default function GradEvents() {
  usePageTitle('Graduated Events');
  const events = useApi<EventDto[]>('/api/events?upcoming=true');
  return (
    <>
      <PageHeader title="Graduated Events" subtitle="Reconnect with your university community." />
      <div className="card">
        <Async res={events} loadingLabel="Loading events…" emptyIcon="calendar-days" empty="No upcoming graduate events right now.">
          {(list) => <EventList events={list} />}
        </Async>
      </div>
    </>
  );
}
