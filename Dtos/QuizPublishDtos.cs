using System;
using System.Collections.Generic;

namespace UniSphere.Api.Dtos;

// ---- Quiz publish (/api/doctor/ai/quiz/publish) — NEW feature, new file ----

/// <summary>Publish a doctor's generated quiz draft to eligible students.
/// Questions reuse the generation shape (<see cref="GeneratedQuestionDto"/>):
/// <c>CorrectIndex</c> must point inside <c>Options</c>; the server resolves
/// the correct answer text itself and never trusts client-supplied course or
/// material titles. The trailing window fields are optional (existing
/// callers stay valid): OpensAtUtc gates attempts, ClosesAtUtc is the
/// server-side submit deadline, DurationMinutes is the advertised soft time
/// budget, ShowAnswersAfterSubmit reveals post-submit correctness when set.</summary>
public record QuizPublishRequest(
    int CourseId,
    int? MaterialId,
    string Title,
    string Language,
    int? Marks,
    IReadOnlyList<GeneratedQuestionDto> Questions,
    DateTime? OpensAtUtc = null,
    DateTime? ClosesAtUtc = null,
    int? DurationMinutes = null,
    bool ShowAnswersAfterSubmit = false);

/// <summary>Result of publishing a generated quiz (the persisted exam).</summary>
public record QuizPublishResponse(
    int ExamId,
    string Title,
    int QuestionCount,
    int NotifiedStudents,
    string Status);

/// <summary>One published exam as the student sees it: the same fields as
/// <see cref="StudentExamDto"/> plus its questions in the safe
/// <see cref="StudentExamQuestionDto"/> shape (no correct answers, no
/// explanations — these columns are never selected server-side).</summary>
public record StudentExamDetailDto(
    int Id, int CourseId, string CourseCode, string CourseTitle,
    string Title, int QuestionCount, int? Marks,
    DateTime CreatedAtUtc, DateTime? PublishedAtUtc,
    IReadOnlyList<StudentExamQuestionDto> Questions);
