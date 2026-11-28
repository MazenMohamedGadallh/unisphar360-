using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>Answers each graduate prompt with fixed JSON; the correct quiz option is always index 1.</summary>
public sealed class ScriptedCompletion : IAiCompletionService
{
    public Task<AiCompletion> CompleteAsync(
        string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct,
        int timeoutSeconds = 90, string? preferredProvider = null)
    {
        object reply =
            userPrompt.Contains("multiple-choice scenario") ? new
            {
                questions = Enumerable.Range(1, 5).Select(i => new
                {
                    question = $"Q{i}", options = new[] { "a", "b", "c", "d" }, correctIndex = 1, explanation = "because"
                })
            }
            : userPrompt.Contains("study checklist") ? new
            {
                items = new[] { 1, 2, 3 }.Select(i => new { concept = $"C{i}", detail = "do it" })
            }
            : userPrompt.Contains("micro-questions") ? new { questions = new[] { "d1", "d2", "d3" } }
            : userPrompt.Contains("missingKeywords") ? new
            {
                missingKeywords = new[] { "Terraform", "SQL" },
                bulletImprovements = new[] { new { keyword = "Terraform", original = "", improved = "Provisioned..." } },
                questions = new[] { "j1", "j2", "j3" }
            }
            : userPrompt.Contains("coverLetter") ? new { coverLetter = "Dear team", pitch = "Hi" }
            : userPrompt.Contains("Candidate answer") ? new
            {
                feedback = "ok", score = 6,
                rubric = new[] { new { criterion = "Depth", score = userPrompt.Contains("sharper") ? 9 : 3 }, new { criterion = "Relevance", score = 8 } }
            }
            : new { questions = new[] { "i1", "i2" } };
        return Task.FromResult(new AiCompletion(JsonSerializer.Serialize(reply), "fake", "fake-model"));
    }
}

public sealed class ScriptedAiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiCompletionService>();
            services.AddSingleton<IAiCompletionService, ScriptedCompletion>();
        });
    }
}

