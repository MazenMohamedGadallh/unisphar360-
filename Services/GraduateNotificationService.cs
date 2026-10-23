using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Real in-app career notifications for graduates, shown on the shared
/// /notifications page and counted by the graduate overview. Generation is
/// idempotent: each notification carries a per-user dedupe key, so running
/// the generator twice never duplicates rows (the store enforces the same
/// rule with a partial unique index).
///
/// Sources, all backed by real database rows:
///   - Strong new job matches (score &gt;= the graduate's alert threshold),
///     one per job — regenerated when the catalog syncs or the profile grows.
///   - Application follow-up reminders (interviews/deadlines in the next
///     7 days, or lately overdue active applications).
///   - University career-office announcements (category "career").
/// </summary>
public class GraduateNotificationService(
    AppDbContext db,
    JobMatchingService matching,
    GraduateCareerService career,
    ILogger<GraduateNotificationService> logger)
{
    public const string CategoryMatch = "CareerMatch";
    public const string CategoryFollowUp = "FollowUp";
    public const string CategoryAnnouncement = "CareerAnnouncement";

    // Application statuses that end a follow-up lifecycle (plain strings:
    // JobApplications stores free-text statuses).
    private const string StatusRejected = "Rejected";
    private const string StatusWithdrawn = "Withdrawn";

    /// <summary>Run every generator for one graduate. Returns how many
    /// new notifications were created.</summary>
    public async Task<int> GenerateAllAsync(int userId)
    {
        int created = 0;
        var matches = await EnsureMatchAlertsAsync(userId);
        var followUps = await EnsureFollowUpRemindersAsync(userId);
        var announcements = await EnsureAnnouncementNoticesAsync(userId);
        created += matches + followUps + announcements;
        return created;
    }

    /// <summary>Notifications for strong NEW matches. Settings gate it
    /// (NotifyNewMatches + threshold). Dedupe key "match:{jobId}" keeps one
    /// notification per job regardless of how the score changes afterwards.
    /// Precomputed matches (overview flow) are reused to avoid a second
    /// scoring pass.</summary>
    public async Task<int> EnsureMatchAlertsAsync(int userId, IReadOnlyList<JobMatchDto>? precomputed = null)
    {
        var settings = await EnsureSettingsAsync(userId);
        if (!settings.NotifyNewMatches) return 0;

        var matches = precomputed ?? await matching.MatchForUserAsync(userId, limit: 50, minScore: 0);
        var threshold = Math.Max(0, settings.MatchAlertThresholdPercent);
        var strong = matches
            .Where(m => m.ScorePercent >= threshold)
            .OrderByDescending(m => m.ScorePercent)
            .Take(10)
            .ToList();
        if (strong.Count == 0) return 0;

        var keys = strong.Select(m => MatchKey(m.Job.Id)).ToList();
        var existing = await ExistingKeysAsync(userId, keys);
        var pending = strong.Where(m => !existing.Contains(MatchKey(m.Job.Id))).ToList();

        var rows = pending.Select(m => new Notification
        {
            UserId = userId,
            Category = CategoryMatch,
            DedupeKey = MatchKey(m.Job.Id),
            Title = $"New strong match: {m.Job.Title}",
            Body = $"{m.Job.CompanyName} matches your profile at {m.ScorePercent}%. {m.Explanation}",
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();
        if (rows.Count == 0) return 0;

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();
        logger.LogInformation("Graduate {UserId}: queued {Count} new-match notifications", userId, rows.Count);
        return rows.Count;
    }

    /// <summary>Application follow-ups: remind about dates in the next 7 days
    /// (one reminder per application per follow-up date) and stale overdue
    /// dates on still-active applications (one row per application). Settings
    /// gate it (NotifyFollowUps).</summary>
    public async Task<int> EnsureFollowUpRemindersAsync(int userId)
    {
        var settings = await EnsureSettingsAsync(userId);
        if (!settings.NotifyFollowUps) return 0;

        var today = DateTime.UtcNow.Date;
        var applications = (await db.JobApplications.AsNoTracking()
                .Where(a => a.UserId == userId && a.FollowUpDate != null)
                .ToListAsync())
            // Status filter is client-side: pattern matching is not EF-translatable.
            .Where(a => a.Status is not (StatusRejected or StatusWithdrawn))
            .ToList();

        var dueSoon = applications
            .Where(a => a.FollowUpDate!.Value >= today && a.FollowUpDate.Value <= today.AddDays(7))
            .ToList();
        var overdue = applications.Where(a => a.FollowUpDate!.Value < today).ToList();
        if (dueSoon.Count == 0 && overdue.Count == 0) return 0;

        var keys = dueSoon.Select(a => FollowUpKey(a.Id, a.FollowUpDate!.Value))
            .Concat(overdue.Select(a => FollowUpOverdueKey(a.Id)))
            .ToList();
        var existing = await ExistingKeysAsync(userId, keys);

        var rows = new List<Notification>();
        foreach (var a in dueSoon)
        {
            var key = FollowUpKey(a.Id, a.FollowUpDate!.Value);
            if (existing.Contains(key)) continue;
            rows.Add(new Notification
            {
                UserId = userId,
                Category = CategoryFollowUp,
                DedupeKey = key,
                Title = $"Follow up soon: {a.CompanyName}",
                Body = $"Your {a.JobTitle} application has a follow-up on {a.FollowUpDate:ddd, d MMM yyyy}. Log the outcome in My Applications.",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        foreach (var a in overdue)
        {
            var key = FollowUpOverdueKey(a.Id);
            if (existing.Contains(key)) continue;
            rows.Add(new Notification
            {
                UserId = userId,
                Category = CategoryFollowUp,
                DedupeKey = key,
                Title = $"Overdue follow-up: {a.CompanyName}",
                Body = $"The follow-up for your {a.JobTitle} application ({a.FollowUpDate:dd MMM yyyy}) passed without a logged outcome. Update its status in My Applications.",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        if (rows.Count == 0) return 0;

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();
        logger.LogInformation("Graduate {UserId}: queued {Count} follow-up reminders", userId, rows.Count);
        return rows.Count;
    }

    /// <summary>University career office announcements: the last 30 days of
    /// "career" category rows. Dedupe key "announcement:{id}"; no settings
    /// toggle for these because they are university-wide messages.</summary>
    public async Task<int> EnsureAnnouncementNoticesAsync(int userId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var announcements = await db.Announcements.AsNoTracking()
            .Where(a => a.Category == "career" && a.CreatedAtUtc >= cutoff)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(5)
            .ToListAsync();
        if (announcements.Count == 0) return 0;

        var keys = announcements.Select(a => AnnouncementKey(a.Id)).ToList();
        var existing = await ExistingKeysAsync(userId, keys);
        var rows = announcements
            .Where(a => !existing.Contains(AnnouncementKey(a.Id)))
            .Select(a => new Notification
            {
                UserId = userId,
                Category = CategoryAnnouncement,
                DedupeKey = AnnouncementKey(a.Id),
                Title = $"Career office: {a.Title}",
                Body = string.IsNullOrWhiteSpace(a.Body) ? "A new career announcement was posted by your university." : Truncate(a.Body, 280),
                CreatedAtUtc = DateTime.UtcNow
            })
            .ToList();
        if (rows.Count == 0) return 0;

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();
        logger.LogInformation("Graduate {UserId}: queued {Count} career announcement notices", userId, rows.Count);
        return rows.Count;
    }

    // ---------------- helpers ----------------

    private Task<List<string>> ExistingKeysAsync(int userId, IReadOnlyList<string> keys) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.DedupeKey != null && keys.Contains(n.DedupeKey))
            .Select(n => n.DedupeKey!)
            .ToListAsync();

    private async Task<GraduateSettings> EnsureSettingsAsync(int userId)
    {
        var settings = await db.GraduateSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        settings ??= await career.GetSettingsEntityAsync(userId); // creates the row if missing
        return settings;
    }

    public static string MatchKey(int jobId) => $"match:{jobId}";
    public static string FollowUpKey(int applicationId, DateTime date) =>
        $"followup:{applicationId}:{date:yyyyMMdd}";
    public static string FollowUpOverdueKey(int applicationId) => $"followup:{applicationId}:overdue";
    public static string AnnouncementKey(int announcementId) => $"announcement:{announcementId}";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
