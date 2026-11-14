import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, PageHeader, Tabs } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useApi, usePageTitle } from '../../lib/hooks';
import { fmtDateTime } from '../../lib/format';
import type { NotificationDto } from '../../types/dto';

/** Tell the top bar to refresh its unread badge. */
function notifyBadge() {
  window.dispatchEvent(new Event('unisphere:notifications-changed'));
}

export default function Notifications() {
  usePageTitle('Notifications');
  const list = useApi<NotificationDto[]>('/api/notifications');
  const [filter, setFilter] = useState<'all' | 'unread'>('all');
  const [error, setError] = useState<string | null>(null);

  const markRead = async (id: number) => {
    setError(null);
    try {
      await api.post(`/api/notifications/${id}/read`);
      list.setData((l) => l?.map((n) => (n.id === id ? { ...n, isRead: true } : n)));
      notifyBadge();
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  const markAll = async () => {
    setError(null);
    try {
      await api.post('/api/notifications/read-all');
      list.setData((l) => l?.map((n) => ({ ...n, isRead: true })));
      notifyBadge();
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  const remove = async (id: number) => {
    setError(null);
    try {
      await api.del(`/api/notifications/${id}`);
      list.setData((l) => l?.filter((n) => n.id !== id));
      notifyBadge();
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  const unread = (list.data ?? []).filter((n) => !n.isRead).length;

  return (
    <>
      <PageHeader title="Notifications" subtitle="Stay updated with the latest announcements and reminders." />
      <ErrorBox error={error} />
      <div className="card">
        <div className="card-header">
          <h3>All Notifications</h3>
          <button className="btn btn-sm btn-secondary" onClick={() => void markAll()} disabled={unread === 0}>
            Mark All as Read
          </button>
        </div>
        <div className="card-body-tight">
          <Tabs
            tabs={[
              { key: 'all', label: `All (${list.data?.length ?? 0})` },
              { key: 'unread', label: `Unread (${unread})` },
            ]}
            value={filter}
            onChange={setFilter}
          />
        </div>
        <Async res={list} loadingLabel="Loading notifications…" emptyIcon="bell" empty="You have no notifications.">
          {(items) => {
            const shown = filter === 'unread' ? items.filter((n) => !n.isRead) : items;
            if (shown.length === 0) return <div className="empty-state">No unread notifications. You&apos;re all caught up!</div>;
            return (
              <div className="notification-list">
                {shown.map((n) => (
                  <div key={n.id} className={`notification-item ${n.isRead ? '' : 'unread'}`} onClick={() => !n.isRead && void markRead(n.id)}>
                    <div className="notification-indicator">{!n.isRead && <span className="unread-dot" />}</div>
                    <div className="notification-content">
                      <h4>{n.title}</h4>
                      <p>{n.body}</p>
                      <span className="notification-time">
                        {fmtDateTime(n.createdAtUtc)}
                        {n.category ? ` · ${n.category}` : ''}
                      </span>
                    </div>
                    <div className="row-actions">
                      {!n.isRead && (
                        <button
                          className="btn btn-sm btn-link"
                          onClick={(e) => {
                            e.stopPropagation();
                            void markRead(n.id);
                          }}
                        >
                          Mark Read
                        </button>
                      )}
                      <button
                        className="icon-btn icon-btn-danger"
                        title="Delete notification"
                        aria-label="Delete notification"
                        onClick={(e) => {
                          e.stopPropagation();
                          void remove(n.id);
                        }}
                      >
                        <Icon name="trash-2" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            );
          }}
        </Async>
      </div>
    </>
  );
}
