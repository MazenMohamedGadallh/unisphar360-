using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>Mocked-HTTP coverage for the shared server-side RAG client. These
/// tests never contact the local RAG service or a real model provider.</summary>
public class RagClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return _respond(request);
        }
    }

    private static RagClient CreateClient(
        ScriptedHandler handler,
        bool enabled = true,
        string baseUrl = "http://rag.test/",
        int topK = 3)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        return new RagClient(
            http,
            Options.Create(new RagOptions
            {
                Enabled = enabled,
                BaseUrl = baseUrl,
                TopK = topK,
                TimeoutSeconds = 5,
                DisplayName = "Test RAG",
                ModelName = "test-rag-model"
            }),
            NullLogger<RagClient>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task Query_ParsesAnswerAndSources_AndSendsSnakeCasePayload()
    {
        const string responseJson = """
            {
              "answer": "Grounded answer",
              "sources": [
                {
                  "title": "Lecture 1",
                  "source": "lecture.pdf",
                  "page": 2,
                  "snippet": "A useful excerpt",
                  "distance": 0.25
                }
              ],
              "grounded": true,
              "provider": "groq",
              "model": "test-model"
            }
            """;
        var handler = new ScriptedHandler(_ => Json(responseJson));
        var client = CreateClient(handler);

        var answer = await client.QueryAsync(
            "What is supervised learning?",
            new[]
            {
                new RagDocument("1", "Lecture 1", "Supervised learning uses labels.",
                    new Dictionary<string, string> { ["courseCode"] = "CS101" })
            },
            "Answer only from context.",
            CancellationToken.None);

        Assert.Equal("Grounded answer", answer.Answer);
        Assert.True(answer.Grounded);
        Assert.Equal("groq", answer.Provider);
        Assert.Equal("test-model", answer.Model);
        var source = Assert.Single(answer.Sources);
        Assert.Equal("Lecture 1", source.Title);
        Assert.Equal(2, source.Page);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("\"top_k\":3", body);
        Assert.Contains("\"system_prompt\":\"Answer only from context.\"", body);
        Assert.Contains("\"courseCode\":\"CS101\"", body);
        Assert.Equal("query", handler.Requests[0].RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task Retrieve_ParsesContextAndSources()
    {
        const string responseJson = """
            {
              "context": "Retrieved context",
              "sources": [
                {
                  "title": "Lecture 2",
                  "source": null,
                  "page": null,
                  "snippet": "Another excerpt",
                  "distance": 0.5
                }
              ],
              "provider": "groq",
              "embedding_model": "MiniLM"
            }
            """;
        var handler = new ScriptedHandler(_ => Json(responseJson));
        var client = CreateClient(handler);

        var retrieval = await client.RetrieveAsync(
            "exam topic",
            new[] { new RagDocument("2", "Lecture 2", "Content") },
            2,
            CancellationToken.None);

        Assert.Equal("Retrieved context", retrieval.Context);
        Assert.Equal("MiniLM", retrieval.EmbeddingModel);
        Assert.Single(retrieval.Sources);
        Assert.Contains("\"top_k\":2", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task HttpError_IsMappedToSafeUnavailableException()
    {
        var handler = new ScriptedHandler(_ => Json("{\"detail\":\"upstream failed\"}", HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<RagUnavailableException>(() =>
            client.QueryAsync("question", Array.Empty<RagDocument>(), null, CancellationToken.None));

        Assert.DoesNotContain("upstream failed", ex.Message);
        Assert.Contains("unavailable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unauthorized_IsMappedToCredentialMessage()
    {
        var handler = new ScriptedHandler(_ => Json("{\"detail\":\"bad key\"}", HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<RagUnavailableException>(() =>
            client.QueryAsync("question", Array.Empty<RagDocument>(), null, CancellationToken.None));

        Assert.Contains("credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bad key", ex.Message);
    }

    [Fact]
    public async Task InvalidJson_IsMappedToInvalidResponseException()
    {
        var handler = new ScriptedHandler(_ => Json("not-json"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<RagInvalidResponseException>(() =>
            client.QueryAsync("question", Array.Empty<RagDocument>(), null, CancellationToken.None));
    }

    [Fact]
    public async Task DisabledClient_DoesNotSendRequest()
    {
        var handler = new ScriptedHandler(_ => Json("{}"));
        var client = CreateClient(handler, enabled: false);

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<RagUnavailableException>(() =>
            client.QueryAsync("question", Array.Empty<RagDocument>(), null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}
