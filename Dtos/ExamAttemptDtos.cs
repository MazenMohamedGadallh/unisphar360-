namespace UniSphere.Api.Dtos;

// ---- Published-exam attempts (student take + grading + doctor results) ----
// NEW feature, new file. Student-facing shapes never carry CorrectAnswer or
// Explanation; the correct option is resolved SERVER-side at submit time.

/// <summary>One answer the client submits. SelectedOption is a 0-based index
/// into the question's OptionsJson array; null or out of range counts as
/// unanswered. Correctness is computed by the server at submit — this value
/// is only ever a choice, never a grade.</summary>
public record SubmitExamAnswerDto(int QuestionId, int? SelectedOption);

/// <summary>Submit payload: the answer set for the attempt. Questions without
/// an entry remain unanswered (null selection, not correct).</summary>
public record SubmitExamAttemptRequest(IReadOnlyList<SubmitExamAnswerDto> Answers);

/// <summary>One question during an ACTIVE attempt: options only. The chosen
/// index is compared server-side; the correct index/answer text is never in
/// this shape.</summary>
public record AttemptQuestionDto(
    int QuestionId, int ExamId, int OrderIndex, string QuestionText,
    string QuestionType, string? OptionsJson, int? Marks);

/// <summary>One graded answer as the student sees it after submitting.
/// IsCorrect / CorrectOption are populated ONLY when the exam's
/// ShowAnswersAfterSubmit flag allows it (checked server-side); otherwise
/// both stay null so correctness is never revealed. Explanations are never
/// returned to students in any state.</summary>
public record AttemptAnswerReviewDto(
    int QuestionId, int OrderIndex, string QuestionText,
    string? OptionsJson,
    int? SelectedOption, bool? IsCorrect, int? CorrectOption);

/// <summary>The student's own attempt summary (list rows + detail header).</summary>
public record StudentExamAttemptDto(
    int AttemptId, int ExamId, DateTime StartedAtUtc, DateTime? SubmittedAtUtc,
    int? Score, int? MaxScore);

/// <summary>One of the student's own attempts, for the My Exams list chips.</summary>
public record StudentAttemptListRowDto(
    int AttemptId, int ExamId, string ExamTitle,
    string CourseCode, string CourseTitle,
    DateTime StartedAtUtc, DateTime? SubmittedAtUtc, int? Score, int? MaxScore);

/// <summary>Full state of one exam attempt for the student pages: window
/// metadata, the attempt (null = not started yet), the take-able questions
/// (only while the attempt is active) and the post-submit review (only once
/// submitted, with correctness gated by ShowAnswersAfterSubmit).</summary>
public record StudentExamAttemptStateDto(
    int ExamId, string Title, string CourseCode, string CourseTitle,
    int QuestionCount, int? Marks, string Status,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc, int? DurationMinutes,
    bool ShowAnswersAfterSubmit,
    StudentExamAttemptDto? Attempt,
    IReadOnlyList<AttemptQuestionDto>? TakeQuestions,
    IReadOnlyList<AttemptAnswerReviewDto>? Review);

// ---- Doctor results ----

/// <summary>One submitted attempt on the doctor's results page: score/max,
/// percentage and submitted-at, plus the student's name.</summary>
public record DoctorExamResultTakerDto(
    int StudentUserId, string FullName, string? Email,
    DateTime StartedAtUtc, DateTime SubmittedAtUtc,
    int Score, int MaxScore, double? Percent);

/// <summary>An enrolled, eligible student who has NOT submitted this exam.</summary>
public record DoctorExamNonTakerDto(
    int StudentUserId, string FullName, string? Email);

/// <summary>Doctor-facing results view of one of their own exams: takers
/// with server-graded scores plus enrolled non-takers. Owned-exam scope is
/// enforced server-side (foreign exams read as 404).</summary>
public record DoctorExamResultsDto(
    int ExamId, string ExamTitle, string CourseCode, string CourseTitle,
    int QuestionCount, int? Marks, string Status,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc, int? DurationMinutes,
    int EligibleCount,
    IReadOnlyList<DoctorExamResultTakerDto> Takers,
    IReadOnlyList<DoctorExamNonTakerDto> NonTakers);
