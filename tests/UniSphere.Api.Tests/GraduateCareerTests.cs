using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Graduate career platform (api/grad/*): profile, skills, CV, jobs,
/// applications, roadmap, interview, matches, skill-gap, overview,
/// alumni and settings — against freshly seeded demo data.
/// </summary>
public class GraduateCareerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public GraduateCareerTests(ApiFactory factory) => _factory = factory;

    // ---------- Profile ----------

    [Fact]
    public async Task Profile_ReturnsSeededGraduate()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/grad/profile");

        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<GradProfileRow>();
        Assert.NotNull(profile);
        Assert.Equal("Backend Engineer | .NET, APIs & Data", profile!.Headline);
        Assert.Equal("Cairo University", profile.UniversityName);
        Assert.True(profile.Skills.Count >= 7, "seeded profile skills exist");
        Assert.Equal(3, profile.Entries.Count);
    }

    [Fact]
    public async Task Profile_UpdatePersists()
    {
        var (headline, careerGoal) = ("Data Platform Engineer", "Ship one real production deployment this quarter.");
        var put = await _factory.GraduateClient.PutAsJsonAsync("/api/grad/profile", new
        {
            headline,
            careerSummary = "Graduate engineer focusing on data engineering.",
            careerGoal
        });
        put.EnsureSuccessStatusCode();

        var profile = await (await _factory.GraduateClient.GetAsync("/api/grad/profile"))
            .Content.ReadFromJsonAsync<GradProfileRow>();

        Assert.Equal(headline, profile!.Headline);
        Assert.Equal(careerGoal, profile.CareerGoal);
        // Fields not sent keep the seeded values (nullable patch semantics).
        Assert.NotEmpty(profile.PreferredLocations!);
    }

    [Fact]
    public async Task Skills_DedupeCaseInsensitive()
    {
        var first = await _factory.GraduateClient.PostAsJsonAsync("/api/grad/profile/skills", new { name = "linq" });
        first.EnsureSuccessStatusCode();
        var second = await _factory.GraduateClient.PostAsJsonAsync("/api/grad/profile/skills", new { name = "Linq" });
        second.EnsureSuccessStatusCode();

        var skills = await (await _factory.GraduateClient.GetAsync("/api/grad/profile")).Content
            .ReadFromJsonAsync<GradProfileRow>();

        Assert.Single(skills!.Skills, s => s.Name.Equals("LINQ", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(skills.Skills.Count, skills.Skills.DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Completion_IncludesWeights()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/grad/profile/completion");

        response.EnsureSuccessStatusCode();
        var completion = await response.Content.ReadFromJsonAsync<GradCompletionRow>();
        Assert.InRange(completion!.Percent, 60, 100);
    }

    // ---------- CV ----------

    [Fact]
    public async Task Cv_UploadTxt_ParsesText()
    {
        using var form = new MultipartFormDataContent();
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "Volunteer mentor for first-year students; built small data cleaning scripts in Python; " +
            "organized weekly study groups and tracked progress with Excel and reports for the faculty mentor.");
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/plain");
        form.Add(content, "file", "cv-note.txt");

        var response = await _factory.GraduateClient.PostAsync("/api/grad/profile/cv", form);

        response.EnsureSuccessStatusCode();
        var cv = await response.Content.ReadFromJsonAsync<GradCvRow>();
        Assert.Equal("Parsed", cv!.ParseStatus);
        Assert.True(cv.TextChars >= 80);

        // The saved CV shows up in the list (dto is owned by the graduate).
        var list = await (await _factory.GraduateClient.GetAsync("/api/grad/profile/cv"))
            .Content.ReadFromJsonAsync<List<GradCvRow>>();
        Assert.Contains(list!, c => c.Id == cv.Id);
    }

    [Fact]
    public async Task Cv_UploadUnsupportedExtension_Fails()
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(new byte[64]);
        content.Headers.ContentType = new("application/octet-stream");
        form.Add(content, "file", "payload.exe");

        var response = await _factory.GraduateClient.PostAsync("/api/grad/profile/cv", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Jobs ----------

    [Fact]
    public async Task Jobs_SearchSeededCatalog()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/grad/jobs?page=1&pageSize=5");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<GradJobsPage>();
        Assert.True(page!.Total >= 12); // 12 demo jobs seeded
        Assert.Equal(5, page.Jobs.Count);
        Assert.All(page.Jobs, j => Assert.Equal("Demo", j.Source));
    }

    [Fact]
    public async Task Jobs_SaveDedupeReturnsConflict()
    {
        var jobs = await (await _factory.GraduateClient.GetAsync("/api/grad/jobs?page=1&pageSize=5"))
            .Content.ReadFromJsonAsync<GradJobsPage>();
        var jobId = jobs!.Jobs[0].Id;

        var first = await _factory.GraduateClient.PostAsync($"/api/grad/jobs/{jobId}/save", null);
        var second = await _factory.GraduateClient.PostAsync($"/api/grad/jobs/{jobId}/save", null);

        first.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var saved = await (await _factory.GraduateClient.GetAsync("/api/grad/jobs/saved"))
            .Content.ReadFromJsonAsync<List<GradJobRow>>();
        // 1 seeded saved job + the one we just saved
        Assert.True(saved!.Count >= 2);

        var unsave = await _factory.GraduateClient.DeleteAsync($"/api/grad/jobs/{jobId}/save");
        unsave.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Matches_AreDeterministicAndAnnotated()
    {
        var first = await GetMatchesAsync();
        var second = await GetMatchesAsync();

        Assert.NotEmpty(first);
        Assert.Equal(ToScoreList(first), ToScoreList(second));
        Assert.All(first, m =>
        {
            Assert.Contains("Score", m.Explanation);
            Assert.NotEmpty(m.Actions);
        });
    }

    [Fact]
    public async Task SkillGap_KnownAndUnknownRoles()
    {
        var known = await _factory.GraduateClient.GetAsync("/api/grad/jobs/skill-gap?role=Frontend Developer");
        known.EnsureSuccessStatusCode();
        var gap = await known.Content.ReadFromJsonAsync<GradGapReport>();
        Assert.Equal("Frontend Developer", gap!.TargetRole);
        Assert.InRange(gap.CoveragePercent, 0, 100);
        Assert.NotEmpty(gap.Matched);

        var unknown = await _factory.GraduateClient.GetAsync("/api/grad/jobs/skill-gap?role=Quantum Oracle Wrangler");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    // ---------- Applications ----------

    [Fact]
    public async Task Applications_CrudAndSummary()
    {
        // Seeded: 2 applications (Applied, Interview).
        var create = await _factory.GraduateClient.PostAsJsonAsync("/api/grad/applications", new
        {
            companyName = "GreenTech Labs",
            jobTitle = "Junior Data Analyst",
            status = "Applied",
            followUpDate = "2026-11-01",
            notes = "Referred by alumna."
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<GradAppRow>();

        var list = await (await _factory.GraduateClient.GetAsync("/api/grad/applications"))
            .Content.ReadFromJsonAsync<List<GradAppRow>>();
        Assert.True(list!.Count >= 3);
        Assert.Contains(list, a => a.Id == created!.Id && a.CompanyName == "GreenTech Labs");

        var update = await _factory.GraduateClient.PutAsJsonAsync($"/api/grad/applications/{created!.Id}", new
        {
            companyName = "GreenTech Labs",
            jobTitle = "Junior Data Analyst",
            status = "Interview"
        });
        update.EnsureSuccessStatusCode();

        var summary = await (await _factory.GraduateClient.GetAsync("/api/grad/applications/summary"))
            .Content.ReadFromJsonAsync<GradAppSummary>();
        Assert.Equal(list.Count, summary!.Total);
        Assert.Equal(2, summary.Interviews); // seeded interview row + updated GreenTech
        Assert.True(summary.UpcomingFollowUps >= 2); // two seeded follow-up rows in the near future

        var delete = await _factory.GraduateClient.DeleteAsync($"/api/grad/applications/{created!.Id}");
        delete.EnsureSuccessStatusCode();
    }

    // ---------- Roadmap ----------

    [Fact]
    public async Task Roadmap_CrudAndProgress()
    {
        var seeded = await (await _factory.GraduateClient.GetAsync("/api/grad/roadmap"))
            .Content.ReadFromJsonAsync<List<GradRoadmapRow>>();
        Assert.Equal(3, seeded!.Count);
        Assert.Contains(seeded, r => r.Origin == "Manual");

        var create = await _factory.GraduateClient.PostAsJsonAsync("/api/grad/roadmap", new
        {
            title = "Add unit tests to portfolio project",
            category = "Skill",
            targetDate = "2026-12-01"
        });
        create.EnsureSuccessStatusCode();

        var list = await (await _factory.GraduateClient.GetAsync("/api/grad/roadmap"))
            .Content.ReadFromJsonAsync<List<GradRoadmapRow>>();
        var created = list!.Single(r => r.Title == "Add unit tests to portfolio project");

        var update = await _factory.GraduateClient.PutAsJsonAsync($"/api/grad/roadmap/{created.Id}", new
        {
            title = created.Title,
            status = "Completed"
        });
        update.EnsureSuccessStatusCode();

        var progress = await (await _factory.GraduateClient.GetAsync("/api/grad/roadmap/progress"))
            .Content.ReadFromJsonAsync<GradRoadmapProgress>();
        // 3 seeded (1 Completed) + new one marked Completed
        Assert.Equal(4, progress!.Total);
        Assert.Equal(2, progress.Completed);
        Assert.Equal(50, progress.Percent);

        var delete = await _factory.GraduateClient.DeleteAsync($"/api/grad/roadmap/{created.Id}");
        delete.EnsureSuccessStatusCode();
    }

    // ---------- Interview ----------

    [Fact]
    public async Task Interview_CuratedSession_NoAiNeeded()
    {
        var create = await _factory.GraduateClient.PostAsJsonAsync("/api/grad/interview/sessions", new
        {
            targetRole = "Backend Engineer",
            useAi = false
        });
        create.EnsureSuccessStatusCode();
        var session = await create.Content.ReadFromJsonAsync<GradSessionRow>();

        Assert.Equal("Curated", session!.QuestionSource);
        Assert.Equal(5, session.Questions.Count);
        Assert.All(session.Questions, q => Assert.True(q.QuestionText.Length > 10));

        // Feedback always needs a provider: none is configured in tests → 503.
        var answer = await _factory.GraduateClient.PostAsJsonAsync(
            $"/api/grad/interview/sessions/{session.Id}/questions/{session.Questions[0].Id}/answer",
            new { answerText = "I measured latency before and after, then published an internal post with the graphs and follow-up tickets." });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
    }

    [Fact]
    public async Task Interview_AiSessionWithoutProvider_FailsExplicitly()
    {
        var create = await _factory.GraduateClient.PostAsJsonAsync("/api/grad/interview/sessions", new
        {
            targetRole = "Backend Engineer",
            useAi = true
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
    }

    // ---------- Overview / Alumni / Settings ----------

    [Fact]
    public async Task Overview_ReturnsAggregates()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/grad/overview");

        response.EnsureSuccessStatusCode();
        var overview = await response.Content.ReadFromJsonAsync<GradOverviewDto>();
        Assert.Equal("Omar", overview!.FirstName);
        Assert.InRange(overview.ProfileCompletionPercent, 40, 100);
        Assert.True(overview.ApplicationsCount >= 2);
        Assert.Equal(33, overview.RoadmapProgressPercent);
        Assert.NotEmpty(overview.UpcomingEvents);
        Assert.NotEmpty(overview.NextActions);
    }

    [Fact]
    public async Task Alumni_ReturnsUniversityServices()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/grad/alumni");

        response.EnsureSuccessStatusCode();
        var alumni = await response.Content.ReadFromJsonAsync<GradAlumniDto>();
        Assert.Equal("Cairo University", alumni!.UniversityName);
        Assert.NotEmpty(alumni.CareerEvents); // graduation ceremony + career events
        Assert.False(string.IsNullOrWhiteSpace(alumni.CareerCenterTip));
        Assert.NotEmpty(alumni.Contacts);
    }

    [Fact]
    public async Task Settings_RoundTrip()
    {
        var put = await _factory.GraduateClient.PutAsJsonAsync("/api/grad/settings", new
        {
            notifyNewMatches = false,
            notifyFollowUps = true,
            matchAlertThresholdPercent = 60,
            showGpaOnProfile = true
        });
        put.EnsureSuccessStatusCode();
        var settings = await put.Content.ReadFromJsonAsync<GradSettingsRow>();

        Assert.False(settings!.NotifyNewMatches);
        Assert.True(settings.NotifyFollowUps);
        Assert.Equal(60, settings.MatchAlertThresholdPercent);
        Assert.True(settings.ShowGpaOnProfile);

        Assert.Equal(60, (await (await _factory.GraduateClient.GetAsync("/api/grad/settings"))
            .Content.ReadFromJsonAsync<GradSettingsRow>())!.MatchAlertThresholdPercent);
    }

    // ---------- Access control ----------

    [Fact]
    public async Task Student_CannotUseGraduateEndpoints()
    {
        var profile = await _factory.StudentClient.GetAsync("/api/grad/profile");
        Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);

        var jobs = await _factory.StudentClient.GetAsync("/api/grad/jobs");
        Assert.Equal(HttpStatusCode.Forbidden, jobs.StatusCode);
    }

    // ---------- helpers ----------

    private async Task<List<GradMatchRow>> GetMatchesAsync()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/grad/jobs/matches?limit=10&minScore=20");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<GradMatchRow>>())!;
    }

    private static List<string> ToScoreList(List<GradMatchRow> matches) =>
        matches.Select(m => $"{m.Job.Id}:{m.ScorePercent}").ToList();

    private sealed class GradProfileRow
    {
        public string Headline { get; set; } = "";
        public string? CareerGoal { get; set; }
        public string? PreferredLocations { get; set; }
        public string? UniversityName { get; set; }
        public int CompletionPercent { get; set; }
        public List<GradSkillEntry> Skills { get; set; } = [];
        public List<GradProfileEntry> Entries { get; set; } = [];
    }

    private sealed class GradSkillEntry
    {
        public string Name { get; set; } = "";
        public string? Level { get; set; }
    }

    private sealed class GradProfileEntry
    {
        public string Title { get; set; } = "";
    }

    private sealed class GradCompletionRow
    {
        public int Percent { get; set; }
        public List<string> MissingFields { get; set; } = [];
        public List<string> NextActions { get; set; } = [];
    }

    private sealed class GradCvRow
    {
        public int Id { get; set; }
        public string FileName { get; set; } = "";
        public string ParseStatus { get; set; } = "";
        public string? ParseError { get; set; }
        public int TextChars { get; set; }
    }

    private sealed class GradJobsPage
    {
        public List<GradJobRow> Jobs { get; set; } = [];
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class GradJobRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Source { get; set; } = "";
        public bool IsSaved { get; set; }
        public bool IsApplied { get; set; }
    }

    private sealed class GradMatchRow
    {
        public GradJobRow Job { get; set; } = new();
        public int ScorePercent { get; set; }
        public string Explanation { get; set; } = "";
        public List<string> Actions { get; set; } = [];
    }

    private sealed class GradGapReport
    {
        public string TargetRole { get; set; } = "";
        public int CoveragePercent { get; set; }
        public List<GradGapLine> Matched { get; set; } = [];
        public List<GradGapLine> Missing { get; set; } = [];
        public List<string> RecommendedSteps { get; set; } = [];
    }

    private sealed class GradGapLine
    {
        public string Skill { get; set; } = "";
        public string Priority { get; set; } = "";
    }

    private sealed class GradAppRow
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private sealed class GradAppSummary
    {
        public int Total { get; set; }
        public List<GradStatusCount> ByStatus { get; set; } = [];
        public int UpcomingFollowUps { get; set; }
        public int Interviews { get; set; }
    }

    private sealed class GradStatusCount
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
    }

    private sealed class GradRoadmapRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public string Origin { get; set; } = "Manual";
    }

    private sealed class GradRoadmapProgress
    {
        public int Total { get; set; }
        public int NotStarted { get; set; }
        public int InProgress { get; set; }
        public int Completed { get; set; }
        public int Percent { get; set; }
    }

    private sealed class GradSessionRow
    {
        public int Id { get; set; }
        public string TargetRole { get; set; } = "";
        public string QuestionSource { get; set; } = "";
        public List<GradQuestionRow> Questions { get; set; } = [];
    }

    private sealed class GradQuestionRow
    {
        public int Id { get; set; }
        public string QuestionText { get; set; } = "";
    }

    private sealed class GradOverviewDto
    {
        public string FirstName { get; set; } = "";
        public int ProfileCompletionPercent { get; set; }
        public int ApplicationsCount { get; set; }
        public int RoadmapProgressPercent { get; set; }
        public List<GradEventLite> UpcomingEvents { get; set; } = [];
        public List<string> NextActions { get; set; } = [];
    }

    private sealed class GradEventLite
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    private sealed class GradAlumniDto
    {
        public string? UniversityName { get; set; }
        public List<GradEventLite> CareerEvents { get; set; } = [];
        public string CareerCenterTip { get; set; } = "";
        public List<string> Contacts { get; set; } = [];
    }

    private sealed class GradSettingsRow
    {
        public bool NotifyNewMatches { get; set; }
        public bool NotifyFollowUps { get; set; }
        public int MatchAlertThresholdPercent { get; set; }
        public bool ShowGpaOnProfile { get; set; }
    }
}
