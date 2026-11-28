using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class RoadmapService(AppDbContext db, GraduateAiService ai, CvFileService cvs, JobMatchingService matcher)
{
    public const int PassScore = 4;       // one mistake allowed out of five
    public const int LevelXp = 100;

    private sealed record PendingQuiz(string Id, int Level, GraduateAiService.PracticeQuestionData[] Questions);

    public async Task<GraduateQuestProgress> GetOrCreateProgressAsync(int userId)
    {
        var progress = await db.GraduateQuestProgress.FirstOrDefaultAsync(p => p.UserId == userId);
        if (progress is null)
        {
            progress = new GraduateQuestProgress { UserId = userId };
            db.GraduateQuestProgress.Add(progress);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.Entry(progress).State = EntityState.Detached;
                progress = await db.GraduateQuestProgress.FirstAsync(p => p.UserId == userId);
            }
        }
        return progress;
    }

    /// <summary>Job Readiness: 60% quest levels, 20% role skill coverage, 20% interview scores (0-10).</summary>
    public static int Readiness(int passedLevels, int levelCount, int skillCoverage, double averageInterviewScore) =>
        Math.Clamp((int)Math.Round(
            60.0 * passedLevels / levelCount + 0.2 * skillCoverage + 2.0 * averageInterviewScore), 0, 100);

    public async Task<QuestDto> GetQuestAsync(int userId)
    {
        var progress = await GetOrCreateProgressAsync(userId);
        var role = await matcher.TargetRoleAsync(userId);
        var levels = CareerCatalog.QuestLevels(role);
        var coverage = (await matcher.SkillGapAsync(userId, await matcher.GuessTargetRoleAsync(userId))).CoveragePercent;
        var scores = await db.InterviewQuestions.AsNoTracking()
            .Where(q => q.Session!.UserId == userId && q.FeedbackScore != null)
            .Select(q => q.FeedbackScore!.Value).ToListAsync();

        var readiness = Readiness(progress.UnlockedLevel - 1 - Skipped(progress).Count, levels.Length, coverage,
            scores.Count == 0 ? 0 : scores.Average());
        if (readiness != progress.ReadinessPercent)
        {
            progress.ReadinessPercent = readiness;
            progress.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return new QuestDto(role, progress.Xp, progress.QuizAttempts, readiness, PassScore,
            [.. levels.Select((l, i) => new QuestLevelDto(i + 1, l.Title, l.Topic,
                Skipped(progress).Contains(i + 1) ? "skipped"
                : i + 1 < progress.UnlockedLevel ? "completed"
                : i + 1 == progress.UnlockedLevel ? "active" : "locked"))]);
    }

    private static HashSet<int> Skipped(GraduateQuestProgress progress) =>
        [.. (progress.SkippedLevelsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];

    /// <summary>A level can be studied and quizzed while it is the current one, or after it was skipped.</summary>
    private async Task<(GraduateQuestProgress Progress, string Role, string Title, string Topic)> OpenLevelAsync(
        int userId, int level)
    {
        var progress = await GetOrCreateProgressAsync(userId);
        var role = await matcher.TargetRoleAsync(userId);
        var levels = CareerCatalog.QuestLevels(role);
        if (level < 1 || level > levels.Length || (level != progress.UnlockedLevel && !Skipped(progress).Contains(level)))
            throw new InvalidOperationException("This level is not open yet.");
        return (progress, role, levels[level - 1].Title, levels[level - 1].Topic);
    }

    private async Task<List<RagDocument>> LevelDocumentsAsync(int userId, string role)
    {
        var profile = await db.GraduateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        var gap = await matcher.SkillGapAsync(userId, await matcher.GuessTargetRoleAsync(userId));
        return
        [
            new("cv", "Candidate CV", await cvs.GetLatestTextAsync(userId) ?? ""),
            new("goal", "Career calibration",
                $"Target role: {role}. Experience: {profile?.ExperienceLevel}. Main struggle: {profile?.PrimaryStruggle}. " +
                $"Skills missing for the role: {string.Join(", ", gap.Missing.Select(m => m.Skill))}.")
        ];
    }

    // Level prep guide

    private sealed record CachedPrep(string Key, PrepItemDto[] Items);

    private static Dictionary<int, CachedPrep> ReadPrep(GraduateQuestProgress progress) =>
        progress.PrepJson is null ? [] : JsonSerializer.Deserialize<Dictionary<int, CachedPrep>>(progress.PrepJson)!;

    /// <summary>Generated once per level, and again only when the target role or the CV changes.</summary>
    public async Task<LevelPrepDto> GetPrepAsync(int userId, int level, CancellationToken ct)
    {
        var (progress, role, title, topic) = await OpenLevelAsync(userId, level);
        var cvId = await db.CvDocuments.Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UploadedAtUtc).Select(c => c.Id).FirstOrDefaultAsync(ct);
        var key = $"{role}|{cvId}";
        if (ReadPrep(progress).TryGetValue(level, out var cached) && cached.Key == key)
            return new LevelPrepDto(level, cached.Items);

        var items = await ai.GenerateLevelPrepAsync(role, title, topic, await LevelDocumentsAsync(userId, role), ct);

        await db.Entry(progress).ReloadAsync(ct); // the AI call is slow; do not overwrite newer guides
        var all = ReadPrep(progress);
        all[level] = new CachedPrep(key, items);
        progress.PrepJson = JsonSerializer.Serialize(all);
        await db.SaveChangesAsync(ct);
        return new LevelPrepDto(level, items);
    }

    public async Task<LevelPrepDto> SetReviewedAsync(int userId, int level, int index, bool reviewed)
    {
        var progress = await GetOrCreateProgressAsync(userId);
        var all = ReadPrep(progress);
        if (!all.TryGetValue(level, out var prep) || index < 0 || index >= prep.Items.Length)
            throw new KeyNotFoundException("Prep item not found.");
        prep.Items[index] = prep.Items[index] with { Reviewed = reviewed };
        progress.PrepJson = JsonSerializer.Serialize(all);
        await db.SaveChangesAsync();
        return new LevelPrepDto(level, prep.Items);
    }

    // Quiz

    public async Task<QuizDto> StartQuizAsync(int userId, int level, CancellationToken ct)
    {
        var (progress, role, title, topic) = await OpenLevelAsync(userId, level);
        var previous = progress.PendingQuizJson is null
            ? null
            : JsonSerializer.Deserialize<PendingQuiz>(progress.PendingQuizJson);
        var questions = await ai.GenerateQuestQuizAsync(role, title, topic,
            await LevelDocumentsAsync(userId, role), ct, previous?.Questions.Select(q => q.Question));

        progress.PendingQuizJson = JsonSerializer.Serialize(new PendingQuiz(Guid.NewGuid().ToString("N"), level, questions));
        await db.SaveChangesAsync(ct);
        return new QuizDto(level, [.. questions.Select(q => new QuizQuestionDto(q.Question, q.Options))]);
    }

    public async Task<QuizResultDto> SubmitQuizAsync(int userId, int level, IReadOnlyList<int> answers)
    {
        var progress = await GetOrCreateProgressAsync(userId);
        var json = progress.PendingQuizJson;
        var quiz = json is null ? null : JsonSerializer.Deserialize<PendingQuiz>(json);
        if (quiz is null || quiz.Level != level)
            throw new InvalidOperationException("There is no open quiz for this level. Start it again.");
        if (answers.Count != quiz.Questions.Length)
            throw new ArgumentException($"Answer all {quiz.Questions.Length} questions.");

        var review = quiz.Questions
            .Select((q, i) => new QuizReviewDto(answers[i] == q.CorrectIndex, q.CorrectIndex, q.Explanation))
            .ToList();
        var score = review.Count(r => r.Correct);
        var passed = score >= PassScore;
        var xp = passed ? LevelXp : 0;
        var unlock = passed && level == progress.UnlockedLevel ? 1 : 0;
        var skipped = Skipped(progress);
        if (passed) skipped.Remove(level);
        var skippedCsv = string.Join(',', skipped);
        // Level 0 marks the quiz as spent; its questions are kept so a retry gets different ones.
        var spent = JsonSerializer.Serialize(quiz with { Level = 0 });

        // Claim the quiz atomically so a double submit cannot award XP twice.
        var claimed = await db.GraduateQuestProgress
            .Where(p => p.UserId == userId && p.PendingQuizJson == json)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PendingQuizJson, spent)
                .SetProperty(p => p.QuizAttempts, p => p.QuizAttempts + 1)
                .SetProperty(p => p.Xp, p => p.Xp + xp)
                .SetProperty(p => p.UnlockedLevel, p => p.UnlockedLevel + unlock)
                .SetProperty(p => p.SkippedLevelsCsv, skippedCsv)
                .SetProperty(p => p.UpdatedAtUtc, DateTime.UtcNow));
        if (claimed == 0) throw new InvalidOperationException("This quiz was already submitted.");
        await db.Entry(progress).ReloadAsync();

        if (passed)
        {
            db.Activities.Add(new ActivityItem
            {
                UserId = userId,
                Kind = "quest",
                Message = $"Passed Career Quest level {level} (+{xp} XP)",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var state = await GetQuestAsync(userId); // persists the new readiness score
        return new QuizResultDto(passed, score, review.Count, xp, state.ReadinessPercent, review);
    }

    /// <summary>Unlocks the next level with no XP; a skipped level does not count towards readiness.</summary>
    public async Task<QuestDto> SkipLevelAsync(int userId, int level)
    {
        var progress = await GetOrCreateProgressAsync(userId);
        var levels = CareerCatalog.QuestLevels(await matcher.TargetRoleAsync(userId));
        if (level != progress.UnlockedLevel || level > levels.Length)
            throw new InvalidOperationException("Only your current level can be skipped.");
        progress.SkippedLevelsCsv = string.Join(',', Skipped(progress).Append(level));
        progress.UnlockedLevel++;
        progress.PendingQuizJson = null;
        progress.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await GetQuestAsync(userId);
    }

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
