using System.Runtime.CompilerServices;

namespace UniSphere.Api.Services;

/// <summary>Student-only chat provider backed by the existing RAG service.
/// It is intentionally not registered in <see cref="ChatProviderRegistry"/>, so
/// unrelated AI features cannot select it by accident.</summary>
public sealed class RagChatProvider : IChatProvider
{
    private readonly IRagClient _rag;

    public RagChatProvider(IRagClient rag)
    {
        _rag = rag;
    }

    public string Name => "rag";
    public string ModelName => _rag.ModelName;
    public bool IsConfigured => _rag.IsConfigured;

    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var question = request.Messages
            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? "";

        RagAnswer answer;
        try
        {
            answer = await _rag.QueryAsync(
                question,
                request.RagDocuments,
                request.SystemPrompt,
                ct);
        }
        catch (RagUnavailableException ex)
        {
            throw new ChatProviderException(ex.Message);
        }
        catch (RagInvalidResponseException ex)
        {
            throw new ChatProviderException(ex.Message);
        }

        var text = answer.Answer.Trim();
        if (answer.Sources.Count > 0)
        {
            var references = answer.Sources
                .Select(s => s.Page.HasValue ? $"- {s.Title} (page {s.Page})" : $"- {s.Title}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            text += "\n\nSources:\n" + string.Join("\n", references);
        }

        yield return text;
    }
}
