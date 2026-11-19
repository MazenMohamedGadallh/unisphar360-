using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Career roadmap CRUD + AI-assisted suggestions. Statuses are normalized;
/// completion is derived, never stored (single source of truth).
/// </summary>
public class RoadmapService(AppDbContext db, GraduateAiService ai)
{
    private static readonly string[] Statuses = ["NotStarted", "InProgress", "Completed"];

    public async Task<IReadOnlyList<RoadmapItemDto>> ListAsync(int userId)
    {
        var items = await db.RoadmapItems.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync();
        return [.. items.Select(Map)];
    }

    public async Task<RoadmapProgressDto> GetProgressAsync(int userId)
    {
        var items = await db.RoadmapItems.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.Status).ToListAsync();
        var completed = items.Count(s => s == "Completed");
        return new RoadmapProgressDto(
            items.Count,
            items.Count(s => s == "NotStarted"),
            items.Count(s => s == "InProgress"),
            completed,
            items.Count == 0 ? 0 : (int)Math.Round(completed * 100.0 / items.Count));
    }

    public async Task<RoadmapItemDto> CreateAsync(int userId, RoadmapItemCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new MissingFieldException("title");
        var nextSort = await db.RoadmapItems.Where(r => r.UserId == userId).MaxAsync(r => (int?)r.SortOrder) ?? 0;
        var item = new RoadmapItem
        {
            UserId = userId,
            Title = req.Title.Trim(),
            Category = string.IsNullOrWhiteSpace(req.Category) ? "Milestone" : req.Category.Trim(),
            Status = NormalizeStatus(string.IsNullOrWhiteSpace(req.Status) ? "NotStarted" : req.Status),
            Notes = req.Notes,
            TargetDate = req.TargetDate,
            SortOrder = req.SortOrder ?? nextSort + 1,
            Origin = "Manual"
        };
        ApplyStatusTime(item, null, item.Status);
        db.RoadmapItems.Add(item);
        await db.SaveChangesAsync();
        return Map(item);
    }

    public async Task<bool> UpdateAsync(int userId, int id, RoadmapItemUpdateRequest req)
    {
        var item = await db.RoadmapItems.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (item is null) return false;
        var previous = item.Status;
        if (req.Title is { Length: > 0 }) item.Title = req.Title.Trim();
        if (req.Category is { Length: > 0 }) item.Category = req.Category.Trim();
        if (req.Notes is not null) item.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        if (req.TargetDate is not null) item.TargetDate = req.TargetDate;
        if (req.SortOrder is not null) item.SortOrder = req.SortOrder.Value;
        item.Status = NormalizeStatus(string.IsNullOrWhiteSpace(req.Status) ? item.Status : req.Status);
        ApplyStatusTime(item, previous, item.Status);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int userId, int id) =>
        await db.RoadmapItems.Where(r => r.UserId == userId && r.Id == id).ExecuteDeleteAsync() > 0;

    /// <summary>AI suggestions (real provider required) — returned as a list;
    /// applying is an explicit separate action the user takes.</summary>
    public async Task<(IReadOnlyList<RoadmapSuggestionDto> Suggestions, GraduateAiService.AiMeta Meta)> SuggestAsync(
        int userId, CancellationToken ct)
    {
        var profile = await db.GraduateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        var skills = await db.GraduateSkills.AsNoTracking().Where(s => s.UserId == userId)
            .Select(s => s.Name).ToListAsync();
        var goal = string.IsNullOrWhiteSpace(profile?.CareerGoal) ? "Land a first engineering job" : profile!.CareerGoal!;
        var summary = skills.Count == 0 ? "(no skills added yet)" : string.Join(", ", skills);

        var (data, meta) = await ai.SuggestRoadmapAsync(userId, goal, summary, ct);
        return ([.. data.Items.Select(i => new RoadmapSuggestionDto(i.Title, i.Category, i.Notes, i.Why))], meta);
    }

    /// <summary>Create roadmap rows from suggestion payloads (bulk).</summary>
    public async Task<IReadOnlyList<RoadmapItemDto>> ApplySuggestionsAsync(
        int userId, IReadOnlyList<RoadmapSuggestionDto> picked)
    {
        var nextSort = await db.RoadmapItems.Where(r => r.UserId == userId).MaxAsync(r => (int?)r.SortOrder) ?? 0;
        var items = new List<RoadmapItem>();
        foreach (var p in picked.Take(10))
        {
            if (string.IsNullOrWhiteSpace(p.Title)) continue;
            nextSort++;
            items.Add(new RoadmapItem
            {
                UserId = userId,
                Title = p.Title.Trim(),
                Category = string.IsNullOrWhiteSpace(p.Category) ? "Milestone" : p.Category.Trim(),
                Status = "NotStarted",
                Notes = p.Notes,
                SortOrder = nextSort,
                Origin = "AiSuggestion"
            });
        }
        db.RoadmapItems.AddRange(items);
        await db.SaveChangesAsync();
        return [.. items.Select(Map)];
    }

    internal static void ApplyStatusTime(RoadmapItem item, string? previous, string status)
    {
        if (status == "Completed" && previous != "Completed") item.CompletedAtUtc = DateTime.UtcNow;
        if (status != "Completed") item.CompletedAtUtc = null;
    }

    internal static string NormalizeStatus(string status) =>
        Statuses.FirstOrDefault(s => s.Equals(status.Trim().Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) ?? "NotStarted";

    internal static RoadmapItemDto Map(RoadmapItem r) => new(
        r.Id, r.Title, r.Category, r.Status, r.Notes, r.TargetDate, r.SortOrder, r.Origin, r.CompletedAtUtc);
}
