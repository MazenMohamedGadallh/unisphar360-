namespace UniSphere.Api.Services;

/// <summary>
/// Circuit-scoped sync so pages that change notification state (read/delete,
/// read-all) can refresh the layout badge without a full page reload. Blazor
/// Server keeps one instance per SignalR circuit, so the event only reaches
/// the current user's session.
/// </summary>
public class SharedUnreadBadgeState
{
    public int UnreadCount { get; private set; }

    private event Action? Changed;

    public void Publish(int unreadCount)
    {
        UnreadCount = Math.Max(0, unreadCount);
        Changed?.Invoke();
    }

    public void Subscribe(Action handler) => Changed += handler;
    public void Unsubscribe(Action handler) => Changed -= handler;
}
