namespace UniSphere.Api.Dtos;

public record SubmitExamAnswerDto(int QuestionId, int? SelectedOption, string? TextAnswer = null);

public record SubmitExamAttemptRequest(IReadOnlyList<SubmitExamAnswerDto> Answers);

public record AttemptQuestionDto(
    int QuestionId, int ExamId, int OrderIndex, string QuestionText,
    string QuestionType, string? OptionsJson, int? Marks);

public record AttemptAnswerReviewDto(
    int QuestionId, int OrderIndex, string QuestionText,
    string? OptionsJson,
    int? SelectedOption, bool? IsCorrect, int? CorrectOption,
    string? TextAnswer = null, bool PendingGrading = false);

public record StudentExamAttemptDto(
    int AttemptId, int ExamId, DateTime StartedAtUtc, DateTime? SubmittedAtUtc,
    int? Score, int? MaxScore);

public record StudentAttemptListRowDto(
    int AttemptId, int ExamId, string ExamTitle,
    string CourseCode, string CourseTitle,
    DateTime StartedAtUtc, DateTime? SubmittedAtUtc, int? Score, int? MaxScore);

public record StudentExamAttemptStateDto(
    int ExamId, string Title, string CourseCode, string CourseTitle,
    int QuestionCount, int? Marks, string Status,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc, int? DurationMinutes,
    bool ShowAnswersAfterSubmit,
    StudentExamAttemptDto? Attempt,
    IReadOnlyList<AttemptQuestionDto>? TakeQuestions,
    IReadOnlyList<AttemptAnswerReviewDto>? Review);

// Doctor results

public record DoctorExamResultTakerDto(
    int StudentUserId, string FullName, string? Email,
    DateTime StartedAtUtc, DateTime SubmittedAtUtc,
    int Score, int MaxScore, double? Percent, int PendingGrading = 0);

public record DoctorWrittenAnswerDto(
    int AnswerId, int StudentUserId, string StudentName,
    int QuestionId, int OrderIndex, string QuestionType, string QuestionText,
    string? ModelAnswer, string? TextAnswer, bool IsCorrect, bool Graded);

public record GradeWrittenAnswerRequest(bool IsCorrect);

public record DoctorExamNonTakerDto(
    int StudentUserId, string FullName, string? Email);

public record DoctorExamResultsDto(
    int ExamId, string ExamTitle, string CourseCode, string CourseTitle,
    int QuestionCount, int? Marks, string Status,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc, int? DurationMinutes,
    int EligibleCount,
    IReadOnlyList<DoctorExamResultTakerDto> Takers,
    IReadOnlyList<DoctorExamNonTakerDto> NonTakers);
