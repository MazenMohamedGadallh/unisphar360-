using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Aggregates the graduate dashboard (overview), alumni/university services
/// and notification settings. All numbers are computed from real rows.
/// </summary>
public class GraduateCareerService(
    AppDbContext db,
    GraduateProfileService profileService,
    JobMatchingService matcher)
{
    private static readonly string[] CareerEventKeywords = ["career", "job", "hiring", "recruit", "resume", "cv", "internship", "employer", "company", "workshop", "fair", "interview", "alumni"];

    public async Task<GraduateOverviewDto> GetOverviewAsync(int userId)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var completion = await profileService.GetCompletionAsync(userId);

        var latestCv = await db.CvDocuments.AsNoTracking()
            .Where(c => c.UserId == userId).OrderByDescending(c => c.UploadedAtUtc).FirstOrDefaultAsync();
        var lastAnalysis = latestCv is null
            ? null
            : await db.CvAnalyses.AsNoTracking()
                .Where(a => a.CvDocumentId == latestCv.Id)
                .OrderByDescending(a => a.CreatedAtUtc).FirstOrDefaultAsync();
        var analysisCount = latestCv is null ? 0 :
            await db.CvAnalyses.AsNoTracking().CountAsync(a => a.CvDocumentId == latestCv.Id);

        var matches = await matcher.MatchForUserAsync(userId, limit: 50, minScore: 0);
        var threshold = (await GetSettingsEntityAsync(userId)).MatchAlertThresholdPercent;
        var matchingCount = matches.Count(m => m.ScorePercent >= 40);
        var matchingAbove = matches.Count(m => m.ScorePercent >= threshold);

        var savedCount = await db.SavedJobs.CountAsync(s => s.UserId == userId);
        var applications = await db.JobApplications.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
        var today = DateTime.UtcNow.Date;
        var upcomingFollowUps = applications.Count(a => a.FollowUpDate >= today && a.Status is not ("Rejected" or "Withdrawn"));
        var inProgress = applications.Count(a => a.Status is "Applied" or "Interview");

        var roadmap = await db.RoadmapItems.AsNoTracking().Where(r => r.UserId == userId).ToListAsync();
        var roadmapPercent = roadmap.Count == 0
            ? 0
            : (int)Math.Round(roadmap.Count(r => r.Status == "Completed") * 100.0 / roadmap.Count);

        var targetRole = await matcher.GuessTargetRoleAsync(userId);
        var gap = await matcher.SkillGapAsync(userId, targetRole);
        var topGaps = gap.Missing.Take(3).ToList();

        var events = await db.Events.AsNoTracking()
            .Where(e => e.StartsAtUtc >= DateTime.UtcNow)
            .OrderBy(e => e.StartsAtUtc).Take(8).ToListAsync();

        var now = DateTime.UtcNow;
        var activity = await db.Activities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAtUtc).Take(5).ToListAsync();

        var unread = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead).CountAsync();

        return new GraduateOverviewDto(
            user.FirstName, user.FullName,
            completion.Percent, completion.MissingFields,
            latestCv?.ParseStatus, latestCv?.UploadedAtUtc, lastAnalysis?.ScorePercent,
            matchingCount, matchingAbove,
            savedCount,
            applications.Count,
            inProgress, upcomingFollowUps, roadmapPercent,
            topGaps,
            NextActions(completion.MissingFields, latestCv, lastAnalysis, upcomingFollowUps, topGaps),
            [.. events.Take(4).Select(e => new GradEventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.Description))],
            [.. activity.Select(a => new GradActivityDto(a.Id, a.Kind, a.Message, a.CreatedAtUtc))],
            unread, analysisCount
        );

        static List<string> NextActions(IReadOnlyList<string> missing, CvDocument? cv, CvAnalysis? analysis,
            int followUps, List<SkillGapItem> gaps)
        {
            var actions = new List<string>();
            if (missing.Count > 0 && !missing.Contains("Uploaded CV"))
                actions.Add($"Add your {missing[0]}");
            if (cv is null)
                actions.Add("Upload your CV for AI analysis");
            else if (analysis is null && cv.ParseStatus is "Parsed" or "Manual")
                actions.Add("Run AI analysis on your latest CV");
            if (followUps > 0) actions.Add($"Follow up on {followUps} pending application(s)");
            if (gaps.Count > 0) actions.Add($"Close the skill gap: {gaps[0].Skill}");
            if (actions.Count == 0)
                actions.Add("You're on track — sync live jobs to find new matches");
            return [.. actions.Take(4)];
        }
    }

    // ---------------- Alumni / university services ----------------

    public async Task<GradAlumniDto> GetAlumniAsync(int userId)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var university = user.UniversityId is null ? null :
            await db.Universities.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.UniversityId);

        var events = await db.Events.AsNoTracking()
            .Where(e => e.StartsAtUtc >= DateTime.UtcNow)
            .OrderBy(e => e.StartsAtUtc).Take(12).ToListAsync();
        var careerEvents = events
            .Where(e => CareerEventKeywords.Any(k => (e.Title + " " + (e.Description ?? "")).Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Take(5).Select(e => new GradEventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.Description)).ToList();
        var rest = events.Where(e => careerEvents.All(ce => ce.Id != e.Id)).Take(5 - careerEvents.Count)
            .Select(e => new GradEventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.Description));

        var cutoff = DateTime.UtcNow.AddDays(-90);
        var announcements = await db.Announcements.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= cutoff)
            .OrderByDescending(a => a.CreatedAtUtc).Take(6).ToListAsync();

        return new GradAlumniDto(
            university?.Name,
            user.Department,
            user.GraduationYear?.ToString(),
            user.GraduatedFrom,
            careerEvents.Concat(rest).ToList(),
            [.. announcements.Select(a => new GradAnnouncementDto(a.Id, a.Title, a.Body, a.Category, a.CreatedAtUtc))],
            CareerCenterTipFor(user),
            ["UniSphere support: admin@unisphere.edu"]);
    }

    private static string CareerCenterTipFor(User user)
    {
        if (string.IsNullOrWhiteSpace(user.GraduatedFrom))
            return "Complete your graduation application to receive your alumni record.";
        return "Check your skill gap against your target role, then practice with the AI interview coach before applying.";
    }

    // ---------------- Settings ----------------

    public async Task<GraduateSettings> GetSettingsEntityAsync(int userId)
    {
        var settings = await db.GraduateSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        if (settings is null)
        {
            settings = new GraduateSettings { UserId = userId };
            db.GraduateSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<GradSettingsDto> UpdateSettingsAsync(
        int userId, bool notifyNewMatches, bool notifyFollowUps, int threshold, bool showGpa,
        string? theme = "system", string? alertKeywords = null, string? alertLocations = null)
    {
        var settings = await GetSettingsEntityAsync(userId);
        settings.NotifyNewMatches = notifyNewMatches;
        settings.NotifyFollowUps = notifyFollowUps;
        settings.MatchAlertThresholdPercent = Math.Clamp(threshold, 0, 100);
        settings.ShowGpaOnProfile = showGpa;
        if (theme is not null)
            settings.ThemePreference = theme is "light" or "dark" or "system" ? theme : "system";
        if (alertKeywords is not null)
            settings.AlertKeywords = TruncateAlertList(alertKeywords, 300);
        if (alertLocations is not null)
            settings.AlertLocations = TruncateAlertList(alertLocations, 300);
        await db.SaveChangesAsync();
        return new GradSettingsDto(
            settings.NotifyNewMatches, settings.NotifyFollowUps,
            settings.MatchAlertThresholdPercent, settings.ShowGpaOnProfile,
            settings.ThemePreference, settings.AlertKeywords, settings.AlertLocations);
    }

    public async Task<GradSettingsDto> GetSettingsAsync(int userId)
    {
        var s = await GetSettingsEntityAsync(userId);
        return new GradSettingsDto(
            s.NotifyNewMatches, s.NotifyFollowUps, s.MatchAlertThresholdPercent, s.ShowGpaOnProfile,
            s.ThemePreference, s.AlertKeywords, s.AlertLocations);
    }

    /// <summary>Alert filters are free-form comma lists; store trimmed,
    /// de-duplicated, capped values so regeneration stays predictable.</summary>
    private static string? TruncateAlertList(string? raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var items = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => i.Length > 40 ? i[..40] : i)
            .GroupBy(i => i, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .ToList();
        var joined = string.Join(", ", items);
        return joined.Length == 0 ? null : joined.Length > max ? joined[..max].TrimEnd() + "…" : joined;
    }
}
