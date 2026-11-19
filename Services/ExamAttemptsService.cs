using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>Why an attempt operation was refused. NotFound is used for
/// foreign/hidden resources so ids stay indistinguishable; the other
/// failures map to HTTP 409 order-of-flow conflicts.</summary>
public enum ExamAttemptFailure
{
    NotFound,
    NotOpenYet,
    Closed,
    AlreadySubmitted,
    NotStarted,
}

/// <summary>Outcome for one attempt operation: <see cref="Value"/> when
/// <see cref="Failure"/> is null, otherwise the refusal reason + message.</summary>
public sealed record ExamAttemptResult<T>(T? Value, ExamAttemptFailure? Failure = null, string? Error = null)
{
    public bool Ok => Failure is null;

    public static ExamAttemptResult<T> Good(T value) => new(value, null, null);
    public static ExamAttemptResult<T> Fail(ExamAttemptFailure failure, string? error = null) =>
        new(default, failure, error);
}

/// <summary>Exam + display info of one course-scoped, student-visible exam.</summary>
internal sealed record VisibleExamView(Exam Exam, string CourseCode, string CourseTitle);

/// <summary>
/// Persistence for published-exam student attempts: start/resume attempts
/// (one per student per exam, unique-indexed), server-side grading at
/// submit, own-attempt listings and doctor per-exam results.
///
/// Authorization is recomputed here for every call: a student may only take
/// exams that are published, for a currently-enrolled (ungraded) course at
/// their university; a doctor may only read results for their OWN exams
/// (foreign exams read as 404). Grading ignores everything the client sends
/// except the raw option choice — the correct option index is derived
/// server-side from the stored question data, so a score can never be
/// injected from a payload.
/// </summary>
public interface IExamAttemptsService
{
    Task<ExamAttemptResult<StudentExamAttemptStateDto>> GetAttemptStateAsync(
        int studentUserId, int examId, CancellationToken ct);

    Task<ExamAttemptResult<StudentExamAttemptStateDto>> StartAttemptAsync(
        int studentUserId, int examId, CancellationToken ct);

    Task<ExamAttemptResult<StudentExamAttemptStateDto>> SubmitAttemptAsync(
        int studentUserId, int examId, SubmitExamAttemptRequest req, CancellationToken ct);

    Task<IReadOnlyList<StudentAttemptListRowDto>> ListMyAttemptsAsync(
        int studentUserId, CancellationToken ct);

    Task<ExamAttemptResult<DoctorExamResultsDto>> GetDoctorResultsAsync(
        int doctorUserId, int examId, CancellationToken ct);
}

public class ExamAttemptsService : IExamAttemptsService
{
    private readonly AppDbContext _db;

    public ExamAttemptsService(AppDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    /// <summary>Read-only attempt state for the student pages: window
    /// metadata, current attempt (null = not started), take-able questions
    /// while active, review after submit. Never starts an attempt.</summary>
    public async Task<ExamAttemptResult<StudentExamAttemptStateDto>> GetAttemptStateAsync(
        int studentUserId, int examId, CancellationToken ct)
    {
        var view = await LoadVisiblePublishedExamAsync(studentUserId, examId, ct);
        if (view is null)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(ExamAttemptFailure.NotFound);

        var attempt = await LoadAttemptAsync(examId, studentUserId, ct);
        return ExamAttemptResult<StudentExamAttemptStateDto>.Good(
            BuildState(view, attempt));
    }

    // ------------------------------------------------------------------
    // Start / resume (one attempt per student per exam)
    // ------------------------------------------------------------------

    public async Task<ExamAttemptResult<StudentExamAttemptStateDto>> StartAttemptAsync(
        int studentUserId, int examId, CancellationToken ct)
    {
        var view = await LoadVisiblePublishedExamAsync(studentUserId, examId, ct);
        if (view is null)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(ExamAttemptFailure.NotFound);

        var now = DateTime.UtcNow;
        if (view.Exam.OpensAtUtc is { } opens && now < opens)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(
                ExamAttemptFailure.NotOpenYet,
                $"This exam opens at {opens:u} (UTC) — it is not open yet.");

        var attempt = await LoadAttemptAsync(examId, studentUserId, ct);
        if (attempt is not null && attempt.SubmittedAtUtc is not null)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(
                ExamAttemptFailure.AlreadySubmitted,
                "You already submitted this exam. A second attempt is not allowed.");

        if (attempt is null)
        {
            attempt = new ExamAttempt
            {
                ExamId = examId,
                StudentUserId = studentUserId,
                StartedAtUtc = now,
            };
            _db.ExamAttempts.Add(attempt);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent double-start: the unique (ExamId, StudentUserId)
                // index fired, so just work with the row that won.
                attempt = await LoadAttemptAsync(examId, studentUserId, ct)
                    ?? throw new InvalidOperationException(
                        "Attempt row vanished during concurrent start.");
            }
        }

        return ExamAttemptResult<StudentExamAttemptStateDto>.Good(
            BuildState(view, attempt));
    }

