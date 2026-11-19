using System.Net;
using System.Net.Http.Json;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>End-to-end controller/service coverage for the two approved RAG
/// entry points: the Student AI Assistant and the Doctor Exam Assistant.</summary>
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
    public async Task GraduateAndDoctorChat_DoNotUseRag()
    {
        _factory.FakeRag.Reset();

        var graduate = _factory.GraduateClient;
        var graduateProviders = await graduate.GetFromJsonAsync<List<ChatProviderDto>>("/api/chat/providers");
        Assert.NotNull(graduateProviders);
        Assert.DoesNotContain(graduateProviders, p => p.Name == "rag");

        var graduateResponse = await graduate.PostAsJsonAsync(
            "/api/chat/conversations/new/messages",
            new { provider = "openai", content = "Career advice." });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, graduateResponse.StatusCode);

        var doctor = _factory.DoctorClient;
        var doctorProviders = await doctor.GetFromJsonAsync<List<ChatProviderDto>>("/api/chat/providers");
        Assert.NotNull(doctorProviders);
        Assert.DoesNotContain(doctorProviders, p => p.Name == "rag");

        var doctorResponse = await doctor.PostAsJsonAsync(
            "/api/chat/conversations/new/messages",
            new { provider = "openai", content = "General question." });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, doctorResponse.StatusCode);
        Assert.Equal(0, _factory.FakeRag.QueryCalls);
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

        // The existing direct-material generation remains a legitimate
        // fallback; the response must say that RAG was unavailable.
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
}
