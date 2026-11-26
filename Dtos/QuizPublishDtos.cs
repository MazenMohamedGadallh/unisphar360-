using System;
using System.Collections.Generic;

namespace UniSphere.Api.Dtos;

// Quiz publish (/api/doctor/ai/quiz/publish) - NEW feature, new file

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

public record QuizPublishResponse(
    int ExamId,
    string Title,
    int QuestionCount,
    int NotifiedStudents,
    string Status);

public record StudentExamDetailDto(
    int Id, int CourseId, string CourseCode, string CourseTitle,
    string Title, int QuestionCount, int? Marks,
    DateTime CreatedAtUtc, DateTime? PublishedAtUtc,
    IReadOnlyList<StudentExamQuestionDto> Questions);