    // ------------------------------------------------------------------
    // Submit + server-side grading
    // ------------------------------------------------------------------

    public async Task<ExamAttemptResult<StudentExamAttemptStateDto>> SubmitAttemptAsync(
        int studentUserId, int examId, SubmitExamAttemptRequest req, CancellationToken ct)
    {
        var view = await LoadVisiblePublishedExamAsync(studentUserId, examId, ct);
        if (view is null)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(ExamAttemptFailure.NotFound);

        var attempt = await LoadAttemptAsync(examId, studentUserId, ct);
        if (attempt is null)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(
                ExamAttemptFailure.NotStarted, "Start the exam before submitting.");

        if (attempt.SubmittedAtUtc is not null)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(
                ExamAttemptFailure.AlreadySubmitted,
                "This attempt was already submitted and graded.");

        // The close date is the hard server-side deadline; the client clock
        // is never consulted.
        var now = DateTime.UtcNow;
        if (view.Exam.ClosesAtUtc is { } closes && now > closes)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(
                ExamAttemptFailure.Closed,
                $"The exam closed at {closes:u} (UTC) — the submission window is over.");

        // Collapse duplicate answers for the same question. The payload only
        // nominates a choice ({questionId, option index}); out-of-range or
        // null indexes count as unanswered. Nothing here can mark itself
        // correct — correctness is decided by ComputeCorrectIndex below.
        var provided = (req?.Answers ?? Array.Empty<SubmitExamAnswerDto>())
            .Where(a => a is not null)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => NormalizeSelection(g.First().SelectedOption));

        var score = 0;
        foreach (var question in view.Exam.Questions.OrderBy(q => q.OrderIndex))
        {
            var selected = provided.TryGetValue(question.Id, out var sel) ? sel : null;
            var correctIndex = ComputeCorrectIndex(question);
            var isCorrect = correctIndex >= 0 && selected is not null && selected.Value == correctIndex;
            if (isCorrect) score++;

            attempt.Answers.Add(new ExamAnswer
            {
                QuestionId = question.Id,
                SelectedOption = selected,
                IsCorrect = isCorrect,
            });
        }

        attempt.SubmittedAtUtc = now;
        attempt.Score = score;
        attempt.MaxScore = view.Exam.QuestionCount;
        await _db.SaveChangesAsync(ct);

