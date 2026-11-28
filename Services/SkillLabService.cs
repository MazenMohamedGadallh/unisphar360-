using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public sealed record LabStartRequest(string? Skill, string? Difficulty);
public sealed record LabSubmitRequest(IReadOnlyList<int>? Answers, string? Written);
public sealed record LabQuestionDto(string Question, IReadOnlyList<string> Options);
public sealed record LabReviewDto(bool Correct, int CorrectIndex, string Explanation);

public sealed record LabAttemptDto(
    int Id, string Skill, string Difficulty, DateTime CreatedAtUtc, DateTime? SubmittedAtUtc, int? Score, bool Passed,
    string? Feedback, string Scenario, IReadOnlyList<LabQuestionDto> Questions, string Task, IReadOnlyList<string> Criteria,
    IReadOnlyList<LabReviewDto>? Review, IReadOnlyList<string>? Improvements, string? Written);

/// <summary>
/// Non-executable assessments: scenario questions plus a written task. Graduate code is never run on the server.
/// </summary>
public class SkillLabService(AppDbContext db, GraduateAiService ai)
{
    public const int PassScore = 70;
    public const int PointsPerQuestion = 15, QuestionCount = 4;   // 60 points; the written task is worth 40

    private sealed record Submission(int[] Answers, string Written, string[] Improvements);

    public static string[] Skills => [.. CareerCatalog.SkillRegexes.Keys.Order()];

    public static int Score(int correct, int writtenOutOfTen) =>
        correct * PointsPerQuestion + Math.Clamp(writtenOutOfTen, 0, 10) * 4;

    public async Task<LabAttemptDto> StartAsync(int userId, LabStartRequest req, CancellationToken ct)
    {
        var skill = Skills.FirstOrDefault(s => s.Equals(req.Skill?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose a skill from the list.");
        var difficulty = new[] { "Easy", "Medium", "Hard" }
            .FirstOrDefault(d => d.Equals(req.Difficulty, StringComparison.OrdinalIgnoreCase)) ?? "Medium";

        // An unfinished attempt is resumed, so repeated clicks do not cost another AI request.
        var open = await db.SkillChallengeAttempts.AsNoTracking().FirstOrDefaultAsync(
            a => a.UserId == userId && a.Skill == skill && a.Difficulty == difficulty && a.SubmittedAtUtc == null, ct);
        if (open is not null) return Map(open);

        var challenge = await ai.GenerateChallengeAsync(skill, difficulty, ct);
        var attempt = new SkillChallengeAttempt
        {
            UserId = userId, Skill = skill, Difficulty = difficulty, ChallengeJson = JsonSerializer.Serialize(challenge)
        };
        db.SkillChallengeAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return Map(attempt);
    }

    public async Task<LabAttemptDto> SubmitAsync(int userId, int id, LabSubmitRequest req, CancellationToken ct)
    {
        var attempt = await db.SkillChallengeAttempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Challenge not found.");
        if (attempt.SubmittedAtUtc != null) throw new InvalidOperationException("This challenge was already submitted.");

        var challenge = Read(attempt);
        var answers = req.Answers?.ToArray() ?? [];
        var written = req.Written?.Trim() ?? "";
        if (answers.Length != challenge.Questions.Length) throw new ArgumentException("Answer every question.");
        if (written.Length is < 40 or > 4000) throw new ArgumentException("Write between 40 and 4000 characters for the task.");

        // Nothing is stored unless grading succeeds, so a provider failure can simply be retried.
        var grade = await ai.GradeWrittenAsync(attempt.Skill, challenge.Task, challenge.Criteria, written, ct);
        var score = Score(challenge.Questions.Where((q, i) => answers[i] == q.CorrectIndex).Count(), grade.Score);
        var passed = score >= PassScore;
        var json = JsonSerializer.Serialize(new Submission(answers, written, grade.Improvements));
        var now = DateTime.UtcNow;

        var claimed = await db.SkillChallengeAttempts
            .Where(a => a.Id == id && a.UserId == userId && a.SubmittedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.SubmissionJson, json)
                .SetProperty(a => a.Score, score)
                .SetProperty(a => a.Passed, passed)
                .SetProperty(a => a.Feedback, grade.Feedback)
                .SetProperty(a => a.SubmittedAtUtc, now), ct);
        if (claimed == 0) throw new InvalidOperationException("This challenge was already submitted.");

        db.Activities.Add(new ActivityItem
        {
            UserId = userId, Kind = "skill", CreatedAtUtc = now,
            Message = $"{(passed ? "Passed" : "Attempted")} the {attempt.Skill} challenge ({score}/100)"
        });
        await db.SaveChangesAsync(ct);
        return Map(await db.SkillChallengeAttempts.AsNoTracking().FirstAsync(a => a.Id == id, ct));
    }

    public async Task<IReadOnlyList<LabAttemptDto>> ListAsync(int userId) =>
        [.. (await db.SkillChallengeAttempts.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Id).Take(50).ToListAsync()).Select(Map)];

    private static GraduateAiService.ChallengeData Read(SkillChallengeAttempt a) =>
        JsonSerializer.Deserialize<GraduateAiService.ChallengeData>(a.ChallengeJson)!;

    /// <summary>The answer key and explanations leave the server only after submission.</summary>
    private static LabAttemptDto Map(SkillChallengeAttempt a)
    {
        var c = Read(a);
        var sub = a.SubmissionJson is null ? null : JsonSerializer.Deserialize<Submission>(a.SubmissionJson);
        return new LabAttemptDto(
            a.Id, a.Skill, a.Difficulty, a.CreatedAtUtc, a.SubmittedAtUtc, a.Score, a.Passed, a.Feedback,
            c.Scenario, [.. c.Questions.Select(q => new LabQuestionDto(q.Question, q.Options))], c.Task, c.Criteria,
            sub is null ? null : [.. c.Questions.Select((q, i) => new LabReviewDto(sub.Answers[i] == q.CorrectIndex, q.CorrectIndex, q.Explanation))],
            sub?.Improvements, sub?.Written);
    }
}
