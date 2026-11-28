using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>Everything persisted about one graduate that counts as evidence. Always filtered by the signed-in user.</summary>
public sealed record CareerEvidence(
    GraduateProfile Profile, int ProfileCompletion, string TargetRole, string RoleKey,
    IReadOnlyList<GraduateSkill> Skills, string CvText, IReadOnlyList<CvAnalysis> Analyses,
    IReadOnlyList<PortfolioProject> Projects, IReadOnlyList<SkillChallengeAttempt> Challenges,
    IReadOnlyList<InterviewSession> Sessions, IReadOnlyList<InterviewQuestion> Questions,
    int SavedJobs, IReadOnlyList<JobApplication> Applications, IReadOnlyList<RoadmapItem> Plan,
    int QuizLevelsPassed, bool RecoveryDrillDone, int ReportCount, int TopJobMatch)
{
    public int? CvScore => Analyses.Count == 0 ? null : Analyses[^1].ScorePercent;

    public IReadOnlyList<string> VerifiedSkills =>
        [.. Challenges.Where(c => c.Passed).Select(c => c.Skill).Distinct(StringComparer.OrdinalIgnoreCase)];

    public int ValidProjects => Projects.Count(PortfolioRules.IsValid);

    /// <summary>A session counts once every question has been answered and graded.</summary>
    public IReadOnlyList<InterviewSession> CompletedSessions =>
        [.. Sessions.Where(s => Questions.Any(q => q.SessionId == s.Id)
            && Questions.Where(q => q.SessionId == s.Id).All(q => q.FeedbackScore != null))];

    public double? InterviewAverage => Questions.Any(q => q.FeedbackScore != null)
        ? Questions.Where(q => q.FeedbackScore != null).Average(q => q.FeedbackScore!.Value)
        : null;
}

public sealed record SkillStatus(
    string Skill, string Evidence, bool Reported, string? Level, bool InCv, bool InPortfolio, bool Verified,
    int JobDemand, string Priority, string Reason, string Action);

public sealed record SkillGapResult(string Target, int EvidencedPercent, IReadOnlyList<SkillStatus> Skills);

public class CareerEvidenceService(AppDbContext db, GraduateProfileService profiles, JobMatchingService matcher)
{
    public async Task<CareerEvidence> GatherAsync(int userId)
    {
        var profile = await profiles.GetOrCreateProfileEntityAsync(userId);
        var completion = (await profiles.GetCompletionAsync(userId)).Percent;
        var cv = await db.CvDocuments.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UploadedAtUtc).FirstOrDefaultAsync();
        // Rule-only fallback results are not counted: they are not a completed AI analysis.
        var analyses = await db.CvAnalyses.AsNoTracking()
            .Where(a => a.CvDocument!.UserId == userId && a.ProviderName != "deterministic")
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).ToListAsync();
        var quest = await db.GraduateQuestProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        var skipped = (quest?.SkippedLevelsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
        var matches = await matcher.MatchForUserAsync(userId, limit: 3, minScore: 0);

        return new CareerEvidence(
            profile, completion,
            await matcher.TargetRoleAsync(userId), await matcher.GuessTargetRoleAsync(userId),
            await db.GraduateSkills.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(),
            (cv?.ManualText is { Length: > 0 } manual ? manual : cv?.ExtractedText) ?? "",
            analyses,
            await db.PortfolioProjects.AsNoTracking().Where(p => p.UserId == userId)
                .OrderByDescending(p => p.UpdatedAtUtc).ToListAsync(),
            await db.SkillChallengeAttempts.AsNoTracking()
                .Where(c => c.UserId == userId && c.SubmittedAtUtc != null).ToListAsync(),
            await db.InterviewSessions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(),
            await db.InterviewQuestions.AsNoTracking().Where(q => q.Session!.UserId == userId).ToListAsync(),
            await db.SavedJobs.CountAsync(s => s.UserId == userId),
            await db.JobApplications.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(),
            await db.RoadmapItems.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(),
            Math.Max(0, (quest?.UnlockedLevel ?? 1) - 1 - skipped),
            // A finished drill leaves its activity entry behind.
            await db.Activities.AnyAsync(a => a.UserId == userId && a.Kind == "interview" && a.Message.Contains("recovery drill")),
            await db.CareerReports.CountAsync(r => r.UserId == userId),
            matches.Count == 0 ? 0 : (int)Math.Round(matches.Average(m => m.ScorePercent)));
    }

    public async Task<SkillGapResult> SkillGapAsync(CareerEvidence e, IReadOnlyList<string>? required, string? target)
    {
        var demand = (await (await JobCatalogService.CurrentListingsAsync(db)).Select(j => j.KeySkillsCsv).ToListAsync())
            .SelectMany(csv => Csv(csv).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        required ??= CareerCatalog.RoleSkills.TryGetValue(e.RoleKey, out var list) ? list : CareerCatalog.RoleSkills["Software Engineer"];
        return Classify(e, required, target ?? e.RoleKey, demand);
    }

    /// <summary>Pure comparison, so the priority rules can be tested and explained.</summary>
    public static SkillGapResult Classify(
        CareerEvidence e, IReadOnlyList<string> required, string target, IReadOnlyDictionary<string, int> demand)
    {
        var rows = required.Distinct(StringComparer.OrdinalIgnoreCase).Select((skill, index) =>
        {
            var reported = e.Skills.FirstOrDefault(s => CareerCatalog.MatchesSkill(skill, s.Name));
            var inCv = e.CvText.Length > 0 && CareerCatalog.MatchesSkill(skill, e.CvText);
            var inPortfolio = e.Projects.Any(p => Csv(p.Technologies).Concat(Csv(p.Skills)).Any(t => CareerCatalog.MatchesSkill(skill, t)));
            var verified = e.VerifiedSkills.Any(v => CareerCatalog.MatchesSkill(skill, v));
            var evidence = verified ? "Demonstrated in a challenge"
                : inPortfolio ? "Shown in a portfolio project"
                : inCv ? "Mentioned in your CV"
                : reported is not null ? "Self-reported"
                : "Not evidenced";
            var jobs = demand.GetValueOrDefault(skill);
            var core = index < 3;
            var priority = verified ? "Done"
                : !inCv && !inPortfolio && reported is null ? (core || jobs >= 2 ? "High" : "Medium")
                : !inCv && !inPortfolio ? (core ? "High" : "Medium")
                : "Low";
            var reason = $"{(core ? "Core skill" : "Supporting skill")} for {target}; listed in {jobs} current job posting(s); evidence: {evidence.ToLowerInvariant()}.";
            var action = verified ? "Keep it visible in your CV and portfolio."
                : evidence == "Not evidenced" ? $"Learn the basics, add it to your plan, then take the {skill} challenge."
                : $"Take the {skill} challenge to turn this claim into proof.";
            return new SkillStatus(skill, evidence, reported is not null, reported?.Level, inCv, inPortfolio, verified, jobs, priority, reason, action);
        }).OrderBy(r => r.Priority switch { "High" => 0, "Medium" => 1, "Low" => 2, _ => 3 })
          .ThenByDescending(r => r.JobDemand).ToList();

        var evidenced = rows.Count(r => r.InCv || r.InPortfolio || r.Verified);
        return new SkillGapResult(target, rows.Count == 0 ? 0 : (int)Math.Round(evidenced * 100.0 / rows.Count), rows);
    }

    public static IEnumerable<string> Csv(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
