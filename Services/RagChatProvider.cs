using System.Runtime.CompilerServices;

namespace UniSphere.Api.Services;

public sealed class RagChatProvider : IChatProvider, IProviderStatus
{
    private readonly IRagClient _rag;

    public RagChatProvider(IRagClient rag)
    {
        _rag = rag;
    }

    public string Name => "rag";
    public string ModelName => _rag.ModelName;
    public bool IsConfigured => _rag.IsConfigured;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            return await _rag.IsHealthyAsync(timeout.Token);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var question = request.Messages
            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? "";

        // Stream the answer as it is written; the sources list comes last.
        await using var events = _rag.QueryStreamAsync(question, request.RagDocuments, request.SystemPrompt, ct)
            .GetAsyncEnumerator(ct);
        IReadOnlyList<RagSource>? sources = null;
        var any = false;
        while (true)
        {
            RagStreamEvent item;
            try
            {
                if (!await events.MoveNextAsync()) break;
                item = events.Current;
            }
            catch (RagUnavailableException ex) { throw new ChatProviderException(ex.Message); }
            catch (RagInvalidResponseException ex) { throw new ChatProviderException(ex.Message); }

            if (item.Sources is not null) sources = item.Sources;
            if (string.IsNullOrEmpty(item.Delta)) continue;
            // Trim leading whitespace of the answer only, never inside it.
            var text = any ? item.Delta : item.Delta.TrimStart();
            if (text.Length == 0) continue;
            any = true;
            yield return text;
        }

        if (any && sources is { Count: > 0 })
        {
            var references = sources
                .Select(s => s.Page.HasValue ? $"- {s.Title} (page {s.Page})" : $"- {s.Title}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            yield return "\n\nSources:\n" + string.Join("\n", references);
        }
    }
}
