using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

public class RagChatProviderTests
{
    private static ChatRequest Request() => new()
    {
        Provider = "rag",
        Model = "test-rag-model",
        SystemPrompt = "Use the supplied material.",
        Messages = new List<ChatTurn>
        {
            new() { Role = "user", Content = "Explain supervised learning." }
        },
        RagDocuments = new List<RagDocument>
        {
            new("1", "Lecture 1", "Supervised learning uses labeled examples.")
        }
    };

    private static async Task<string> CollectAsync(RagChatProvider provider, ChatRequest request)
    {
        var parts = new List<string>();
        await foreach (var token in provider.StreamAsync(request, CancellationToken.None))
            parts.Add(token);
        return string.Concat(parts);
    }

    [Fact]
    public async Task Stream_ReturnsRagAnswerAndSourceReferences()
    {
        var fake = new FakeRagClient();
        var provider = new RagChatProvider(fake);

        var text = await CollectAsync(provider, Request());

        Assert.Contains("test RAG answer", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sources:", text);
        Assert.Contains("Test Material", text);
        Assert.Equal(1, fake.QueryCalls);
        Assert.Single(fake.LastQueryDocuments);
    }

    [Fact]
    public async Task Stream_UnavailableRag_ThrowsSafeChatProviderException()
    {
        var fake = new FakeRagClient { ThrowUnavailable = true };
        var provider = new RagChatProvider(fake);

        var ex = await Assert.ThrowsAsync<ChatProviderException>(() =>
            CollectAsync(provider, Request()));

        Assert.Contains("unavailable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fake.QueryCalls);
    }
}
