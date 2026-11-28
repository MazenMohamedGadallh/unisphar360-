using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class JobApplicationService(AppDbContext db)
{
    public static readonly string[] Statuses = ["Applied", "Under Review", "Interview", "Assessment", "Offer", "Rejected", "Withdrawn"];

    public async Task<IReadOnlyList<ApplicationDto>> ListAsync(int userId, string? status)
    {
        var q = db.JobApplications.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAtUtc);
        if (!string.IsNullOrWhiteSpace(status))
            q = (IOrderedQueryable<JobApplication>)q.Where(a => a.Status == status.Trim());

        return [.. (await q.ToListAsync()).Select(Map)];
    }

    public async Task<ApplicationSummaryDto> GetSummaryAsync(int userId)
    {
        var apps = await db.JobApplications.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
        var today = DateTime.UtcNow.Date;
        return new ApplicationSummaryDto(
            apps.Count,
            [.. Statuses.Select(s => new StatusCount(s, apps.Count(a => a.Status == s))).Where(x => x.Count > 0)],
            apps.Count(a => a.FollowUpDate >= today && a.Status is not ("Rejected" or "Withdrawn")),
            apps.Count(a => a.Status == "Interview"));
    }

    /// <summary>Counts only what the graduate recorded; no guesses about why an employer decided anything.</summary>
    public async Task<ApplicationAnalyticsDto> GetAnalyticsAsync(int userId, IReadOnlyCollection<string> evidencedSkills)
    {
        var apps = await db.JobApplications.AsNoTracking().Include(a => a.JobListing).Where(a => a.UserId == userId).ToListAsync();
        var interviewed = apps.Count(a => a.InterviewAtUtc != null || a.Status is "Interview" or "Assessment" or "Offer");
        var roles = apps.GroupBy(a => a.JobTitle, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameCount(g.Key, g.Count())).OrderByDescending(x => x.Count).Take(5).ToList();
        var missing = apps.Where(a => a.JobListing != null)
            .SelectMany(a => CareerEvidenceService.Csv(a.JobListing!.KeySkillsCsv).Distinct(StringComparer.OrdinalIgnoreCase))
            .Where(s => !evidencedSkills.Any(e => CareerCatalog.MatchesSkill(s, e)))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameCount(g.Key, g.Count())).OrderByDescending(x => x.Count).Take(5).ToList();

        var insights = new List<string>();
        if (apps.Count < 5)
            insights.Add($"Record at least 5 applications for patterns to mean something. You have {apps.Count}.");
        else
        {
            if (interviewed == 0) insights.Add("None of your applications has reached an interview yet. Review your CV analysis and tailor each application.");
            if (missing.Count > 0) insights.Add($"{missing[0].Name} appears in {missing[0].Count} job(s) you applied to and is not evidenced in your CV, portfolio or challenges.");
        }
        return new ApplicationAnalyticsDto(
            apps.Count, interviewed, apps.Count == 0 ? null : (int)Math.Round(interviewed * 100.0 / apps.Count),
            [.. Statuses.Select(s => new StatusCount(s, apps.Count(a => a.Status == s))).Where(x => x.Count > 0)],
            roles, missing, insights);
    }

    public static string NormalizeStatus(string status) =>
        Statuses.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Applied";

    public async Task<ApplicationDto> CreateAsync(int userId, ApplicationCreateRequest req)
    {
        var company = req.CompanyName?.Trim() ?? "";
        var title = req.JobTitle?.Trim() ?? "";

        JobListing? linkedJob = null;
        if (req.JobListingId is int listingId)
        {
            linkedJob = await db.JobListings.FirstOrDefaultAsync(j => j.Id == listingId);
            if (linkedJob is null)
                throw new KeyNotFoundException("The linked job listing does not exist.");
            if (await db.JobApplications.AnyAsync(a => a.UserId == userId && a.JobListingId == listingId))
                throw new InvalidOperationException("You already recorded an application for this job.");
        }

        if (linkedJob is null)
        {
            if (company.Length == 0) throw new MissingFieldException("companyName");
            if (title.Length == 0) throw new MissingFieldException("jobTitle");
        }

        var app = new JobApplication
        {
            UserId = userId,
            JobListingId = linkedJob?.Id,
            CompanyName = linkedJob?.CompanyName ?? company,
            JobTitle = linkedJob?.Title ?? title,
            SourceUrl = string.IsNullOrWhiteSpace(req.SourceUrl) ? linkedJob?.Url : req.SourceUrl,
            Status = NormalizeStatus(string.IsNullOrWhiteSpace(req.Status) ? "Applied" : req.Status),
            Notes = req.Notes,
            FollowUpDate = req.FollowUpDate,
            InterviewAtUtc = req.InterviewAtUtc,
            AppliedAtUtc = req.AppliedAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.JobApplications.Add(app);
        await db.SaveChangesAsync();

        db.Activities.Add(new ActivityItem
        {
            UserId = userId, Kind = "application", Message = $"Application recorded: {app.JobTitle} at {app.CompanyName}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return Map(app);
    }

    public async Task<bool> UpdateAsync(int userId, int id, ApplicationUpdateRequest req)
    {
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.UserId == userId && a.Id == id);
        if (app is null) return false;
        if (req.Status is { Length: > 0 } s) app.Status = NormalizeStatus(s);
        if (req.Notes is not null) app.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        if (req.FollowUpDate is not null) app.FollowUpDate = req.FollowUpDate;
        if (req.InterviewAtUtc is not null) app.InterviewAtUtc = req.InterviewAtUtc;
        app.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id) =>
        await db.JobApplications.Where(a => a.UserId == userId && a.Id == id).ExecuteDeleteAsync() > 0;

    internal static ApplicationDto Map(JobApplication a) => new(
        a.Id, a.JobListingId, a.CompanyName, a.JobTitle, a.SourceUrl,
        a.Status, a.Notes, a.FollowUpDate, a.AppliedAtUtc, a.UpdatedAtUtc, a.InterviewAtUtc);
}
