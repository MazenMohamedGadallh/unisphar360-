using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;

namespace UniSphere.Api.Services;

public interface IQuizPublishService
{
    Task<QuizPublishOutcome> PublishAsync(
        int doctorUserId, QuizPublishRequest req, CancellationToken ct);
}

public sealed record QuizPublishOutcome(
    bool Ok, QuizPublishResponse? Result, string? Error, bool IsConflict = false);

public sealed class QuizPublishValidationException(string message) : Exception(message);

public sealed class DuplicateQuizException(string message) : Exception(message);

public class QuizPublishService : IQuizPublishService
{
    private const int DuplicateWindowMinutes = 2;

    private readonly AppDbContext _db;
    private readonly IExamAssistantService _exams;
    private readonly DoctorAccessService _access;
    private readonly ILogger<QuizPublishService> _logger;

    public QuizPublishService(
        AppDbContext db,
        IExamAssistantService exams,
        DoctorAccessService access,
        ILogger<QuizPublishService> logger)
    {
        _db = db;
        _exams = exams;
        _access = access;
        _logger = logger;
    }

    public async Task<QuizPublishOutcome> PublishAsync(
        int doctorUserId, QuizPublishRequest req, CancellationToken ct)
    {
        try
        {
            return new QuizPublishOutcome(true, await PublishCoreAsync(doctorUserId, req, ct), null);
        }
        catch (QuizPublishValidationException ex)
        {
            return new QuizPublishOutcome(false, null, ex.Message);
        }
        catch (DuplicateQuizException ex)
        {
            return new QuizPublishOutcome(false, null, ex.Message, IsConflict: true);
        }
        catch (AiUnavailableException ex)
        {
            // Reuse-published infra failure ("not owned", "already published").
            return new QuizPublishOutcome(false, null, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Quiz publish failed unexpectedly (doctor {DoctorId}, course {CourseId})",
                doctorUserId, req.CourseId);
            return new QuizPublishOutcome(false, null,
                "Quiz publication failed. Please try again.");
        }
    }

    private async Task<QuizPublishResponse> PublishCoreAsync(
        int doctorUserId, QuizPublishRequest req, CancellationToken ct)
    {
        var language = (req.Language ?? "").Trim().ToLowerInvariant();
        if (language is not ("en" or "ar"))
            throw new QuizPublishValidationException("Language must be 'en' or 'ar'.");

        var title = (req.Title ?? "").Trim();
        if (title.Length is < 3 or > 150)
            throw new QuizPublishValidationException("Title must contain 3-150 characters.");

        var nowUtc = DateTime.UtcNow;
        if (req.ClosesAtUtc is { } closes && closes <= nowUtc)
            throw new QuizPublishValidationException(
                "The exam closing date must be in the future.");
        if (req.OpensAtUtc is { } opens && req.ClosesAtUtc is { } close2 && opens >= close2)
            throw new QuizPublishValidationException(
                "The open date must come before the close date.");
        if (req.DurationMinutes is < 1 or > 600)
            throw new QuizPublishValidationException(
                "Duration, when provided, must be between 1 and 600 minutes.");

        // Server-side recompute: course, ownership, material
        var course = await _db.Courses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CourseId, ct)
            ?? throw new QuizPublishValidationException("Course not found.");

        if (!await _access.IsAssignedToCourseAsync(doctorUserId, req.CourseId))
            throw new QuizPublishValidationException(
                "You are not assigned to teach this course.");

        var materialTitle = "Course material";
        if (req.MaterialId.HasValue)
        {
            var material = await _db.CourseMaterials.AsNoTracking()
                .Where(m => m.Id == req.MaterialId.Value && m.CourseId == req.CourseId)
                .Select(m => m.Title)
                .FirstOrDefaultAsync(ct)
                ?? throw new QuizPublishValidationException(
                    "The selected material does not belong to this course.");
            materialTitle = material;
        }

        var windowStart = DateTime.UtcNow.AddMinutes(-DuplicateWindowMinutes);
        var recentlyPublished = await _db.Exams.AsNoTracking().AnyAsync(e =>
            e.DoctorUserId == doctorUserId
            && e.CourseId == req.CourseId
            && e.Title == title
            && e.CreatedAtUtc >= windowStart, ct);
        if (recentlyPublished)
                throw new DuplicateQuizException(
                    "This quiz was already published moments ago. Generate a new quiz to publish again.");

        // Map generated questions into the shared exam shape
        var examQuestions = new List<ExamQuestionDto>();
        for (var i = 0; i < req.Questions.Count; i++)
        {
            var q = req.Questions[i];
            var options = q.Options.Select(o => (o ?? "").Trim()).ToList();
            if (options.Count is < 2 or > 6 || options.Any(string.IsNullOrWhiteSpace))
                throw new QuizPublishValidationException(
                    $"Question {i + 1} must have between 2 and 6 non-empty options.");
            if (q.CorrectIndex < 0 || q.CorrectIndex >= options.Count)
                throw new QuizPublishValidationException(
                    $"Question {i + 1} CorrectIndex is out of range.");

            examQuestions.Add(new ExamQuestionDto(
                Id: 0,
                ExamId: 0,
                QuestionText: (q.Question ?? "").Trim(),
                QuestionType: "MCQ",
                // The existing exam infra stores the correct option TEXT.
                CorrectAnswer: options[q.CorrectIndex],
                Explanation: string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation.Trim(),
                SourceMaterial: string.IsNullOrWhiteSpace(q.Reference)
                    ? materialTitle
                    : q.Reference.Trim(),
                OptionsJson: JsonSerializer.Serialize(options),
                OrderIndex: i,
                Marks: null));
        }

        var materialsUsedJson = JsonSerializer.Serialize(new[] { materialTitle });

        var updateReq = new UpdateExamRequest(
            CourseId: req.CourseId,
            Title: title,
            QuestionCount: examQuestions.Count,
            QuestionTypes: "MCQ",
            Difficulty: "mixed",
            Language: language,
            MaterialsUsed: materialsUsedJson,
            Questions: examQuestions);

        // Draft + publish in one ambient transaction (no orphans)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var draft = await _exams.CreateDraftExamAsync(doctorUserId, updateReq, ct) ??
            throw new QuizPublishValidationException("Quiz could not be staged as a draft exam.");

        var draftExam = await _db.Exams.FindAsync(new object?[] { draft.Id }, ct)
            ?? throw new QuizPublishValidationException("Staged draft exam vanished before publication.");
        draftExam.OpensAtUtc = AsUtc(req.OpensAtUtc);
        draftExam.ClosesAtUtc = AsUtc(req.ClosesAtUtc);
        draftExam.DurationMinutes = req.DurationMinutes;
        draftExam.ShowAnswersAfterSubmit = req.ShowAnswersAfterSubmit;

        var (published, notified) = await _exams.PublishExamWithNotifiedCountAsync(
            doctorUserId,
            new ExamAssistantPublishRequest(draft.Id, title, req.Marks),
            ct);

        await tx.CommitAsync(ct);

        return new QuizPublishResponse(
            published.Id, published.Title, published.QuestionCount,
            notified, published.Status ?? "Published");
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
