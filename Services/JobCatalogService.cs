using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Job catalog: DB search, saved jobs and live sync from the Remotive public
/// GraphQL-less JSON endpoint (no API key required). Demo-labeled rows
/// (Source="Demo") guarantee offline exploration; remote rows are labeled
/// Source="Remotive" and always link to the original posting.
/// </summary>
public class JobCatalogService(AppDbContext db, IHttpClientFactory httpFactory, ILogger<JobCatalogService> logger)
{
    public const int ThrottleMinutes = 5;

    public async Task<PagedJobsDto> SearchAsync(int userId, int page, int pageSize,
        string? search, string? location, string? industry, string? remote, string? sort)
    {
        page = Math.Clamp(page, 1, 200);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = db.JobListings.AsNoTracking().Where(j => j.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(j => j.Title.Contains(term) || j.CompanyName.Contains(term) ||
                             j.Description.Contains(term) || j.KeySkillsCsv.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(location))
        {
            q = location.Trim().Equals("remote", StringComparison.OrdinalIgnoreCase)
                ? q.Where(j => j.Remote)
                : q.Where(j => j.Location.Contains(location.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(industry))
            q = q.Where(j => j.Category == industry.Trim());
        if (remote is { Length: > 0 } &&
            (remote.Equals("true", StringComparison.OrdinalIgnoreCase) || remote == "1"))
            q = q.Where(j => j.Remote);

        q = sort?.Trim().ToLowerInvariant() switch
        {
            "title" => q.OrderBy(j => j.Title).ThenBy(j => j.CompanyName),
            "company" => q.OrderBy(j => j.CompanyName).ThenBy(j => j.Title),
            "oldest" => q.OrderBy(j => j.PublishedAtUtc),
            _ => q.OrderByDescending(j => j.PublishedAtUtc).ThenBy(j => j.Title)
        };

        var total = await q.CountAsync();
        var jobs = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var saved = await db.SavedJobs.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.JobListingId).ToListAsync();
        var savedSet = saved.ToHashSet();
        var applied = await db.JobApplications.AsNoTracking().Where(a => a.UserId == userId && a.JobListingId != null).Select(a => a.JobListingId!).ToListAsync();
        var appliedSet = applied.ToHashSet();

        return new PagedJobsDto(
            [.. jobs.Select(j => MapJob(j, savedSet.Contains(j.Id), appliedSet.Contains(j.Id)))],
            total, page, pageSize);
    }

    public async Task<JobListingDto?> GetAsync(int userId, int id)
    {
        var job = await db.JobListings.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.IsActive);
        if (job is null) return null;
        var isSaved = await db.SavedJobs.AnyAsync(s => s.UserId == userId && s.JobListingId == id);
        var isApplied = await db.JobApplications.AnyAsync(a => a.UserId == userId && a.JobListingId == id);
        return MapJob(job, isSaved, isApplied);
    }

    public async Task<JobListingDto> SaveJobAsync(int userId, int jobId)
    {
        var job = await db.JobListings.FirstOrDefaultAsync(j => j.Id == jobId && j.IsActive)
            ?? throw new KeyNotFoundException("Job not found.");
        var exists = await db.SavedJobs.AnyAsync(s => s.UserId == userId && s.JobListingId == jobId);
        if (exists) throw new InvalidOperationException("This job is already in your saved list.");

        db.SavedJobs.Add(new SavedJob { UserId = userId, JobListingId = jobId, SavedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return MapJob(job, true, false, snippet: false);
    }

    public async Task<bool> UnsaveJobAsync(int userId, int jobId) =>
        await db.SavedJobs.Where(s => s.UserId == userId && s.JobListingId == jobId).ExecuteDeleteAsync() > 0;

    public async Task<IReadOnlyList<JobListingDto>> ListSavedAsync(int userId)
    {
        var saved = await db.SavedJobs.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SavedAtUtc)
            .Include(s => s.JobListing)
            .ToListAsync();
        var appliedSet = (await db.JobApplications.AsNoTracking()
                .Where(a => a.UserId == userId && a.JobListingId != null).Select(a => a.JobListingId!).ToListAsync())
            .ToHashSet();
        return [.. saved.Where(s => s.JobListing is not null).Select(s => MapJob(s.JobListing!, true, appliedSet.Contains(s.JobListingId)))];
    }

    // ---------------- Live sync (Remotive) ----------------

    public sealed record SyncResult(int Imported, bool Throttled, string? Error, DateTime? LastSyncUtc);

    public async Task<SyncResult> SyncRemotiveAsync(CancellationToken ct)
    {
        var state = GetState(db);
        if (state.LastSyncUtc is { } last && (DateTime.UtcNow - last).TotalMinutes < ThrottleMinutes)
            return new SyncResult(0, true, null, state.LastSyncUtc);

        int imported = 0;
        string? error = null;
        try
        {
            var client = httpFactory.CreateClient("Remotive");
            using var resp = await client.GetAsync("/api/remote-jobs?limit=50", ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!payload.RootElement.TryGetProperty("jobs", out var jobsEl) || jobsEl.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Unexpected response from the job feed.");

            var existing = await db.JobListings.IgnoreQueryFilters()
                .Where(j => j.Source == "Remotive")
                .Select(j => new { j.SourceId })
                .ToDictionaryAsync(x => x.SourceId, ct);

            int cap = 50, seen = 0;
            foreach (var j in jobsEl.EnumerateArray())
            {
                if (seen++ >= cap) break;
                var sourceId = j.TryGetProperty("id", out var idEl) ? idEl.ValueKind switch
                {
                    JsonValueKind.Number => idEl.GetRawText(),
                    _ => idEl.ToString()
                } : "";
                if (string.IsNullOrEmpty(sourceId) || existing.ContainsKey(sourceId)) continue;

                var title = GetString(j, "title");
                var url = GetString(j, "url");
                if (title.Length == 0 || url.Length == 0) continue;

                var description = HtmlToText(GetString(j, "description"));
                if (description.Length < 40) description = title + " — see the original posting for the full description.";

                db.JobListings.Add(new JobListing
                {
                    Source = "Remotive",
                    SourceId = sourceId,
                    Title = title,
                    CompanyName = GetString(j, "company_name"),
                    Location = GetString(j, "candidate_required_location"),
                    Remote = true,
                    JobType = GetString(j, "job_type"),
                    Category = GetString(j, "category"),
                    Salary = string.IsNullOrWhiteSpace(GetString(j, "salary")) ? null : GetString(j, "salary"),
                    Url = url,
                    Description = description[..Math.Min(2500, description.Length)],
                    KeySkillsCsv = string.Join(",", CareerCatalog.DetectSkills(title + " " + description)),
                    PublishedAtUtc = TrygetDate(j, "publication_date") ?? DateTime.UtcNow,
                    FetchedAtUtc = DateTime.UtcNow
                });
                imported++;
            }

            state.LastSyncUtc = DateTime.UtcNow;
            state.LastStatus = "OK";
            state.LastMessage = imported == 0 ? "No new jobs since the last sync." : $"Imported {imported} new jobs.";
            state.LastImportedCount = imported;
            await db.SaveChangesAsync();
            logger.LogInformation("Remotive job sync imported {Count} jobs", imported);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Remotive job sync failed");
            state.LastSyncUtc = DateTime.UtcNow;
            state.LastStatus = "Failed";
            state.LastMessage = "The live job feed is temporarily unreachable. Demo jobs remain available.";
            await db.SaveChangesAsync();
            error = "The live job feed is temporarily unreachable. Demo jobs remain available.";
            return new SyncResult(0, false, error, state.LastSyncUtc);
        }

        return new SyncResult(imported, false, null, state.LastSyncUtc);
    }

    private static JobSyncState GetState(AppDbContext db)
    {
        var state = db.JobSyncStates.FirstOrDefault(s => s.Id == 1);
        if (state is null)
        {
            state = new JobSyncState { Id = 1, Source = "Remotive" };
            db.JobSyncStates.Add(state);
        }
        return state;
    }

    // ---------------- Insights ----------------

    public async Task<GradInsightsDto> GetInsightsAsync()
    {
        var jobs = await db.JobListings.AsNoTracking()
            .Where(j => j.IsActive)
            .Select(j => new { j.Title, j.Category, j.Remote, j.KeySkillsCsv })
            .ToListAsync();

        var state = await db.JobSyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);

        var topSkills = jobs
            .SelectMany(j => (j.KeySkillsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(8).ToList();

        var byIndustry = jobs
            .Where(j => j.Category.Length > 0)
            .GroupBy(j => j.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name).ToList();

        var byType = jobs
            .GroupBy(j => j.Remote ? "Remote" : "On-site")
            .Select(g => new NameCount(g.Key, g.Count()))
            .ToList();

        return new GradInsightsDto(
            jobs.Count, state?.LastSyncUtc, topSkills, byIndustry, byType,
            [.. jobs.Take(12).Select(j => j.Title).Distinct()]);
    }

    // ---------------- mapping ----------------

    internal static JobListingDto MapJob(JobListing j, bool isSaved, bool isApplied, bool snippet = true) => new(
        j.Id, j.Source, j.Title, j.CompanyName, j.Location, j.Remote, j.JobType, j.Category, j.Salary,
        j.Url, j.PublishedAtUtc,
        [.. (j.KeySkillsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8)],
        isSaved, isApplied,
        snippet ? (j.Description.Length > 220 ? j.Description[..220] + "…" : j.Description) : null);

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static DateTime? TrygetDate(JsonElement el, string name)
        => DateTime.TryParse(GetString(el, name), out var d) ? d : null;

    internal static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<script.*?</script>|<style.*?</style>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t\r\n]+", " ");
        return text.Trim();
    }
}

[Serializable]
public class JobSyncThrottleException(string message) : Exception(message);
