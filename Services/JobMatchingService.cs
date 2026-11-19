using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Deterministic job matching + skill-gap analysis. Scores come from a fixed
/// published weighting (documented below) so every result is reproducible and
/// explained to the user — no LLM scores, no randomness.
///
/// Total = job-skill coverage (40) + title match (20) + industry match (15)
///         + location match (15) + work-mode match (10).
/// </summary>
public class JobMatchingService(AppDbContext db)
{
    public const int WeightSkills = 40, WeightTitle = 20, WeightIndustry = 15, WeightLocation = 15, WeightWorkMode = 10;

    // Technical categories that get a neutral title score when the graduate
    // hasn't picked exact target titles.
    private static readonly string[] TechCategories = ["Technical", "Data", "Fintech"];

    public sealed record UserSkillSnapshot(IReadOnlyList<string> SkillNames, IReadOnlyList<string> PreferredTitles,
        IReadOnlyList<string> PreferredIndustries, IReadOnlyList<string> PreferredLocations,
        IReadOnlyList<string> WorkModes, bool HasExplicitLocationPref);

    public async Task<UserSkillSnapshot> BuildSnapshotAsync(int userId)
    {
        var skills = await db.GraduateSkills.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.Name).ToListAsync();
        var latestCv = await db.CvDocuments.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UploadedAtUtc).ToListAsync();
        var cvSkills = latestCv
            .Select(c => c.ManualText is { Length: > 0 } ? c.ManualText : c.ExtractedText)
            .OfType<string>()
            .SelectMany(t => CareerCatalog.DetectSkills(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var names = skills.Select(s => s.Name).Union(cvSkills, StringComparer.OrdinalIgnoreCase).ToList();

        var profile = await db.GraduateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);

        return new UserSkillSnapshot(
            names,
            SplitCsv(profile?.PreferredJobTitles),
            SplitCsv(profile?.PreferredIndustries),
            SplitCsv(profile?.PreferredLocations),
            SplitCsv(profile?.WorkModes),
            !string.IsNullOrWhiteSpace(profile?.PreferredLocations)
        );
    }

    private static string[] SplitCsv(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<IReadOnlyList<JobMatchDto>> MatchForUserAsync(int userId, int limit, int minScore)
    {
        var snapshot = await BuildSnapshotAsync(userId);
        var jobs = await db.JobListings.AsNoTracking().Where(j => j.IsActive).ToListAsync();
        var savedIds = (await db.SavedJobs.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.JobListingId).ToListAsync()).ToHashSet();
        var appliedIds = (await db.JobApplications.AsNoTracking().Where(a => a.UserId == userId && a.JobListingId != null).Select(a => a.JobListingId!).ToListAsync()).ToHashSet();

        List<(int Score, JobListing Job, string Explanation, List<string> Matched, List<string> Missing, List<string> Actions)> scored = [];
        foreach (var job in jobs)
        {
            var jobSkills = (job.KeySkillsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var required = jobSkills.Count > 0 ? jobSkills : CareerCatalog.DetectSkills(job.Title + " " + job.Description);
            var (score, explanation, matched, missing, actions) = ScoreJob(job, required, snapshot);
            if (score >= minScore)
                scored.Add((score, job, explanation, matched, missing[0..Math.Min(3, missing.Count)], actions));
        }

        return [.. scored
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Job.PublishedAtUtc)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(x => new JobMatchDto(
                JobCatalogService.MapJob(x.Job, savedIds.Contains(x.Job.Id), appliedIds.Contains(x.Job.Id)),
                x.Score, x.Matched, x.Missing, x.Explanation, x.Actions))];
    }

    /// <summary>Deterministic scoring of one job against a user snapshot.</summary>
    internal (int Score, string Explanation, List<string> Matched, List<string> Missing, List<string> Actions) ScoreJob(
        JobListing job, List<string> jobSkills, UserSkillSnapshot user)
    {
        int total = 0;
        var parts = new List<string>();
        var actions = new List<string>();

        // ---- Skills (0..40): coverage of the job's skill list ----
        var matchedSkills = jobSkills
            .Where(js => user.SkillNames.Any(un => string.Equals(un, js, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var skillRatio = jobSkills.Count == 0 ? 0 : (double)matchedSkills.Count / jobSkills.Count;
        var skillScore = (int)Math.Round(WeightSkills * skillRatio);
        total += skillScore;
        parts.Add($"skills covered {matchedSkills.Count}/{jobSkills.Count}");
        if (skillRatio < 1)
        {
            var missing = jobSkills.Except(matchedSkills).ToList();
            if (missing.Count > 0)
            {
                actions.Add($"Add these in-demand skills: {string.Join(", ", missing.Take(3))}");
            }
        }

        // ---- Title (0..20): preferred titles / neutral fallback ----
        var titleScore = 0;
        if (user.PreferredTitles.FirstOrDefault(t => job.Title.Contains(t, StringComparison.OrdinalIgnoreCase)) is not null)
        {
            titleScore = WeightTitle;
            parts.Add("title matches your target");
        }
        else if (user.PreferredTitles.Count == 0 && TechCategories.Contains(job.Category, StringComparer.OrdinalIgnoreCase))
        {
            // No explicit title preference: neutral 50% for mainstream tech families.
            titleScore = WeightTitle / 2;
            parts.Add("general tech category fit");
        }
        total += titleScore;

        // ---- Industry (0..15) ----
        int industryScore = 0;
        var industryPref = user.PreferredIndustries.FirstOrDefault(i => job.Category.Contains(i, StringComparison.OrdinalIgnoreCase)
            || i.Contains(job.Category, StringComparison.OrdinalIgnoreCase));
        if (industryPref is not null) { industryScore = WeightIndustry; parts.Add("industry preference matches"); }
        else if (user.PreferredIndustries.Count > 0)
            actions.Add("Widen your preferred industries in the profile to see more matches.");
        total += industryScore;

        // ---- Location (0..15) ----
        int locationScore = 0;
        var exactLocation = user.PreferredLocations.FirstOrDefault(l => job.Location.Contains(l, StringComparison.OrdinalIgnoreCase)) is not null
            || job.Location.Contains("remote", StringComparison.OrdinalIgnoreCase) && user.WorkModes.Any(w => w.Equals("remote", StringComparison.OrdinalIgnoreCase));
        var countryFallback = !user.HasExplicitLocationPref && job.Remote;
        if (exactLocation) { locationScore = WeightLocation; parts.Add("location/mode matches your preference"); }
        else if (countryFallback) { locationScore = WeightLocation / 2; parts.Add("remote-friendly posting"); }
        else if (user.HasExplicitLocationPref)
            actions.Add("This posting is outside your preferred locations.");
        total += locationScore;

        // ---- Work mode (0..10) ----
        int modeScore = 0;
        if (job.Remote && user.WorkModes.Any(w => w.Equals("remote", StringComparison.OrdinalIgnoreCase))) { modeScore = WeightWorkMode; }
        else if (job.Remote && user.WorkModes.Count == 0) modeScore = WeightWorkMode / 2;
        total += modeScore;
        if (job.Remote) parts.Add("remote");

        var explanation = $"Score {total}/100: {string.Join("; ", parts)}. Skills weight {WeightSkills}, title {WeightTitle}, industry {WeightIndustry}, location {WeightLocation}, work mode {WeightWorkMode}.";
        return (total, explanation, matchedSkills, jobSkills.Except(matchedSkills).ToList(), actions);
    }

    // ---------------- Skill gap ----------------

    public sealed record GapReport(string TargetRole, int CoveragePercent, List<SkillGapItem> Matched, List<SkillGapItem> Missing, List<string> Steps);

    public async Task<GapReport> SkillGapAsync(int userId, string? requestedRole)
    {
        var role = ResolveRole(requestedRole);
        var required = CareerCatalog.RoleSkills.TryGetValue(role, out var list) ? list : CareerCatalog.RoleSkills["Software Engineer"];

        var snapshot = await BuildSnapshotAsync(userId);
        var demandCounts = await db.JobListings.AsNoTracking()
            .Where(j => j.IsActive)
            .Select(j => new { j.Id, j.KeySkillsCsv })
            .ToListAsync();

        int SkillDemand(string skill) => demandCounts.Count(j =>
            (j.KeySkillsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(s => string.Equals(s.Trim(), skill, StringComparison.OrdinalIgnoreCase)));

        var matched = required
            .Where(r => snapshot.SkillNames.Any(sn => CareerCatalog.MatchesSkill(r, sn)))
            .Select((r, i) => new SkillGapItem(r, snapshot.SkillNames.FirstOrDefault(sn => CareerCatalog.MatchesSkill(r, sn)), "Have", SkillDemand(r).ToString()))
            .ToList();
        var missingList = required.Where(r => !matched.Any(m => m.Skill == r)).ToList();

        var missing = missingList.Select((r, idx) => new SkillGapItem(
            r, null,
            idx < 2 ? "High" : idx < 4 ? "Medium" : "Nice-to-have",
            SkillDemand(r).ToString())).ToList();

        double coverage = required.Length == 0 ? 100 : matched.Count * 100.0 / required.Length;
        var steps = missingList.Take(3).Select(s =>
            $"Practice {s} with a small project or course, then add it to your profile.").ToList();
        if (missingList.Count > 0) steps.Add($"Browse jobs tagged {string.Join(", ", missingList.Take(2))} to learn how employers describe these skills.");

        return new GapReport(role, (int)Math.Round(coverage), matched, missing, [.. steps]);
    }

    /// <summary>Pick a recognized catalog role; fall back to the graduate's
    /// first preferred title when it maps, else the default role.</summary>
    private static string ResolveRole(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exact = CareerCatalog.RoleSkills.Keys.FirstOrDefault(k => k.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            throw new ArgumentException($"Unknown role. Available: {string.Join(", ", CareerCatalog.RoleSkills.Keys)}");
        }
        return "Software Engineer";
    }

    /// <summary>Role chosen from the graduate's preferred titles (overview page).</summary>
    public async Task<string> GuessTargetRoleAsync(int userId)
    {
        var profile = await db.GraduateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        var first = SplitCsv(profile?.PreferredJobTitles).FirstOrDefault();
        if (first is not null)
        {
            var match = CareerCatalog.RoleSkills.Keys.FirstOrDefault(k => first.Contains(k, StringComparison.OrdinalIgnoreCase) || k.Contains(first, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return "Software Engineer";
    }
}
