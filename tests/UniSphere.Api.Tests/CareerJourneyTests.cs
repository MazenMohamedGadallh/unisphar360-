using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

public sealed class CareerCompletion : IAiCompletionService
{
    public static string LastPrompt = "";

    public Task<AiCompletion> CompleteAsync(
        string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct,
        int timeoutSeconds = 90, string? preferredProvider = null)
    {
        LastPrompt = userPrompt;
        object reply =
            userPrompt.Contains("practical assessment") ? new
            {
                scenario = "A shop database.",
                questions = Enumerable.Range(1, 4).Select(i => new
                { question = $"Q{i}", options = new[] { "a", "b", "c", "d" }, correctIndex = 2, explanation = "SECRET-EXPLANATION" }),
                task = "Explain an index.",
                criteria = new[] { "correct", "clear", "example" }
            }
            : userPrompt.Contains("Grade strictly") ? new { score = 8, feedback = "Solid.", improvements = new[] { "Give numbers" } }
            : userPrompt.Contains("Assess this graduate") ? new
            {
                summary = "Early career.", experienceLevel = "Entry-level", experienceReason = "One internship.",
                strengths = new[] { new { point = "SQL", evidence = "CV lists SQL" } },
                gaps = new[] { new { point = "Docker", evidence = "Not in CV" } },
                roles = Array.Empty<object>(), priorities = new[] { "Docker" }, inferredSkills = Array.Empty<object>()
            }
            : userPrompt.Contains("scorePercent") ? new { overallAssessment = "Good base.", scorePercent = 80, strengths = new[] { "Clear" } }
            : userPrompt.Contains("portfolio") || userPrompt.Contains("Rewrite the project")
                ? new { suggestedDescription = "Clearer text.", missing = new[] { "Add results" } }
            : new { questions = new[] { "i1", "i2" } };
        return Task.FromResult(new AiCompletion(JsonSerializer.Serialize(reply), "fake", "fake-model"));
    }
}

public sealed class CareerAiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiCompletionService>();
            services.AddSingleton<IAiCompletionService, CareerCompletion>();
        });
    }
}

internal static class CareerTestHelpers
{
    public static async Task<JsonElement> Json(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>A brand new graduate with no profile, CV or history.</summary>
    public static HttpClient NewGraduate(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Email = $"g{Guid.NewGuid():N}@unisphere.edu", FullName = "New Graduate", FirstName = "New",
            Role = UserRole.Graduate, PasswordHash = "x"
        };
        db.Users.Add(user);
        db.SaveChanges();
        return factory.AuthedClient(scope.ServiceProvider.GetRequiredService<TokenService>().Create(user).Token);
    }
}

public class CareerJourneyTests(CareerAiFactory factory) : IClassFixture<CareerAiFactory>
{
    private static Task<JsonElement> Json(Task<HttpResponseMessage> r) => CareerTestHelpers.Json(r);

    private static JsonElement Level(JsonElement journey, int n) => journey.GetProperty("levels")[n - 1];

    [Fact]
    public async Task NewGraduate_StartsEmpty_AndEarnsEachRewardOnce()
    {
        var client = CareerTestHelpers.NewGraduate(factory);

        var start = await Json(client.GetAsync("/api/graduate/journey"));
        Assert.Equal(0, start.GetProperty("xp").GetInt32());
        Assert.Equal(1, start.GetProperty("careerLevel").GetInt32());
        Assert.Equal("locked", Level(start, 2).GetProperty("status").GetString());

        // Evidence for a locked level earns nothing yet.
        await Json(client.PostAsJsonAsync("/api/graduate/portfolio", new
        {
            title = "Inventory API",
            description = "A REST API that tracks shop inventory, with authentication, tests and a SQL database behind it.",
            role = "I built the whole backend", technologies = "C#, SQL", gitHubUrl = "https://github.com/me/inventory"
        }));
        Assert.Equal(0, (await Json(client.GetAsync("/api/graduate/journey"))).GetProperty("xp").GetInt32());

        await Json(client.PostAsJsonAsync("/api/graduate/profile/onboarding", new
        { targetRole = "Backend Developer", experienceLevel = "Entry-level", primaryStruggle = "CV / ATS", preferredWorkMode = "Remote" }));
        await Json(client.PutAsJsonAsync("/api/grad/profile", new { careerGoal = "Ship backend services" }));
        await Json(client.PostAsync("/api/graduate/dna", null));

        var done = await Json(client.GetAsync("/api/graduate/journey"));
        Assert.Equal("completed", Level(done, 1).GetProperty("status").GetString());
        Assert.Equal("active", Level(done, 2).GetProperty("status").GetString());
        Assert.Equal(2, done.GetProperty("careerLevel").GetInt32());
        var xp = done.GetProperty("xp").GetInt32();
        Assert.Equal(150, xp);
        Assert.Equal(xp, (await Json(client.GetAsync("/api/graduate/journey"))).GetProperty("xp").GetInt32());
    }

