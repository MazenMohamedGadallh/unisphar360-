namespace UniSphere.Api.Services;

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