        return ExamAttemptResult<StudentExamAttemptStateDto>.Good(
            BuildState(view, attempt));
    }

    // ------------------------------------------------------------------
    // Student list (own attempts only)
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<StudentAttemptListRowDto>> ListMyAttemptsAsync(
        int studentUserId, CancellationToken ct)
    {
        return await _db.ExamAttempts.AsNoTracking()
            .Where(a => a.StudentUserId == studentUserId)
            .OrderByDescending(a => a.StartedAtUtc)
            .Select(a => new StudentAttemptListRowDto(
                a.Id, a.ExamId, a.Exam!.Title,
                a.Exam.Course!.Code, a.Exam.Course!.Title,
                a.StartedAtUtc, a.SubmittedAtUtc, a.Score, a.MaxScore))
            .ToListAsync(ct);
    }

    // ------------------------------------------------------------------
    // Doctor results (own exams only)
    // ------------------------------------------------------------------

    public async Task<ExamAttemptResult<DoctorExamResultsDto>> GetDoctorResultsAsync(
        int doctorUserId, int examId, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == examId, ct);
        // Not owned → identical to missing: a foreign doctor cannot even
        // confirm the exam exists.
        if (exam is null || exam.DoctorUserId != doctorUserId)
            return ExamAttemptResult<DoctorExamResultsDto>.Fail(ExamAttemptFailure.NotFound);

        var takers = await _db.ExamAttempts.AsNoTracking()
            .Where(a => a.ExamId == examId && a.SubmittedAtUtc != null)
            .Join(_db.Users.AsNoTracking(),
                a => a.StudentUserId, u => u.Id,
                (a, u) => new { a, u })
            .OrderBy(x => x.u.FullName)
            .Select(x => new DoctorExamResultTakerDto(
                x.a.StudentUserId, x.u.FullName, (string?)x.u.Email,
                x.a.StartedAtUtc, x.a.SubmittedAtUtc!.Value,
                x.a.Score ?? 0, x.a.MaxScore ?? exam.QuestionCount,
                Percent(x.a.Score ?? 0, x.a.MaxScore ?? exam.QuestionCount)))
            .ToListAsync(ct);

        var eligibleIds = await GetEligibleStudentIdsAsync(exam.CourseId, ct);
        var takerIds = takers.ConvertAll(t => t.StudentUserId).ToHashSet();
        var nonTakerIds = eligibleIds.Where(id => !takerIds.Contains(id)).ToList();

        var nonTakers = nonTakerIds.Count == 0
            ? new List<DoctorExamNonTakerDto>()
            : await _db.Users.AsNoTracking()
                .Where(u => nonTakerIds.Contains(u.Id))
                .OrderBy(u => u.FullName)
                .Select(u => new DoctorExamNonTakerDto(u.Id, u.FullName, (string?)u.Email))
                .ToListAsync(ct);

        return ExamAttemptResult<DoctorExamResultsDto>.Good(new DoctorExamResultsDto(
            exam.Id, exam.Title, exam.Course!.Code, exam.Course!.Title,
            exam.QuestionCount, exam.Marks, exam.Status,
            exam.OpensAtUtc, exam.ClosesAtUtc, exam.DurationMinutes,
            eligibleIds.Count, takers, nonTakers));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Loads the exam with its questions when the student is allowed
    /// to take it: published, enrolled in the course this semester without a
    /// grade, and within the same university. Null = not visible (404).</summary>
    private async Task<VisibleExamView?> LoadVisiblePublishedExamAsync(
        int studentUserId, int examId, CancellationToken ct)
    {
        var student = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studentUserId, ct);
        if (student is null || student.Role != UserRole.Student)
            return null;

        // Eligible enrollments: current semester. Current grades are in-progress
        // marks, so a graded current enrollment still gets the quiz.
        var eligibleCourseIds = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == studentUserId
                        && e.Semester == "Current")
            .Select(e => e.CourseId)
            .ToListAsync(ct);
        if (eligibleCourseIds.Count == 0)
            return null;

        var found = await _db.Exams.AsNoTracking()
            .Where(e => e.Id == examId
                        && e.Status == "Published"
                        && eligibleCourseIds.Contains(e.CourseId))
            .Select(e => new
            {
                Exam = e,
                CourseCode = e.Course!.Code,
                CourseTitle = e.Course!.Title,
                CourseUniversityId = (int?)e.Course!.UniversityId,
            })
            .FirstOrDefaultAsync(ct);
        if (found is null)
            return null;

        // University isolation: same rule as student exam visibility.
        if (found.CourseUniversityId is not null
            && student.UniversityId is not null
            && found.CourseUniversityId != student.UniversityId)
            return null;

        // CorrectAnswer/Explanation stay only on the entity we use for
        // grading; they are never projected into a student DTO.
        var exam = found.Exam;
        exam.Questions = await _db.ExamQuestions.AsNoTracking()
            .Where(q => q.ExamId == exam.Id)
            .OrderBy(q => q.OrderIndex)
            .ToListAsync(ct);

        return new VisibleExamView(exam, found.CourseCode, found.CourseTitle);
    }

    private Task<ExamAttempt?> LoadAttemptAsync(int examId, int studentUserId, CancellationToken ct) =>
        _db.ExamAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.ExamId == examId && a.StudentUserId == studentUserId, ct);

    private static StudentExamAttemptStateDto BuildState(
        VisibleExamView view, ExamAttempt? attempt)
    {
        var exam = view.Exam;
        var active = attempt is not null && attempt.SubmittedAtUtc is null;
        var submitted = attempt?.SubmittedAtUtc is not null;

        StudentExamAttemptDto? attemptDto = null;
        if (attempt is not null)
        {
            attemptDto = new StudentExamAttemptDto(
                attempt.Id, attempt.ExamId, attempt.StartedAtUtc,
                attempt.SubmittedAtUtc, attempt.Score, attempt.MaxScore);
        }

        // Take-able questions exist ONLY while the attempt is active, and
        // the shape never carries CorrectAnswer or Explanation.
        IReadOnlyList<AttemptQuestionDto>? takeQuestions = active
            ? exam.Questions
                .Select(q => new AttemptQuestionDto(
                    q.Id, q.ExamId, q.OrderIndex, q.QuestionText,
                    q.QuestionType, q.OptionsJson, q.Marks))
                .ToList()
            : null;

        IReadOnlyList<AttemptAnswerReviewDto>? review = null;
        if (submitted && attempt is not null)
        {
            // Correctness fields stay null unless the doctor opted into
            // showing the answers after submission (server-checked flag).
            var byQuestion = attempt.Answers.ToDictionary(a => a.QuestionId);
            var reveal = exam.ShowAnswersAfterSubmit;
            review = exam.Questions
                .Select(q =>
                {
                    var answer = byQuestion.TryGetValue(q.Id, out var a) ? a : null;
                    return new AttemptAnswerReviewDto(
                        q.Id, q.OrderIndex, q.QuestionText, q.OptionsJson,
                        answer?.SelectedOption,
                        reveal ? answer?.IsCorrect : null,
                        reveal ? ComputeCorrectIndex(q) : (int?)null);
                })
                .ToList();
        }

        return new StudentExamAttemptStateDto(
            exam.Id, exam.Title, view.CourseCode, view.CourseTitle,
            exam.QuestionCount, exam.Marks, exam.Status,
            exam.OpensAtUtc, exam.ClosesAtUtc, exam.DurationMinutes,
            exam.ShowAnswersAfterSubmit,
            attemptDto, takeQuestions, review);
    }

    /// <summary>Null/foreign/out-of-range choices are unanswered — the client
    /// cannot push an option index outside the stored option list.</summary>
    private static int? NormalizeSelection(int? selected) =>
        selected is >= 0 ? selected : null;

    /// <summary>The correct option index, derived SERVER-side by matching the
    /// stored option text list with the exam's correct answer text.</summary>
    private static int ComputeCorrectIndex(ExamQuestion question)
    {
        if (string.IsNullOrWhiteSpace(question.OptionsJson) ||
            string.IsNullOrWhiteSpace(question.CorrectAnswer))
            return -1;
        try
        {
            var options = JsonSerializer.Deserialize<List<string>>(question.OptionsJson);
            if (options is null || options.Count == 0)
                return -1;
            var wanted = question.CorrectAnswer.Trim();
            var index = options.FindIndex(o => o is not null && o.Trim() == wanted);
            if (index < 0)
                index = options.FindIndex(o =>
                    o is not null &&
                    string.Equals(o.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            return index;
        }
        catch
        {
            return -1;
        }
    }

    private static double? Percent(int score, int maxScore) =>
        maxScore <= 0 ? null : Math.Round(score * 100.0 / maxScore, 1);

    /// <summary>Eligible takers for a course (mirrors ExamAssistantService):
    /// current semester, no grade yet, student role, same university.</summary>
    private async Task<List<int>> GetEligibleStudentIdsAsync(int courseId, CancellationToken ct)
    {
        var course = await _db.Courses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return new List<int>();

        var enrolledUserIds = await _db.Enrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId
                        && e.Semester == "Current")
            .Select(e => e.UserId)
            .ToListAsync(ct);
        if (enrolledUserIds.Count == 0) return new List<int>();

        var users = await _db.Users.AsNoTracking()
            .Where(u => enrolledUserIds.Contains(u.Id))
            .ToListAsync(ct);

        return users
            .Where(u => u.Role == UserRole.Student)
            .Where(u => course.UniversityId is null
                        || u.UniversityId is null
                        || course.UniversityId == u.UniversityId)
            .Select(u => u.Id)
            .ToList();
    }
}