    [Fact]
    public async Task Challenge_HidesAnswers_GradesOnce_AndFeedsSkillGap()
    {
        var client = CareerTestHelpers.NewGraduate(factory);

        var started = await client.PostAsJsonAsync("/api/graduate/lab", new { skill = "SQL", difficulty = "Easy" });
        var raw = await started.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-EXPLANATION", raw);
        Assert.DoesNotContain("correctIndex", raw, StringComparison.OrdinalIgnoreCase);
        var attempt = JsonDocument.Parse(raw).RootElement;
        var id = attempt.GetProperty("id").GetInt32();
        // Starting again resumes the open attempt instead of generating another.
        Assert.Equal(id, (await Json(client.PostAsJsonAsync("/api/graduate/lab", new { skill = "SQL", difficulty = "Easy" })))
            .GetProperty("id").GetInt32());

        var before = await Json(client.PostAsJsonAsync("/api/graduate/skill-gap", new { role = "Data Analyst" }));
        Assert.False(Skill(before, "SQL").GetProperty("verified").GetBoolean());

        var other = CareerTestHelpers.NewGraduate(factory);
        // The fake key is option "c"; options are shuffled, so find where it landed. Miss the last one.
        var right = attempt.GetProperty("questions").EnumerateArray()
            .Select(q => q.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList().IndexOf("c")).ToArray();
        var submission = new { answers = new[] { right[0], right[1], right[2], (right[3] + 1) % 4 }, written = "An index lets the database find rows without reading the whole table." };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/graduate/lab/{id}/submit", submission)).StatusCode);

        var graded = await Json(client.PostAsJsonAsync($"/api/graduate/lab/{id}/submit", submission));
        Assert.Equal(77, graded.GetProperty("score").GetInt32()); // 3 x 15 + 8 x 4
        Assert.True(graded.GetProperty("passed").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/graduate/lab/{id}/submit", submission)).StatusCode);

        var after = await Json(client.PostAsJsonAsync("/api/graduate/skill-gap", new { role = "Data Analyst" }));
        Assert.True(Skill(after, "SQL").GetProperty("verified").GetBoolean());
        Assert.Equal("Not evidenced", Skill(after, "Excel").GetProperty("evidence").GetString());

        static JsonElement Skill(JsonElement gap, string name) =>
            gap.GetProperty("skills").EnumerateArray().Single(s => s.GetProperty("skill").GetString() == name);
    }

    [Fact]
    public async Task Portfolio_ValidatesLinks_AndIsPrivate()
    {
        var owner = CareerTestHelpers.NewGraduate(factory);
        var stranger = CareerTestHelpers.NewGraduate(factory);

        var script = await owner.PostAsJsonAsync("/api/graduate/portfolio", new
        { title = "X", description = "Y", demoUrl = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, script.StatusCode);

        var saved = await Json(owner.PostAsJsonAsync("/api/graduate/portfolio", new { title = "X", description = "Short" }));
        Assert.False(saved.GetProperty("complete").GetBoolean());
        var id = saved.GetProperty("id").GetInt32();

        Assert.Empty((await Json(stranger.GetAsync("/api/graduate/portfolio"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/graduate/portfolio/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PutAsJsonAsync($"/api/graduate/portfolio/{id}", new { title = "Mine", description = "Now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/graduate/portfolio/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.StudentClient.GetAsync("/api/graduate/journey")).StatusCode);
    }

    [Fact]
    public async Task Cv_AnalysisIsExplained_KeptInHistory_AndReportPersists()
    {
        var client = CareerTestHelpers.NewGraduate(factory);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Summary\nBackend developer, dev@example.com\nEducation\nBSc Computer Science\nProjects\nBuilt a JavaScript shop with a SQL database and Git."), "file", "cv.txt" }
        };
        var cv = await Json(client.PostAsync("/api/grad/profile/cv", form));
        var id = cv.GetProperty("id").GetInt32();
        Assert.False(cv.GetProperty("hasAnalysis").GetBoolean()); // upload never analyzes

        var result = await Json(client.PostAsJsonAsync($"/api/grad/profile/cv/{id}/analyze",
            new { jobDescription = "Needs SQL and Docker" }));
        Assert.Equal(80, result.GetProperty("aiScore").GetInt32());
        Assert.NotEmpty(result.GetProperty("scoreBreakdown").EnumerateArray());
        Assert.Equal("Docker", result.GetProperty("jobFit").GetProperty("notEvidenced")[0].GetString());
        Assert.DoesNotContain("Java", result.GetProperty("technicalSkills").EnumerateArray().Select(x => x.GetString()));

        var history = await Json(client.GetAsync("/api/grad/profile/cv-history"));
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(0, (await Json(CareerTestHelpers.NewGraduate(factory).GetAsync("/api/grad/profile/cv-history"))).GetArrayLength());

        await Json(client.PostAsync("/api/graduate/report", null));
        var report = await Json(client.GetAsync("/api/graduate/report"));
        Assert.Equal(1, report.GetProperty("history").GetArrayLength());
        Assert.False(report.GetProperty("current").GetProperty("careerReady").GetBoolean());
        var interview = report.GetProperty("current").GetProperty("dimensions").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "Interview preparation");
        Assert.Equal(JsonValueKind.Null, interview.GetProperty("percent").ValueKind); // not assessed, not zero
    }

    [Fact]
    public async Task Applications_RejectDuplicates_AndAnalyticsAreOwn()
    {
        var client = CareerTestHelpers.NewGraduate(factory);
        var jobs = await Json(client.GetAsync("/api/grad/jobs"));
        var jobId = jobs.GetProperty("jobs")[0].GetProperty("id").GetInt32();

        await Json(client.PostAsJsonAsync("/api/grad/applications", new { jobListingId = jobId }));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/grad/applications", new { jobListingId = jobId })).StatusCode);
        await Json(client.PostAsJsonAsync("/api/grad/applications",
            new { companyName = "Acme", jobTitle = "Analyst", status = "Assessment" }));

        var analytics = await Json(client.GetAsync("/api/grad/applications/analytics"));
        Assert.Equal(2, analytics.GetProperty("total").GetInt32());
        Assert.Equal(50, analytics.GetProperty("interviewRatePercent").GetInt32());
    }

    [Fact]
    public void Docx_KeepsParagraphs_SoSectionsAreFound()
    {
        var path = Path.GetTempFileName();
        File.Delete(path);
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                w.Write("<w:document><w:p><w:r><w:t>Educ</w:t></w:r><w:r><w:t>ation</w:t></w:r></w:p>" +
                        "<w:p><w:r><w:t xml:space=\"preserve\">Projects</w:t></w:r></w:p></w:document>");

            var text = CvFileService.ExtractDocx(path);

            Assert.Equal("Education\nProjects\n", text);
            Assert.Equal(["Education", "Projects"], CvSectionParser.Parse(text).Sections);
        }
        finally { File.Delete(path); }
    }
}

public class CareerWithoutProviderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AiFeatures_FailClearly_AndStoreNothing()
    {
        var client = CareerTestHelpers.NewGraduate(factory);

        var lab = await client.PostAsJsonAsync("/api/graduate/lab", new { skill = "SQL" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lab.StatusCode);
        Assert.Empty((await CareerTestHelpers.Json(client.GetAsync("/api/graduate/lab"))).GetProperty("attempts").EnumerateArray());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/graduate/dna", null)).StatusCode);
    }
}
