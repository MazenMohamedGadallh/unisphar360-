using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class InterviewService(AppDbContext db, GraduateAiService ai, CvFileService cvs, RoadmapService quest)
{
    public const int DrillXp = 50;

    // Baseline: the session's score for the dimension. Scores: question index to the drill's score for it.
    private sealed record PendingDrill(
        string Id, string Role, string Dimension, string[] Questions, double Baseline, Dictionary<int, int> Scores);

    /// <summary>Lowest average rubric criterion; ties go to the first alphabetically.</summary>
    public static string? WeakestDimension(IEnumerable<RubricItemDto> rubric) =>
        rubric.GroupBy(r => r.Criterion, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Average(r => r.Score)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault()?.Key;

    public async Task<RecoveryDrillDto> StartDrillAsync(int userId, int sessionId, CancellationToken ct)
    {
        var session = await GetSessionAsync(userId, sessionId)
            ?? throw new KeyNotFoundException("Interview session not found.");
        var dimension = WeakestDimension(session.Questions!.SelectMany(q => q.Rubric))
            ?? throw new InvalidOperationException("Score at least one answer before starting a recovery drill.");

        var weakest = session.Questions!.Where(q => q.Rubric.Count > 0)
            .OrderBy(q => q.Rubric.Where(r => r.Criterion.Equals(dimension, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Score).DefaultIfEmpty(10).Min()).First();
        var notes = $"Weakest question: {weakest.QuestionText} Coach notes: {string.Join("; ", weakest.Improvements)}";
        var questions = await ai.GenerateDrillAsync(session.TargetRole, dimension, notes,
            [new("cv", "Candidate CV", await cvs.GetLatestTextAsync(userId) ?? "")], ct);

        var baseline = session.Questions!.SelectMany(q => q.Rubric)
            .Where(r => r.Criterion.Equals(dimension, StringComparison.OrdinalIgnoreCase))
            .Average(r => r.Score);
        var drill = new PendingDrill(
            Guid.NewGuid().ToString("N"), session.TargetRole, dimension, questions, baseline, []);
        var progress = await quest.GetOrCreateProgressAsync(userId);
        progress.PendingDrillJson = JsonSerializer.Serialize(drill);
        await db.SaveChangesAsync(ct);
        return new RecoveryDrillDto(drill.Id, dimension, questions, DrillXp);
    }

    public async Task<DrillAnswerDto> AnswerDrillAsync(int userId, DrillAnswerRequest req, CancellationToken ct)
    {
        var progress = await quest.GetOrCreateProgressAsync(userId);
        var drill = ReadDrill(progress.PendingDrillJson, req.DrillId);
        if (req.Index < 0 || req.Index >= drill.Questions.Length)
            throw new KeyNotFoundException("Drill question not found.");

        var (feedback, _) = await ai.FeedbackAnswerAsync(
            userId, drill.Role, drill.Questions[req.Index], req.AnswerText.Trim(), ct);

        // Re-read after the slow AI call: another answer may have been recorded meanwhile.
        await db.Entry(progress).ReloadAsync(ct);
        var json = progress.PendingDrillJson;
        drill = ReadDrill(json, req.DrillId);
        drill.Scores[req.Index] = feedback.Rubric
            .FirstOrDefault(r => r.Criterion.Equals(drill.Dimension, StringComparison.OrdinalIgnoreCase))?.Score
            ?? feedback.Score;
        var completed = drill.Scores.Count == drill.Questions.Length;
        var average = Math.Round(drill.Scores.Values.Average(), 1);
        var xp = completed ? DrillXp : 0;
        var next = completed ? null : JsonSerializer.Serialize(drill);

        var saved = await db.GraduateQuestProgress
            .Where(p => p.UserId == userId && p.PendingDrillJson == json)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PendingDrillJson, next)
                .SetProperty(p => p.Xp, p => p.Xp + xp)
                .SetProperty(p => p.UpdatedAtUtc, DateTime.UtcNow), ct);
        if (saved == 0) throw new InvalidOperationException("The drill changed while grading. Submit the answer again.");

        if (completed)
        {
            db.Activities.Add(new ActivityItem
            {
                UserId = userId,
                Kind = "interview",
                Message = $"Completed a {drill.Dimension} recovery drill (+{xp} XP)",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        await db.Entry(progress).ReloadAsync(ct);
        return new DrillAnswerDto(feedback.Feedback, feedback.Score, feedback.Improvements,
            drill.Scores.Count, completed, xp,
            Math.Round(drill.Baseline, 1), average, completed && average > drill.Baseline);
    }

    private static PendingDrill ReadDrill(string? json, string drillId)
    {
        var drill = json is null ? null : JsonSerializer.Deserialize<PendingDrill>(json);
        return drill?.Scores is not null && drill.Id == drillId
            ? drill
            : throw new KeyNotFoundException("This drill is finished or was replaced. Start a new one.");
    }

    /// <summary>Stores already-generated questions (job preparation) as a normal practice session.</summary>
    public async Task<InterviewSessionDto> CreateFromQuestionsAsync(
        int userId, string role, int jobId, IReadOnlyList<string> questions, string provider, string model)
    {
        var session = new InterviewSession
        {
            UserId = userId,
            TargetRole = NormalizedRole(role),
            JobListingId = jobId,
            QuestionSource = "Ai",
            ProviderName = provider,
            Model = model
        };
        db.InterviewSessions.Add(session);
        await db.SaveChangesAsync();
        db.InterviewQuestions.AddRange(questions.Select(q => new InterviewQuestion { SessionId = session.Id, QuestionText = q }));
        await db.SaveChangesAsync();
        return (await GetSessionAsync(userId, session.Id))!;
    }

    public const int DefaultQuestionCount = 5;

    public async Task<InterviewSessionDto> CreateSessionAsync(
        int userId, InterviewSessionCreateRequest req, CancellationToken ct)
    {
        var role = NormalizedRole(req.TargetRole);
        var job = req.JobListingId is int jobId
            ? await db.JobListings.FirstOrDefaultAsync(j => j.Id == jobId)
            : null;

        var useAi = req.UseAi != false;
        string source;
        string? providerName = null, model = null;

        var session = new InterviewSession
        {
            UserId = userId,
            TargetRole = role,
            JobListingId = job?.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        static string OneOf(string? value, string fallback, params string[] allowed) =>
            allowed.FirstOrDefault(a => a.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
        session.InterviewType = OneOf(req.InterviewType, "Technical", "HR", "Technical", "Behavioral");
        session.Difficulty = OneOf(req.Difficulty, "Medium", "Easy", "Medium", "Hard");

        List<string> questions;
        if (useAi)
        {
            try
            {
                var (data, meta) = await ai.GenerateInterviewQuestionsAsync(
                    userId, role, job?.Description, Math.Min(DefaultQuestionCount, 8), ct, session.InterviewType, session.Difficulty);
                questions = [.. data.Questions];
                source = "Ai";
                providerName = meta.ProviderName;
                model = meta.Model;
            }
            catch (ChatProviderException)
            {
                // Real provider error: rethrow so the client sees an explicit message.
                throw;
            }
        }
        else
        {
            questions = session.InterviewType == "Technical"
                ? [.. CuratedRoleQuestions(role)]
                : [.. CareerCatalog.InterviewQuestions["Behavioral"]];
            source = "Curated";
        }

        session.QuestionSource = source;
        session.ProviderName = providerName;
        session.Model = model;

        db.InterviewSessions.Add(session);
        await db.SaveChangesAsync();

        var qas = questions.Select((q, i) => new InterviewQuestion
        {
            SessionId = session.Id,
            QuestionText = q,
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();
        db.InterviewQuestions.AddRange(qas);

        db.Activities.Add(new ActivityItem
        {
            UserId = userId,
            Kind = "interview",
            Message = $"Interview practice started for {role} ({source})",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new InterviewSessionDto(
            session.Id, session.TargetRole, session.JobListingId, source, providerName, model,
            session.CreatedAtUtc, 0, qas.Count, [.. qas.Select(MapQuestion)], session.InterviewType, session.Difficulty);
    }

    public async Task<IReadOnlyList<InterviewSessionDto>> ListSessionsAsync(int userId)
    {
        var sessions = await db.InterviewSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc).Take(50).ToListAsync();
        var qCounts = await db.InterviewQuestions.AsNoTracking()
            .GroupBy(q => q.SessionId)
            .Select(g => new { SessionId = g.Key, Total = g.Count(), Answered = g.Count(x => x.AnswerText != null) })
            .ToDictionaryAsync(x => x.SessionId);
        return [.. sessions.Select(s => new InterviewSessionDto(
            s.Id, s.TargetRole, s.JobListingId, s.QuestionSource, s.ProviderName, s.Model,
            s.CreatedAtUtc,
            qCounts.TryGetValue(s.Id, out var c) ? c.Answered : 0,
            qCounts.TryGetValue(s.Id, out var tt) ? tt.Total : 0, [], s.InterviewType, s.Difficulty))];
    }

    public async Task<InterviewSessionDto?> GetSessionAsync(int userId, int id)
    {
        var session = await db.InterviewSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Id == id);
        if (session is null) return null;
        var questions = await db.InterviewQuestions.AsNoTracking()
            .Where(q => q.SessionId == id)
            .OrderBy(q => q.Id).ToListAsync();
        return new InterviewSessionDto(
            session.Id, session.TargetRole, session.JobListingId, session.QuestionSource, session.ProviderName, session.Model,
            session.CreatedAtUtc, questions.Count(q => q.AnswerText != null), questions.Count,
            [.. questions.Select(MapQuestion)], session.InterviewType, session.Difficulty);
    }

    public async Task<bool> DeleteSessionAsync(int userId, int id) =>
        await db.InterviewSessions.Where(s => s.UserId == userId && s.Id == id).ExecuteDeleteAsync() > 0;

    public async Task<InterviewQuestionDto> AnswerAsync(
        int userId, int sessionId, int questionId, string answer, CancellationToken ct)
    {
        var session = await db.InterviewSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Id == sessionId)
            ?? throw new KeyNotFoundException("Interview session not found.");
        var question = await db.InterviewQuestions
            .FirstOrDefaultAsync(q => q.SessionId == sessionId && q.Id == questionId)
            ?? throw new KeyNotFoundException("Interview question not found.");

        question.AnswerText = answer.Trim();
        // A new answer invalidates the grade of the previous one, even if the provider fails below.
        question.Feedback = null;
        question.FeedbackScore = null;
        question.FeedbackDetailsJson = null;
        try
        {
            var (feedback, meta) = await ai.FeedbackAnswerAsync(
                userId, session.TargetRole, question.QuestionText, answer, ct);
            question.Feedback = feedback.Feedback;
            question.FeedbackScore = feedback.Score;
            question.FeedbackDetailsJson = JsonSerializer.Serialize(
                new InterviewFeedbackDetails(feedback.Strengths, feedback.Improvements, feedback.Rubric, feedback.StrongerAnswer));
        }
        catch (ChatProviderException)
        {
            await db.SaveChangesAsync(); // keep the typed answer even when the provider failed
            throw;
        }
        await db.SaveChangesAsync();
        return MapQuestion(question);
    }

    // curated fallback (never presented as AI)

    internal static string[] CuratedRoleQuestions(string role)
    {
        var technical = CareerCatalog.InterviewQuestions["Technical"];
        var behavioral = CareerCatalog.InterviewQuestions["Behavioral"];
        var roleSpecific = CareerCatalog.InterviewQuestions["Role specific"]
            .Select(q => q.Replace("{role}", role)).ToArray();
        return [.. technical.Take(2).Concat(behavioral.Take(1)).Concat(roleSpecific.Take(2))];
    }

    internal static string NormalizedRole(string? role)
    {
        var trimmed = (role ?? "").Trim();
        if (trimmed.Length == 0) return "Software Engineer";
        return trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }

    public static InterviewQuestionDto MapQuestion(InterviewQuestion q)
    {
        // Answers graded before this column existed have no details.
        var details = q.FeedbackDetailsJson is null
            ? null
            : JsonSerializer.Deserialize<InterviewFeedbackDetails>(q.FeedbackDetailsJson);
        return new(q.Id, q.QuestionText, q.AnswerText, q.Feedback, q.FeedbackScore,
            details?.Strengths ?? [], details?.Improvements ?? [], details?.Rubric ?? [], details?.StrongerAnswer);
    }
}
