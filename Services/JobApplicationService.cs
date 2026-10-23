using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// The graduate's personal application tracker. Every row records what the
/// graduate actually did (applied/ interviewing/ rejected) — the platform
/// never claims to submit anything to an employer on the user's behalf.
/// </summary>
public class JobApplicationService(AppDbContext db)
{
    private static readonly string[] Statuses = ["Applied", "Interview", "Offer", "Rejected", "Withdrawn"];

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
        }

        // Manual rows need a company + title; catalog-linked rows inherit
        // company/title/source URL from the listing itself.
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
        app.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id) =>
        await db.JobApplications.Where(a => a.UserId == userId && a.Id == id).ExecuteDeleteAsync() > 0;

    internal static ApplicationDto Map(JobApplication a) => new(
        a.Id, a.JobListingId, a.CompanyName, a.JobTitle, a.SourceUrl,
        a.Status, a.Notes, a.FollowUpDate, a.AppliedAtUtc, a.UpdatedAtUtc);
}
