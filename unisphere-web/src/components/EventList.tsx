import { Icon } from './Icon';
import { fmtTime, parseDate } from '../lib/format';

interface EventLike {
  id: number;
  title: string;
  location: string;
  startsAtUtc: string;
  description?: string | null;
}

export function EventList({ events }: { events: EventLike[] }) {
  return (
    <div className="event-list">
      {events.map((evt) => {
        const d = parseDate(evt.startsAtUtc);
        return (
          <div className="event-item" key={evt.id}>
            <div className="event-date">
              <span className="event-day">{d ? d.getDate() : ''}</span>
              <span className="event-month">{d ? d.toLocaleDateString(undefined, { month: 'short' }) : ''}</span>
            </div>
            <div className="event-info">
              <h4>{evt.title}</h4>
              <p>
                <Icon name="map-pin" /> {evt.location}
              </p>
              <p>
                <Icon name="clock-3" /> {d ? d.toLocaleDateString(undefined, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' }) : ''} · {fmtTime(evt.startsAtUtc)}
              </p>
              {evt.description && <p>{evt.description}</p>}
            </div>
          </div>
        );
      })}
    </div>
  );
}
