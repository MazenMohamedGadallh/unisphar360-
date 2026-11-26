using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public enum ExamAttemptFailure
{
    NotFound,
    NotOpenYet,
    Closed,
    AlreadySubmitted,
    NotStarted,
}

public sealed record ExamAttemptResult<T>(T? Value, ExamAttemptFailure? Failure = null, string? Error = null)
{
    public bool Ok => Failure is null;

    public static ExamAttemptResult<T> Good(T value) => new(value, null, null);
    public static ExamAttemptResult<T> Fail(ExamAttemptFailure failure, string? error = null) =>
        new(default, failure, error);
}

internal sealed record VisibleExamView(Exam Exam, string CourseCode, string CourseTitle);

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

    Task<ExamAttemptResult<IReadOnlyList<DoctorWrittenAnswerDto>>> ListWrittenAnswersAsync(
        int doctorUserId, int examId, CancellationToken ct);

    Task<ExamAttemptResult<DoctorWrittenAnswerDto>> GradeWrittenAnswerAsync(
        int doctorUserId, int examId, int answerId, bool isCorrect, CancellationToken ct);
}

public class ExamAttemptsService : IExamAttemptsService
{
    private const int MaxTextAnswerLength = 5000;
    private readonly AppDbContext _db;

    /// Questions the student answers in writing and the doctor grades by hand.
    public static bool IsWritten(string? questionType) =>
        questionType is not null &&
        (questionType.Equals("Essay", StringComparison.OrdinalIgnoreCase) ||
         questionType.Equals("Definition", StringComparison.OrdinalIgnoreCase) ||
         questionType.Equals("ShortAnswer", StringComparison.OrdinalIgnoreCase));

    public ExamAttemptsService(AppDbContext db)
    {
        _db = db;
    }

    // State

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

