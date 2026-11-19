namespace UniSphere.Api.Services;

/// <summary>Cached health probe for the Python RAG service. The chat
/// provider list is requested on every page load, so pinging the service
/// each time would add a network round-trip (or a timeout wait when it is
/// down) to every request. Results are cached for <see cref="CacheDuration"/>;
/// a probe is skipped entirely after <c>RagApi:Enabled=false</c>.
/// Registered as a singleton so one cache covers all requests.</summary>
public sealed class RagHealthCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private bool? _healthy;
    private DateTime _checkedAtUtc = DateTime.MinValue;

    public async Task<bool> IsHealthyAsync(IRagClient rag, CancellationToken ct)
    {
        // Not configured (disabled / no base URL): never reachable, no probe.
        if (!rag.IsConfigured) return false;

        if (TryReadCache(out var cached)) return cached;

        // One probe at a time so concurrent page loads don't all hit the
        // Python service; the others re-check the cache and reuse the result.
        await _probeLock.WaitAsync(ct);
        try
        {
            if (TryReadCache(out cached)) return cached;

            bool healthy;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                healthy = await rag.IsHealthyAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Probe exceeded 5s: treat as down rather than stalling the list.
                healthy = false;
            }

            lock (_gate)
            {
                _healthy = healthy;
                _checkedAtUtc = DateTime.UtcNow;
            }
            return healthy;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private bool TryReadCache(out bool healthy)
    {
        lock (_gate)
        {
            if (_healthy is not null && DateTime.UtcNow - _checkedAtUtc < CacheDuration)
            {
                healthy = _healthy.Value;
                return true;
            }
        }
        healthy = false;
        return false;
    }
}
