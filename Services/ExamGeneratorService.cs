using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Exam generation for doctors. Builds a prompt from a course's own materials,
/// asks the configured AI provider for JSON-formatted questions, validates and
/// repairs the output, and returns a draft question set ready for saving.
///
/// Every step is scoped to the doctor's own assigned courses: materials are
/// only read for courses the doctor teaches, and unknown/invalid output is
/// rejected loudly rather than published as garbage.
/// </summary>
public interface IExamGeneratorService
{
    /// <summary>List the courses this doctor is assigned to (for the dropdown).</summary>
    Task<IReadOnlyList<ExamCatalogCourseDto>> GetAssignedCoursesAsync(int doctorUserId, CancellationToken ct = default);

    /// <summary>Generate exam questions from a course's materials. Returns
    /// drafted questions (not saved) plus course info.</summary>
    Task<ExamGenerationResponse> GenerateAsync(
        int doctorUserId, ExamGenerationRequest req, CancellationToken ct);

    /// <summary>Save a generated exam with its questions.</summary>
    Task<ExamDto> SaveExamAsync(
        int doctorUserId, UpdateExamRequest req, CancellationToken ct);

    /// <summary>List the caller's saved exams.</summary>
    Task<IReadOnlyList<ExamListDto>> ListExamsAsync(int doctorUserId, CancellationToken ct);

    /// <summary>Get one saved exam with its questions.</summary>
    Task<ExamDto?> GetExamAsync(int doctorUserId, int examId, CancellationToken ct);

    /// <summary>Update an existing exam (title, question edits).</summary>
    Task<ExamDto?> UpdateExamAsync(
        int doctorUserId, int examId, UpdateExamRequest req, CancellationToken ct);

    /// <summary>Delete an exam and its questions.</summary>
    Task<bool> DeleteExamAsync(int doctorUserId, int examId, CancellationToken ct);

    /// <summary>Update a single question inside an exam.</summary>
    Task<ExamQuestionDto?> UpdateQuestionAsync(
        int doctorUserId, int examId, int questionId,
        UpdateExamQuestionRequest req, CancellationToken ct);

    /// <summary>Delete a single question from an exam.</summary>
    Task<bool> DeleteQuestionAsync(int doctorUserId, int examId, int questionId, CancellationToken ct);
}

/// <summary>One course exposed to the doctor for exam generation.</summary>
public record ExamCatalogCourseDto(
    int Id, string Code, string Title, string Department,
    int? AcademicYear, string Semester);

public class ExamGeneratorService : IExamGeneratorService
{
    private static readonly string[] ValidQuestionTypes = { "MCQ", "TrueFalse", "ShortAnswer" };
    private static readonly string[] ValidDifficulties = { "easy", "medium", "hard", "mixed" };
    private static readonly string[] ValidLanguages = { "en", "ar" };
    private const int MaxQuestions = 50;

    private readonly AppDbContext _db;
    private readonly IDoctorAiService _ai;
    private readonly DoctorAiOptions _options;
    private readonly DoctorAccessService _access;

    public ExamGeneratorService(
        AppDbContext db, IDoctorAiService ai, IOptions<DoctorAiOptions> options,
        DoctorAccessService access)
    {
        _db = db;
        _ai = ai;
        _options = options.Value;
        _access = access;
    }

    public async Task<IReadOnlyList<ExamCatalogCourseDto>> GetAssignedCoursesAsync(
        int doctorUserId, CancellationToken ct = default)
    {
        var assignments = (await _access.GetAssignedCourseIdsAsync(doctorUserId)).ToList();

        if (assignments.Count == 0)
            return new List<ExamCatalogCourseDto>();

        return await _db.Courses.AsNoTracking()
            .Where(c => assignments.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new ExamCatalogCourseDto(
                c.Id, c.Code, c.Title, c.Department,
                c.AcademicYear, c.Semester ?? ""))
            .ToListAsync(ct);
    }

    public async Task<ExamGenerationResponse> GenerateAsync(
        int doctorUserId, ExamGenerationRequest req, CancellationToken ct)
    {
        if (req.QuestionCount < 1 || req.QuestionCount > MaxQuestions)
            throw new AiUnavailableException(
                $"Question count must be between 1 and {MaxQuestions}.");

        var reqTypes = req.QuestionTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim())
            .ToList();
        if (!reqTypes.All(t => ValidQuestionTypes.Any(v =>
                string.Equals(v, t, StringComparison.OrdinalIgnoreCase))))
        {
            throw new AiUnavailableException(
                $"Question types must be one of: {string.Join(", ", ValidQuestionTypes)}.");
        }

        if (!ValidDifficulties.Contains(req.Difficulty.ToLowerInvariant()))
            throw new AiUnavailableException(
                $"Difficulty must be one of: {string.Join(", ", ValidDifficulties)}.");

        if (!ValidLanguages.Contains(req.Language.ToLowerInvariant()))
            throw new AiUnavailableException(
                $"Language must be one of: {string.Join(", ", ValidLanguages)}.");