    // Start / resume (one attempt per student per exam)

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
                attempt = await LoadAttemptAsync(examId, studentUserId, ct)
                    ?? throw new InvalidOperationException(
                        "Attempt row vanished during concurrent start.");
            }
        }

        return ExamAttemptResult<StudentExamAttemptStateDto>.Good(
            BuildState(view, attempt));
    }

    // Submit + server-side grading

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

        var now = DateTime.UtcNow;
        if (view.Exam.ClosesAtUtc is { } closes && now > closes)
            return ExamAttemptResult<StudentExamAttemptStateDto>.Fail(
                ExamAttemptFailure.Closed,
                $"The exam closed at {closes:u} (UTC) — the submission window is over.");

        var provided = (req?.Answers ?? Array.Empty<SubmitExamAnswerDto>())
            .Where(a => a is not null)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.First());

        var score = 0;
        foreach (var question in view.Exam.Questions.OrderBy(q => q.OrderIndex))
        {
            var given = provided.TryGetValue(question.Id, out var dto) ? dto : null;
            if (IsWritten(question.QuestionType))
            {
                var text = given?.TextAnswer?.Trim();
                if (text is { Length: > MaxTextAnswerLength }) text = text[..MaxTextAnswerLength];
                attempt.Answers.Add(new ExamAnswer
                {
                    QuestionId = question.Id,
                    TextAnswer = string.IsNullOrEmpty(text) ? null : text,
                    IsCorrect = false,
                    // A blank written answer needs no grading: it is simply wrong.
                    GradedAtUtc = string.IsNullOrEmpty(text) ? now : null,
                });
                continue;
            }

            var selected = NormalizeSelection(given?.SelectedOption);
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

    // Student list (own attempts only)

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

    // Doctor results (own exams only)

    public async Task<ExamAttemptResult<DoctorExamResultsDto>> GetDoctorResultsAsync(
        int doctorUserId, int examId, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == examId, ct);
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
                Percent(x.a.Score ?? 0, x.a.MaxScore ?? exam.QuestionCount),
                x.a.Answers.Count(an => an.TextAnswer != null && an.GradedAtUtc == null)))
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

    // Doctor grading of written answers (own exams only)

    public async Task<ExamAttemptResult<IReadOnlyList<DoctorWrittenAnswerDto>>> ListWrittenAnswersAsync(
        int doctorUserId, int examId, CancellationToken ct)
    {
        if (!await OwnsExamAsync(doctorUserId, examId, ct))
            return ExamAttemptResult<IReadOnlyList<DoctorWrittenAnswerDto>>.Fail(ExamAttemptFailure.NotFound);

        var rows = await WrittenAnswersQuery(examId, null).ToListAsync(ct);
        return ExamAttemptResult<IReadOnlyList<DoctorWrittenAnswerDto>>.Good(rows);
    }

    public async Task<ExamAttemptResult<DoctorWrittenAnswerDto>> GradeWrittenAnswerAsync(
        int doctorUserId, int examId, int answerId, bool isCorrect, CancellationToken ct)
    {
        if (!await OwnsExamAsync(doctorUserId, examId, ct))
            return ExamAttemptResult<DoctorWrittenAnswerDto>.Fail(ExamAttemptFailure.NotFound);

        var answer = await _db.ExamAnswers
            .Include(a => a.Attempt).ThenInclude(a => a!.Answers)
            .Include(a => a.Question)
            .FirstOrDefaultAsync(a => a.Id == answerId && a.Attempt!.ExamId == examId, ct);
        if (answer is null || answer.TextAnswer is null || !IsWritten(answer.Question?.QuestionType))
            return ExamAttemptResult<DoctorWrittenAnswerDto>.Fail(ExamAttemptFailure.NotFound);

        answer.IsCorrect = isCorrect;
        answer.GradedAtUtc = DateTime.UtcNow;
        answer.Attempt!.Score = answer.Attempt.Answers.Count(a => a.IsCorrect);
        await _db.SaveChangesAsync(ct);

        var row = await WrittenAnswersQuery(examId, answerId).FirstAsync(ct);
        return ExamAttemptResult<DoctorWrittenAnswerDto>.Good(row);
    }

    private Task<bool> OwnsExamAsync(int doctorUserId, int examId, CancellationToken ct) =>
        _db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.DoctorUserId == doctorUserId, ct);

    // Filter and sort on entities: EF cannot translate OrderBy/Where on the projected record.
    private IQueryable<DoctorWrittenAnswerDto> WrittenAnswersQuery(int examId, int? answerId) =>
        _db.ExamAnswers.AsNoTracking()
            .Where(a => a.Attempt!.ExamId == examId
                        && a.Attempt.SubmittedAtUtc != null
                        && a.TextAnswer != null
                        && (answerId == null || a.Id == answerId))
            .OrderBy(a => a.Attempt!.StudentUser!.FullName).ThenBy(a => a.Question!.OrderIndex)
            .Select(a => new DoctorWrittenAnswerDto(
                a.Id, a.Attempt!.StudentUserId, a.Attempt.StudentUser!.FullName,
                a.QuestionId, a.Question!.OrderIndex, a.Question.QuestionType, a.Question.QuestionText,
                a.Question.CorrectAnswer, a.TextAnswer, a.IsCorrect, a.GradedAtUtc != null));

    // Helpers

    private async Task<VisibleExamView?> LoadVisiblePublishedExamAsync(
        int studentUserId, int examId, CancellationToken ct)
    {
        var student = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studentUserId, ct);
        if (student is null || student.Role != UserRole.Student)
            return null;

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
            var byQuestion = attempt.Answers.ToDictionary(a => a.QuestionId);
            var reveal = exam.ShowAnswersAfterSubmit;
            review = exam.Questions
                .Select(q =>
                {
                    var answer = byQuestion.TryGetValue(q.Id, out var a) ? a : null;
                    var pending = answer is { TextAnswer: not null, GradedAtUtc: null };
                    return new AttemptAnswerReviewDto(
                        q.Id, q.OrderIndex, q.QuestionText, q.OptionsJson,
                        answer?.SelectedOption,
                        reveal && !pending ? answer?.IsCorrect : null,
                        reveal ? ComputeCorrectIndex(q) : (int?)null,
                        answer?.TextAnswer, pending);
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

    private static int? NormalizeSelection(int? selected) =>
        selected is >= 0 ? selected : null;

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