public class GraduateQuestTests(ScriptedAiFactory factory) : IClassFixture<ScriptedAiFactory>
{
    private static async Task<JsonElement> Json(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task OnboardingQuestDrillAndJobPrep_EndToEnd()
    {
        var client = factory.GraduateClient;

        var bad = await client.PostAsJsonAsync("/api/graduate/profile/onboarding", new
        { targetRole = "Backend Developer", experienceLevel = "Wizard", primaryStruggle = "System Design", preferredWorkMode = "Remote" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        await Json(client.PostAsJsonAsync("/api/graduate/profile/onboarding", new
        {
            targetRole = "Backend Developer", experienceLevel = "Entry-level", primaryStruggle = "Problem Solving",
            preferredWorkMode = "Remote", preferredCity = "Alexandria"
        }));
        var profile = await Json(client.GetAsync("/api/grad/profile"));
        Assert.True(profile.GetProperty("hasCompletedOnboarding").GetBoolean());
        Assert.StartsWith("Backend Developer", profile.GetProperty("preferredJobTitles").GetString());
        Assert.StartsWith("Alexandria", profile.GetProperty("preferredLocations").GetString());

        // Quest: locked levels have no quiz, failing keeps the level, passing awards XP exactly once.
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync("/api/graduate/quest/levels/2/quiz", null)).StatusCode);
        var quiz = await Json(client.PostAsync("/api/graduate/quest/levels/1/quiz", null));
        Assert.False(quiz.GetProperty("questions")[0].TryGetProperty("correctIndex", out _));

        // Options are shuffled; the scripted key is always "b".
        static int[] Pick(JsonElement q, int correct) => q.GetProperty("questions").EnumerateArray().Select((x, i) =>
        {
            var at = x.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList().IndexOf("b");
            return i < correct ? at : (at + 1) % 4;
        }).ToArray();
        var failed = await Json(client.PostAsJsonAsync("/api/graduate/quest/levels/1/submit",
            new { answers = Pick(quiz, 3) }));
        Assert.False(failed.GetProperty("passed").GetBoolean());

        quiz = await Json(client.PostAsync("/api/graduate/quest/levels/1/quiz", null));
        var answers = new { answers = Pick(quiz, 4) };
        var passed = await Json(client.PostAsJsonAsync("/api/graduate/quest/levels/1/submit", answers));
        Assert.True(passed.GetProperty("passed").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/graduate/quest/levels/1/submit", answers)).StatusCode);

        var quest = await Json(client.GetAsync("/api/graduate/quest"));
        Assert.Equal(100, quest.GetProperty("xp").GetInt32());
        Assert.Equal(2, quest.GetProperty("quizAttempts").GetInt32());
        Assert.Equal("active", quest.GetProperty("levels")[1].GetProperty("status").GetString());
        var overview = await Json(client.GetAsync("/api/grad/overview"));
        Assert.Equal(quest.GetProperty("readinessPercent").GetInt32(), overview.GetProperty("jobReadinessPercent").GetInt32());

        // Prep guide is cached with its reviewed marks.
        await Json(client.PostAsync("/api/graduate/quest/levels/2/prep", null));
        await Json(client.PutAsJsonAsync("/api/graduate/quest/levels/2/prep/1", new { reviewed = true }));
        var prepGuide = await Json(client.PostAsync("/api/graduate/quest/levels/2/prep", null));
        Assert.True(prepGuide.GetProperty("items")[1].GetProperty("reviewed").GetBoolean());

        // Skipping unlocks the next level without XP or readiness; passing it later earns both.
        var skippedQuest = await Json(client.PostAsync("/api/graduate/quest/levels/2/skip", null));
        Assert.Equal("skipped", skippedQuest.GetProperty("levels")[1].GetProperty("status").GetString());
        Assert.Equal("active", skippedQuest.GetProperty("levels")[2].GetProperty("status").GetString());
        Assert.Equal(100, skippedQuest.GetProperty("xp").GetInt32());
        Assert.Equal(quest.GetProperty("readinessPercent").GetInt32(), skippedQuest.GetProperty("readinessPercent").GetInt32());
        quiz = await Json(client.PostAsync("/api/graduate/quest/levels/2/quiz", null));
        var retaken = await Json(client.PostAsJsonAsync("/api/graduate/quest/levels/2/submit", new { answers = Pick(quiz, 4) }));
        Assert.True(retaken.GetProperty("readinessPercent").GetInt32() > quest.GetProperty("readinessPercent").GetInt32());
        var afterRetake = await Json(client.GetAsync("/api/graduate/quest"));
        Assert.Equal("completed", afterRetake.GetProperty("levels")[1].GetProperty("status").GetString());
        Assert.Equal("active", afterRetake.GetProperty("levels")[2].GetProperty("status").GetString());

        // Job preparation needs a CV, then yields gaps and a 3-question session.
        var jobs = await Json(client.GetAsync("/api/grad/jobs"));
        var jobId = jobs.GetProperty("jobs")[0].GetProperty("id").GetInt32();
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("Backend developer. Built REST APIs with C# and SQL for a university course registration system, with unit tests and Git."), "file", "cv.txt");
            (await client.PostAsync("/api/grad/profile/cv", form)).EnsureSuccessStatusCode();
        }
        var prep = await Json(client.PostAsync($"/api/graduate/jobs/{jobId}/prepare", null));
        Assert.Equal("Terraform", Assert.Single(prep.GetProperty("missingKeywords").EnumerateArray()).GetString());
        var session = prep.GetProperty("interview");
        Assert.Equal(3, session.GetProperty("questions").GetArrayLength());
        var outreach = await Json(client.PostAsJsonAsync($"/api/graduate/jobs/{jobId}/outreach", new { tone = "Direct" }));
        Assert.Equal("Direct", outreach.GetProperty("tone").GetString());

        // Recovery drill targets the weakest rubric dimension and pays XP only on completion.
        var sessionId = session.GetProperty("id").GetInt32();
        var questionId = session.GetProperty("questions")[0].GetProperty("id").GetInt32();
        await Json(client.PostAsJsonAsync($"/api/grad/interview/sessions/{sessionId}/questions/{questionId}/answer",
            new { answerText = "A reasonably detailed answer." }));
        var drill = await Json(client.PostAsJsonAsync("/api/graduate/interview/recovery-drill", new { sessionId }));
        Assert.Equal("Depth", drill.GetProperty("dimension").GetString());
        var drillId = drill.GetProperty("drillId").GetString();

        async Task<int> Answer(int index) => (await Json(client.PostAsJsonAsync(
            "/api/graduate/interview/recovery-drill/answer",
            new { drillId, index, answerText = "A reasonably detailed answer." }))).GetProperty("xpAwarded").GetInt32();

        Assert.Equal(0, await Answer(0));
        Assert.Equal(0, await Answer(0)); // repeating a question does not count twice
        Assert.Equal(0, await Answer(1));
        Assert.Equal(InterviewService.DrillXp, await Answer(2));
        Assert.Equal(200 + InterviewService.DrillXp,
            (await Json(client.GetAsync("/api/graduate/quest"))).GetProperty("xp").GetInt32());

        // A completed drill says whether the weak score rose, so the graduate can repeat until it does.
        async Task<JsonElement> RunDrill(string answerText)
        {
            var next = await Json(client.PostAsJsonAsync("/api/graduate/interview/recovery-drill", new { sessionId }));
            var id = next.GetProperty("drillId").GetString();
            var last = default(JsonElement);
            for (var index = 0; index < 3; index++)
                last = await Json(client.PostAsJsonAsync("/api/graduate/interview/recovery-drill/answer",
                    new { drillId = id, index, answerText }));
            return last;
        }

        var same = await RunDrill("A reasonably detailed answer.");
        Assert.False(same.GetProperty("improved").GetBoolean());
        var better = await RunDrill("A much sharper and deeper answer.");
        Assert.True(better.GetProperty("improved").GetBoolean());
        Assert.Equal(3, better.GetProperty("baseline").GetDouble());
        Assert.Equal(9, better.GetProperty("drillAverage").GetDouble());
    }
}