        // Authorize: doctor must be assigned to this course (legacy
        // CourseInstructors rows and CourseAssignments both count).
        if (!await _access.IsAssignedToCourseAsync(doctorUserId, req.CourseId))
            throw new AiUnavailableException("You are not assigned to teach this course.");

        var course = await _db.Courses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CourseId, ct)
            ?? throw new AiUnavailableException("Course not found.");

        // Load this course's materials (content first) — same university,
        // same course, teacher-scoped.
        var materials = await _db.CourseMaterials.AsNoTracking()
            .Where(m => m.CourseId == req.CourseId && !string.IsNullOrWhiteSpace(m.Content))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        if (materials.Count == 0)
            throw new AiUnavailableException("No lecture materials for this course yet.");

        var combined = new StringBuilder();
        foreach (var m in materials)
        {
            combined.AppendLine($"### {m.Title}");
            combined.AppendLine(m.Content ?? "");
            combined.AppendLine();
        }

        var content = Truncate(combined.ToString(), _options.MaxContextChars);
        var materialTitles = materials.Select(m => m.Title).ToList();

        var systemPrompt =
            "You are an expert university exam generator. " +
            "Create exam questions STRICTLY from the provided lecture material. " +
            "Do not invent facts not present in the material. " +
            "Respond with ONLY a JSON object with key \"questions\" (an array). " +
            "Each question object must have: " +
            "\"questionText\" (string), \"questionType\" (\"MCQ\" | \"TrueFalse\" | \"ShortAnswer\"), " +
            "\"correctAnswer\" (string), \"explanation\" (string), \"sourceMaterial\" (string - the exact lecture title used), " +
            "\"optionsJson\" (JSON array of exactly 4 option strings, only for MCQ; null otherwise). " +
            "Do not include any text outside the JSON object.";

        var userPrompt =
            $"Course: {course.Title} ({course.Code})\n" +
            $"Difficulty: {req.Difficulty}\nLanguage: {req.Language}\n" +
            $"Number of questions: {req.QuestionCount}\n" +
            $"Question types: {req.QuestionTypes}\n\n" +
            $"Lecture material:\n{content}";

        var raw = await _ai.CompleteAsync(systemPrompt, userPrompt, ct);
        var questions = ParseQuestions(raw, req.QuestionCount);

        return new ExamGenerationResponse(
            (await _ai.GetProvidersAsync(ct)).FirstOrDefault()?.Name ?? "unknown",
            _ai.GetModelName(),
            questions,
            req.CourseId, course.Code, course.Title);
    }

    public async Task<ExamDto> SaveExamAsync(
        int doctorUserId, UpdateExamRequest req, CancellationToken ct)
    {
        var course = await _db.Courses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CourseId, ct)
            ?? throw new AiUnavailableException("Course not found.");

        // Legacy CourseInstructors rows count as assignments as well.
        if (!await _access.IsAssignedToCourseAsync(doctorUserId, req.CourseId))
            throw new AiUnavailableException("You are not assigned to teach this course.");

        var exam = new Exam
        {
            DoctorUserId = doctorUserId,
            CourseId = req.CourseId,
            Title = req.Title,
            QuestionCount = req.QuestionCount,
            QuestionTypes = req.QuestionTypes,
            Difficulty = req.Difficulty,
            Language = req.Language,
            MaterialsUsed = req.MaterialsUsed,
            CreatedAtUtc = DateTime.UtcNow,
            Questions = req.Questions.Select((q, i) => new ExamQuestion
            {
                QuestionText = q.QuestionText,
                QuestionType = q.QuestionType,
                CorrectAnswer = q.CorrectAnswer ?? "",
                Explanation = q.Explanation,
                SourceMaterial = q.SourceMaterial,
                OptionsJson = q.OptionsJson,
                OrderIndex = q.OrderIndex
            }).ToList()
        };

        _db.Exams.Add(exam);
        await _db.SaveChangesAsync(ct);

        return MapToDto(exam, course);
    }

    public async Task<IReadOnlyList<ExamListDto>> ListExamsAsync(
        int doctorUserId, CancellationToken ct = default)
    {
        return await _db.Exams.AsNoTracking()
            .Where(e => e.DoctorUserId == doctorUserId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Include(e => e.Course)
            .Select(e => new ExamListDto(
                e.Id, e.CourseId,
                e.Course.Code, e.Course.Title,
                e.Title, e.QuestionCount, e.Difficulty, e.Language,
                e.CreatedAtUtc, e.Status, e.PublishedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<ExamDto?> GetExamAsync(
        int doctorUserId, int examId, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Include(e => e.Course)
            .Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId && e.DoctorUserId == doctorUserId, ct);
        return exam is null ? null : MapToDto(exam);
    }

    public async Task<ExamDto?> UpdateExamAsync(
        int doctorUserId, int examId, UpdateExamRequest req, CancellationToken ct)
    {
        var exam = await _db.Exams.Include(e => e.Course).Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId && e.DoctorUserId == doctorUserId, ct);
        if (exam is null) return null;

        exam.Title = req.Title;
        exam.QuestionCount = req.QuestionCount;
        exam.QuestionTypes = req.QuestionTypes;
        exam.Difficulty = req.Difficulty;
        exam.Language = req.Language;
        exam.MaterialsUsed = req.MaterialsUsed;

        // Replace all questions.
        _db.ExamQuestions.RemoveRange(exam.Questions);
        exam.Questions = req.Questions.Select((q, i) => new ExamQuestion
        {
            QuestionText = q.QuestionText,
            QuestionType = q.QuestionType,
            CorrectAnswer = q.CorrectAnswer ?? "",
            Explanation = q.Explanation,
            SourceMaterial = q.SourceMaterial,
            OptionsJson = q.OptionsJson,
            OrderIndex = q.OrderIndex
        }).ToList();

        await _db.SaveChangesAsync(ct);
        return MapToDto(exam);
    }

    public async Task<bool> DeleteExamAsync(
        int doctorUserId, int examId, CancellationToken ct)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(
            e => e.Id == examId && e.DoctorUserId == doctorUserId, ct);
        if (exam is null) return false;

        _db.Exams.Remove(exam);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ExamQuestionDto?> UpdateQuestionAsync(
        int doctorUserId, int examId, int questionId,
        UpdateExamQuestionRequest req, CancellationToken ct)
    {
        var exam = await _db.Exams.Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId && e.DoctorUserId == doctorUserId, ct);
        if (exam is null) return null;

        var question = exam.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null) return null;

        question.QuestionText = req.QuestionText;
        question.QuestionType = req.QuestionType;
        question.CorrectAnswer = req.CorrectAnswer ?? "";
        question.Explanation = req.Explanation;
        question.SourceMaterial = req.SourceMaterial;
        question.OptionsJson = req.OptionsJson;
        question.OrderIndex = req.OrderIndex;

        await _db.SaveChangesAsync(ct);
        return MapQuestionToDto(question);
    }

    public async Task<bool> DeleteQuestionAsync(
        int doctorUserId, int examId, int questionId, CancellationToken ct)
    {
        var exam = await _db.Exams.Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId && e.DoctorUserId == doctorUserId, ct);
        if (exam is null) return false;

        var question = exam.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null) return false;

        _db.ExamQuestions.Remove(question);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static List<GeneratedExamQuestionDto> ParseQuestions(
        string raw, int expectedCount)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new AiInvalidResponseException(
                "The AI response did not contain a valid JSON array of questions. Please try again.");

        JsonElement array;
        try
        {
            using var doc = JsonDocument.Parse(raw.Substring(start, end - start + 1));
            array = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AiInvalidResponseException(
                "The AI response could not be parsed as questions. Please try again.");
        }

        if (array.ValueKind != JsonValueKind.Array)
            throw new AiInvalidResponseException("The AI response was not a list of questions.");

        var list = new List<GeneratedExamQuestionDto>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("questionText", out var q) || q.ValueKind != JsonValueKind.String) continue;

            var questionText = q.GetString() ?? "";
            var questionType = item.TryGetProperty("questionType", out var qt) && qt.ValueKind == JsonValueKind.String
                ? qt.GetString() ?? "MCQ" : "MCQ";
            var correctAnswer = item.TryGetProperty("correctAnswer", out var ca) && ca.ValueKind == JsonValueKind.String
                ? ca.GetString() ?? "" : "";
            var explanation = item.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String
                ? ex.GetString() : null;
            var sourceMaterial = item.TryGetProperty("sourceMaterial", out var sm) && sm.ValueKind == JsonValueKind.String
                ? sm.GetString() : null;
            string? optionsJson = null;
            if (item.TryGetProperty("optionsJson", out var oj) && oj.ValueKind == JsonValueKind.String)
                optionsJson = oj.GetString();

            list.Add(new GeneratedExamQuestionDto(
                questionText, questionType, correctAnswer, explanation, sourceMaterial, optionsJson));
        }

        if (list.Count == 0)
            throw new AiInvalidResponseException(
                "The AI did not return any usable questions. Please try again.");

        if (list.Count > expectedCount)
            list = list.Take(expectedCount).ToList();

        return list;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";

    private ExamDto MapToDto(Exam exam, Course? course = null)
    {
        var examCourse = course ?? exam.Course
            ?? throw new InvalidOperationException("Exam course navigation was not loaded.");
        return new(
            exam.Id, exam.DoctorUserId, exam.CourseId,
            examCourse.Code, examCourse.Title,
            exam.Title, exam.QuestionCount, exam.QuestionTypes,
            exam.Difficulty, exam.Language, exam.MaterialsUsed,
            exam.CreatedAtUtc, exam.Status, exam.PublishedAtUtc, exam.Marks,
            exam.Questions.OrderBy(q => q.OrderIndex)
                .Select(MapQuestionToDto).ToList());
    }

    private static ExamQuestionDto MapQuestionToDto(ExamQuestion q) =>
        new(
            q.Id, q.ExamId, q.QuestionText, q.QuestionType,
            q.CorrectAnswer, q.Explanation, q.SourceMaterial,
            q.OptionsJson, q.OrderIndex, q.Marks);
}