using System.Net;
using System.Net.Http.Json;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

public class RagIntegrationTests : IClassFixture<RagApiFactory>
{
    private readonly RagApiFactory _factory;

    public RagIntegrationTests(RagApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StudentAssistant_ShowsRagProvider_AndStreamsGroundedAnswer()
    {
        _factory.FakeRag.Reset();
        var student = _factory.StudentClient;

        var providers = await student.GetFromJsonAsync<List<ChatProviderDto>>("/api/chat/providers");
        Assert.NotNull(providers);
        Assert.Contains(providers, p => p.Name == "rag");

        var response = await student.PostAsJsonAsync(
            "/api/chat/conversations/new/messages",
            new { provider = "rag", content = "Explain supervised learning from my course." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("The test RAG answer", body);
        Assert.Contains("Sources:", body);
        Assert.Equal(1, _factory.FakeRag.QueryCalls);
    }

    [Fact]
    public async Task Student_SelectingAnotherProvider_DoesNotUseRag()
    {
        _factory.FakeRag.Reset();
        var student = _factory.StudentClient;

        var response = await student.PostAsJsonAsync(
            "/api/chat/conversations/new/messages",
            new { provider = "openai", content = "Use my normal provider." });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, _factory.FakeRag.QueryCalls);
    }

    [Fact]
    public async Task StudentAssistant_RagUnavailable_ReturnsSafeErrorEvent()
    {
        _factory.FakeRag.Reset();
        _factory.FakeRag.ThrowUnavailable = true;
        var student = _factory.StudentClient;

        var response = await student.PostAsJsonAsync(
            "/api/chat/conversations/new/messages",
            new { provider = "rag", content = "Explain supervised learning." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: error", body);
        Assert.Contains("unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("The test RAG answer", body);
        Assert.Equal(1, _factory.FakeRag.QueryCalls);
    }

    [Fact]
    public async Task GraduateAndDoctorChat_UseRag_AdminDoesNot()
    {
        _factory.FakeRag.Reset();

        foreach (var client in new[] { _factory.GraduateClient, _factory.DoctorClient })
        {
            var providers = await client.GetFromJsonAsync<List<ChatProviderDto>>("/api/chat/providers");
            Assert.NotNull(providers);
            Assert.Contains(providers, p => p.Name == "rag");

            var response = await client.PostAsJsonAsync(
                "/api/chat/conversations/new/messages",
                new { provider = "rag", content = "Summarize my material." });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("The test RAG answer", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(2, _factory.FakeRag.QueryCalls);

        // Picking another provider still never goes through RAG.
        var other = await _factory.DoctorClient.PostAsJsonAsync(
            "/api/chat/conversations/new/messages",
            new { provider = "openai", content = "General question." });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
        Assert.Equal(2, _factory.FakeRag.QueryCalls);

        var adminProviders = await _factory.AdminClient.GetFromJsonAsync<List<ChatProviderDto>>("/api/chat/providers");
        Assert.DoesNotContain(adminProviders!, p => p.Name == "rag");
    }

    [Fact]
    public async Task DoctorExamAssistant_RetrievesSelectedMaterials_AndWarnsOnRagFailure()
    {
        _factory.FakeRag.Reset();
        var doctor = _factory.DoctorClient;

        var courses = await doctor.GetFromJsonAsync<List<ExamCatalogCourseDto>>(
            "/api/doctor/exam-assistant/courses");
        Assert.NotNull(courses);
        Assert.NotEmpty(courses);
        var course = courses[0];

        var materials = await doctor.GetFromJsonAsync<List<MaterialDto>>(
            $"/api/doctor/exam-assistant/courses/{course.Id}/materials");
        Assert.NotNull(materials);
        var material = materials.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Content));
        Assert.NotNull(material);

        var request = new
        {
            courseId = course.Id,
            materialIds = new[] { material!.Id },
            questionCount = 1,
            difficulty = "easy",
            language = "en"
        };

        var response = await doctor.PostAsJsonAsync("/api/doctor/exam-assistant/generate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExamGenerationResponse>();
        Assert.NotNull(result);
        Assert.Null(result!.RagWarning);
        Assert.Single(result.Questions);
        Assert.Equal(1, _factory.FakeRag.RetrieveCalls);
        Assert.Single(_factory.FakeRag.LastRetrieveDocuments);
        Assert.Contains(material.Title, _factory.FakeRag.LastRetrieveDocuments[0].Title);
        Assert.Contains("Retrieved test context", _factory.FakeAi.LastUserPrompt);

        _factory.FakeRag.ThrowUnavailable = true;
        var fallbackResponse = await doctor.PostAsJsonAsync("/api/doctor/exam-assistant/generate", request);
        Assert.Equal(HttpStatusCode.OK, fallbackResponse.StatusCode);
        var fallback = await fallbackResponse.Content.ReadFromJsonAsync<ExamGenerationResponse>();
        Assert.NotNull(fallback);
        Assert.Single(fallback!.Questions);
        Assert.False(string.IsNullOrWhiteSpace(fallback.RagWarning));
        Assert.Contains("selected materials only", fallback.RagWarning!);
    }

    [Fact]
    public async Task Student_CannotCallDoctorExamAssistant()
    {
        _factory.FakeRag.Reset();
        var student = _factory.StudentClient;

        var response = await student.PostAsJsonAsync(
            "/api/doctor/exam-assistant/generate",
            new { courseId = 1, materialIds = new[] { 1 }, questionCount = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _factory.FakeRag.RetrieveCalls);
    }

    [Fact]
    public async Task Doctor_TranslateQuestions_SendsQuestionsToAi_AndKeepsTypeAndSource()
    {
        var question = new GeneratedExamQuestionDto(
            "Ma huwa al-ta'allum al-muwajjah?", "ShortAnswer", "x", null, "Lecture 1", null);

        var response = await _factory.DoctorClient.PostAsJsonAsync(
            "/api/doctor/exam-assistant/translate",
            new TranslateQuestionsRequest(new[] { question }, "en"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var translated = await response.Content.ReadFromJsonAsync<List<GeneratedExamQuestionDto>>();
        Assert.Single(translated!);
        Assert.Equal("What is supervised learning?", translated![0].QuestionText);
        Assert.Equal("ShortAnswer", translated[0].QuestionType);
        Assert.Equal("Lecture 1", translated[0].SourceMaterial);
        Assert.Contains(question.QuestionText, _factory.FakeAi.LastUserPrompt);

        var student = await _factory.StudentClient.PostAsJsonAsync(
            "/api/doctor/exam-assistant/translate",
            new TranslateQuestionsRequest(new[] { question }, "en"));
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);
    }
}
