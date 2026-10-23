using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Interview practice: sessions persist real questions either from the
/// configured AI provider (source "Ai") or the curated question bank
/// (source "Curated" — real content, never labeled as AI output).
/// Answer feedback always requires a real provider.
/// </summary>
public class InterviewService(AppDbContext db, GraduateAiService ai)
{
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

        List<string> questions;
        if (useAi)
        {
            try
            {
                var (data, meta) = await ai.GenerateInterviewQuestionsAsync(
                    userId, role, job?.Description, Math.Min(DefaultQuestionCount, 8), ct);
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
            questions = [.. CuratedRoleQuestions(role)];
            source = "Curated";
        }

        // Persist the real provenance: without this the entity keeps its
        // "Curated" default and the AI source label + provider/model audit
        // trail are lost for every reloaded session.
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
            session.CreatedAtUtc, 0, qas.Count, [.. qas.Select(MapQuestion)]);
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
            qCounts.TryGetValue(s.Id, out var tt) ? tt.Total : 0, []))];
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
            [.. questions.Select(MapQuestion)]);
    }

    public async Task<bool> DeleteSessionAsync(int userId, int id) =>
        await db.InterviewSessions.Where(s => s.UserId == userId && s.Id == id).ExecuteDeleteAsync() > 0;

    /// <summary>Save the candidate's answer and request REAL feedback from the
    /// provider. Without a provider this throws ChatProviderException (surfaced
    /// to the client as an explicit error) — feedback is never faked.</summary>
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
        try
        {
            var (feedback, meta) = await ai.FeedbackAnswerAsync(
                userId, session.TargetRole, question.QuestionText, answer, ct);
            question.Feedback = feedback.Feedback;
            question.FeedbackScore = feedback.Score;
        }
        catch (ChatProviderException)
        {
            await db.SaveChangesAsync(); // keep the typed answer even when the provider failed
            throw;
        }
        await db.SaveChangesAsync();
        return MapQuestion(question);
    }

    // ---- curated fallback (never presented as AI) ----

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

    internal static InterviewQuestionDto MapQuestion(InterviewQuestion q) =>
        new(q.Id, q.QuestionText, q.AnswerText, q.Feedback, q.FeedbackScore);
}
