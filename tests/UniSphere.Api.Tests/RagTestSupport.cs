using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Tests;

/// <summary>Deterministic test double for the server-side RAG contract.</summary>
public sealed class FakeRagClient : IRagClient
{
    public bool IsConfigured => true;
    public string DisplayName => "Test RAG";
    public string ModelName => "test-rag-model";

    public bool ThrowUnavailable { get; set; }
    public bool ThrowInvalid { get; set; }
    public int QueryCalls { get; private set; }
    public int RetrieveCalls { get; private set; }
    public List<RagDocument> LastQueryDocuments { get; private set; } = new();
    public List<RagDocument> LastRetrieveDocuments { get; private set; } = new();
    public string? LastSystemPrompt { get; private set; }

    public void Reset()
    {
        ThrowUnavailable = false;
        ThrowInvalid = false;
        QueryCalls = 0;
        RetrieveCalls = 0;
        LastQueryDocuments = new();
        LastRetrieveDocuments = new();
        LastSystemPrompt = null;
    }

    public Task<RagAnswer> QueryAsync(
        string question,
        IReadOnlyList<RagDocument> documents,
        string? systemPrompt,
        CancellationToken ct)
    {
        QueryCalls++;
        LastQueryDocuments = documents.ToList();
        LastSystemPrompt = systemPrompt;
        if (ThrowUnavailable)
            throw new RagUnavailableException("The RAG service is unavailable right now. Please try again later.");
        if (ThrowInvalid)
            throw new RagInvalidResponseException("The RAG service returned an invalid response.");

        return Task.FromResult(new RagAnswer(
            "The test RAG answer is grounded in the supplied course material.",
            new List<RagSource>
            {
                new("Test Material", "test-material", 1, "A short test snippet.", 0.25)
            },
            true,
            "fake-groq",
            "test-rag-model"));
    }

    public Task<RagRetrieval> RetrieveAsync(
        string query,
        IReadOnlyList<RagDocument> documents,
        int? topK,
        CancellationToken ct)
    {
        RetrieveCalls++;
        LastRetrieveDocuments = documents.ToList();
        if (ThrowUnavailable)
            throw new RagUnavailableException("The RAG service is unavailable right now. Please try again later.");
        if (ThrowInvalid)
            throw new RagInvalidResponseException("The RAG service returned an invalid response.");

        return Task.FromResult(new RagRetrieval(
            "Retrieved test context from the selected material.",
            new List<RagSource>
            {
                new("Test Material", "test-material", 1, "A short test snippet.", 0.25)
            },
            "fake-groq",
            "test-embedding-model"));
    }

    public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(!ThrowUnavailable);
}

/// <summary>Minimal deterministic doctor AI double used only for exam tests.</summary>
public sealed class FakeDoctorAiService : IDoctorAiService
{
    public string? LastUserPrompt { get; private set; }

    public Task<IReadOnlyList<ChatProviderDto>> GetProvidersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ChatProviderDto>>(
            new[] { new ChatProviderDto("fake", "Fake AI", "fake-model") });

    public string GetModelName() => "fake-model";

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        LastUserPrompt = userPrompt;
        return Task.FromResult(
            """
            {"questions":[{"questionText":"What is supervised learning?","questionType":"MCQ","correctAnswer":"Learning from labeled examples","explanation":"The material defines supervised learning with labeled examples.","sourceMaterial":"Test Material","optionsJson":"[\"Learning from labeled examples\",\"Learning without data\",\"Only clustering\",\"Only reinforcement\"]"}]}
            """);
    }

    public Task<QuizGenerationResponse> GenerateQuizAsync(
        int doctorUserId, QuizGenerationRequest req, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<LectureSummaryDto> SummarizeAsync(
        int doctorUserId, SummarizeRequest req, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<LectureAnswerDto> AnswerLectureQuestionAsync(
        int doctorUserId, LectureQuestionRequest req, CancellationToken ct) =>
        throw new NotSupportedException();
}

/// <summary>Test host with RAG enabled and both external AI dependencies faked.</summary>
public sealed class RagApiFactory : ApiFactory
{
    public FakeRagClient FakeRag { get; } = new();
    public FakeDoctorAiService FakeAi { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RagApi:Enabled"] = "true",
                ["RagApi:BaseUrl"] = "http://fake-rag/",
                ["RagApi:TimeoutSeconds"] = "10",
                ["RagApi:TopK"] = "3"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRagClient>();
            services.AddSingleton<IRagClient>(FakeRag);

            services.RemoveAll<IDoctorAiService>();
            services.AddSingleton<IDoctorAiService>(FakeAi);

            services.Configure<RagOptions>(options =>
            {
                options.Enabled = true;
                options.BaseUrl = "http://fake-rag/";
                options.TimeoutSeconds = 10;
                options.TopK = 3;
            });
        });
    }
}
