using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class ChatGroundingModeTests : IClassFixture<RagApiFactory>
{
    private readonly RagApiFactory _factory;

    public ChatGroundingModeTests(RagApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("rag")]
    [InlineData("rag-local")]
    public async Task RagChat_SendsStrictRules_NotOpenKnowledgeRules(string provider)
    {
        _factory.FakeRag.Reset();

        var response = await _factory.StudentClient.PostAsJsonAsync(
            "/api/chat/conversations/new/messages", new { provider, content = "طريقة عمل السلطة" });
        await response.Content.ReadAsStringAsync();

        var prompt = _factory.FakeRag.LastSystemPrompt;
        Assert.NotNull(prompt);
        Assert.Contains("STRICT GROUNDING RULES", prompt);
        Assert.DoesNotContain("HYBRID GROUNDING RULES", prompt);
        Assert.DoesNotContain("From general knowledge", prompt);
    }
}
