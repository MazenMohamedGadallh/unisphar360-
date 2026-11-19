using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// AI teaching assistant for doctors. Provider calls go through the existing
/// injectable <see cref="ChatProviderRegistry"/>; API keys live only in
/// environment variables/user-secrets. Every feature:
///  - only reads lecture content the doctor is authorized to access,
///  - validates model output against a schema and fails loudly on malformed
///    output (never publishes a partial/garbage quiz),
///  - states honestly when AI is unavailable instead of pretending to work,
///  - marks generated quizzes as drafts requiring doctor review.
///
/// The lecture Q&amp;A uses lexical (term-matching) retrieval over authorised
/// lecture chunks and answers grounded in the retrieved text — a lightweight
/// RAG approach that adds no vector database or extra AI server.
/// </summary>
public interface IDoctorAiService
{
    Task<IReadOnlyList<ChatProviderDto>> GetProvidersAsync(CancellationToken ct);
    string GetModelName();
    /// <summary>The provider actually resolved for generation (name + model) —
    /// used so responses can be labeled truthfully, not alphabetically-first.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
    Task<QuizGenerationResponse> GenerateQuizAsync(int doctorUserId, QuizGenerationRequest req, CancellationToken ct);
    Task<LectureSummaryDto> SummarizeAsync(int doctorUserId, SummarizeRequest req, CancellationToken ct);
    Task<LectureAnswerDto> AnswerLectureQuestionAsync(int doctorUserId, LectureQuestionRequest req, CancellationToken ct);
}

public class DoctorAiService : IDoctorAiService
{
    private static readonly string[] ProviderOrder = { "openrouter", "openai", "anthropic", "gemini", "ollama" };

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "how", "what", "why", "when", "with", "from",
        "that", "this", "please", "explain", "help", "about", "does", "can", "you",
        "ما", "هل", "كيف", "في", "من", "على", "ان", "إلى", "الى", "عن", "هو", "هي",
        "التي", "الذي", "لماذا", "متى", "أين", "اين"
    };

    private readonly AppDbContext _db;
    private readonly ChatProviderRegistry _registry;
    private readonly DoctorAiOptions _options;
    private readonly ChatOptions _chatOptions;
    private readonly IWebHostEnvironment _env;
    private readonly IRagGroundingService? _grounding;
    private readonly IAiCompletionService _completion;
    private readonly IAiGroundingService? _dbGrounding;
    private AiCompletion? _last;

    public DoctorAiService(
        AppDbContext db, ChatProviderRegistry registry,
        IOptions<DoctorAiOptions> options, IOptions<ChatOptions> chatOptions, IWebHostEnvironment env,
        IAiCompletionService completion,
        IRagGroundingService? grounding = null,
        IAiGroundingService? dbGrounding = null)
    {
        _completion = completion;
        _dbGrounding = dbGrounding;
        _grounding = grounding;
        _db = db;
        _registry = registry;
        _options = options.Value;
        _chatOptions = chatOptions.Value;
        _env = env;
    }

    public Task<IReadOnlyList<ChatProviderDto>> GetProvidersAsync(CancellationToken ct) =>
        _registry.AvailableAsync(ct);

    public string GetModelName()
    {
        // First live provider in auto-selection order (the one CompleteAsync tries first).
        var configured = _registry.Available().Select(p => p.Name).ToList();
        var preferred = new[] { _options.PreferredProvider }
            .Concat(configured)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .FirstOrDefault(n => configured.Contains(n, StringComparer.OrdinalIgnoreCase));
        return _registry.ResolveModel(preferred ?? "gemini");
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        try
        {
            // Fails over Gemini → OpenRouter → ... until one answers.
            _last = await _completion.CompleteAsync(systemPrompt, userPrompt, _options.MaxTokens, ct,
                Math.Max(5, _options.TimeoutSeconds), _options.PreferredProvider);
        }
        catch (ChatProviderException ex)
        {
            throw new AiUnavailableException(ex.Message);
        }
        return _last.Text;
    }

    // ---- Quiz generator ----

    public async Task<QuizGenerationResponse> GenerateQuizAsync(
        int doctorUserId, QuizGenerationRequest req, CancellationToken ct)
    {
        var (authorized, material) = await LoadAuthorizedContentAsync(doctorUserId, req.CourseId, req.MaterialId, ct);
        if (!authorized) throw new AiUnavailableException("You are not authorized to use this course's content.");

        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CourseId, ct)
            ?? throw new AiUnavailableException("Course not found.");

        var count = Math.Clamp(req.QuestionCount, 1, 15);
        // Never ask the model to write a quiz with nothing to ground it: it would
        // invent questions (or quiz itself about the prompt).
        if (string.IsNullOrWhiteSpace(material.content) || material.content.Trim().Length < 80)
            throw new AiUnavailableException(
                "This course has no lecture text yet. Add lecture content (or a text/markdown file) before generating a quiz.");
        var content = Truncate(material.content, _options.MaxContextChars);
        var title = material.title ?? course.Title;

        var system =
            "You are an academic quiz generator. Produce exam-quality multiple-choice questions " +
            "strictly from the provided lecture content. Respond with ONLY a JSON array, no prose. " +
            "Each element must be an object with keys: \"question\" (string), " +
            "\"options\" (array of exactly 4 strings), \"correctIndex\" (integer 0-3), " +
            "\"explanation\" (string), \"reference\" (string, the lecture section/title or empty).";
        var user =
            $"Course: {course.Title} ({course.Code})\nLecture/section: {title}\n" +
            $"Create {count} questions.\n\nLecture content:\n{content}";

        var raw = await CompleteAsync(system, user, ct);
        var providerName = _last!.Provider;
        var model = _last.Model;
        var questions = ParseQuestions(raw, count);

        return new QuizGenerationResponse(providerName, model, questions, IsDraft: true);
    }

    private static IReadOnlyList<GeneratedQuestionDto> ParseQuestions(string raw, int expected)
    {
        if (!AiJsonExtractor.TryExtract(raw, out var array))
            throw new AiInvalidResponseException("The AI response was not valid JSON question data. Please try again.");

        if (array.ValueKind == JsonValueKind.Object && array.TryGetProperty("questions", out var wrapped))
            array = wrapped;
        if (array.ValueKind != JsonValueKind.Array)
            throw new AiInvalidResponseException("The AI response was not a list of questions.");

        var list = new List<GeneratedQuestionDto>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("question", out var q) || q.ValueKind != JsonValueKind.String) continue;
            if (!item.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array) continue;

            var options = opts.EnumerateArray()
                .Where(o => o.ValueKind == JsonValueKind.String)
                .Select(o => o.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
            if (options.Count < 2) continue;

            var correctIndex = AiJsonExtractor.ReadAnswerIndex(item, options);
            if (correctIndex < 0) continue; // unusable question: drop it, keep the rest

            var explanation = item.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String
                ? ex.GetString() ?? "" : "";
            var reference = item.TryGetProperty("reference", out var rf) && rf.ValueKind == JsonValueKind.String
                ? rf.GetString() : null;

            list.Add(new GeneratedQuestionDto(q.GetString() ?? "", options, correctIndex, explanation,
                string.IsNullOrWhiteSpace(reference) ? null : reference));
        }

        if (list.Count == 0)
            throw new AiInvalidResponseException("The AI did not return any usable questions. Please try again.");
        if (list.Count > expected) list = list.Take(expected).ToList();
        return list;
    }

    // ---- Lecture summarizer ----

    public async Task<LectureSummaryDto> SummarizeAsync(
        int doctorUserId, SummarizeRequest req, CancellationToken ct)
    {
        var material = await _db.CourseMaterials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == req.MaterialId, ct)
            ?? throw new AiInvalidResponseException("Lecture material not found.");
        if (material.CourseId != req.CourseId)
            throw new AiUnavailableException("You are not authorized to use this lecture.");

        var (authorized, _) = await LoadAuthorizedContentAsync(doctorUserId, req.CourseId, req.MaterialId, ct);
        if (!authorized) throw new AiUnavailableException("You are not authorized to use this course's content.");

        var text = await ExtractTextAsync(material, ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new AiInvalidResponseException(
                "This material has no extractable text. Add lecture text or upload a plain-text/markdown file " +
                "(scanned PDFs need OCR, which is not available).");

        var content = Truncate(text, _options.MaxContextChars);
        var system =
            "You summarize university lecture material for an instructor. " +
            "Produce a concise, faithful summary with key concepts and definitions. " +
            "Do not invent facts not present in the text. Answer with plain text (short markdown allowed).";
        var user = $"Lecture title: {material.Title}\nType: {material.Type}\n\nContent:\n{content}";

        var summary = await CompleteAsync(system, user, ct);
        var providerName = _last!.Provider;
        var model = _last.Model;
        return new LectureSummaryDto(material.Id, material.Title, summary, providerName, model);
    }

    // ---- Lecture question answering (lexical RAG) ----

    public async Task<LectureAnswerDto> AnswerLectureQuestionAsync(
        int doctorUserId, LectureQuestionRequest req, CancellationToken ct)
    {
        var authorized = await IsAuthorizedAsync(doctorUserId, req.CourseId, ct);
        if (!authorized) throw new AiUnavailableException("You are not authorized to use this course's content.");

        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CourseId, ct)
            ?? throw new AiUnavailableException("Course not found.");

        var materials = await _db.CourseMaterials.AsNoTracking()
            .Where(m => m.CourseId == req.CourseId)
            .ToListAsync(ct);

        var keywords = Regex.Split(req.Question.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(w => w.Length > 2 && !Stopwords.Contains(w))
            .Distinct()
            .ToList();

        // RAG retrieval over the whole course (semantic), merged with the
        // lexical excerpts below. Silent no-op when the RAG service is off.
        var ragContext = RagGrounding.None;
        if (_grounding is not null)
        {
            var ragDocs = materials
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new RagDocument(m.Id.ToString(), m.Title, Truncate(m.Content!, 6000)))
                .ToList();
            ragContext = await _grounding.GroundAsync(req.Question, ragDocs, ct);
        }

        var scored = materials
            .Select(m => (Material: m, Text: (m.Title + "\n" + m.Description + "\n" + m.Content)))
            .Select(x => (x.Material, x.Text, Score: Score(x.Text, keywords)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var hasCourseContext = scored.Count > 0 || ragContext.HasContext;

        var context = new StringBuilder();
        var sources = new List<string>();
        foreach (var (material, text, _) in scored)
        {
            context.AppendLine($"[{material.Title}]: {Truncate(text, 2500)}");
            sources.Add($"{course.Title} — {material.Title}");
        }

        if (ragContext.HasContext)
        {
            context.AppendLine(ragContext.Context);
            sources.AddRange(ragContext.Sources.Select(t => $"{course.Title} — {t}"));
        }

        var system =
            "You are a teaching assistant answering questions about a specific course's lecture material. " +
            HybridGroundingPolicy.Instructions + " Keep the answer focused.";
        var user = hasCourseContext
            ? $"Course: {course.Title}\nQuestion: {req.Question}\n\nLecture excerpts:\n{context}"
            : $"Course: {course.Title}\nQuestion: {req.Question}\n\n(No lecture excerpt matched; answer from general knowledge and say so.)";
        if (_dbGrounding is not null)
        {
            try
            {
                var db = await _dbGrounding.BuildAsync(doctorUserId, req.Question, ct, req.CourseId, 3000);
                if (db.HasContext) user += "\n\n" + db.Context;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* context is optional */ }
        }

        var answer = await CompleteAsync(system, user, ct);
        return new LectureAnswerDto(answer, sources.Distinct().ToList(), Grounded: hasCourseContext, _last!.Provider, _last.Model);
    }

    private async Task<bool> IsAuthorizedAsync(int doctorUserId, int courseId, CancellationToken ct) =>
        await _db.CourseAssignments.AsNoTracking()
            .AnyAsync(a => a.DoctorUserId == doctorUserId && a.CourseId == courseId, ct);

    private async Task<(bool Authorized, (string? title, string content))> LoadAuthorizedContentAsync(
        int doctorUserId, int courseId, int? materialId, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(doctorUserId, courseId, ct))
            return (false, (null, ""));

        if (materialId.HasValue)
        {
            var material = await _db.CourseMaterials.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == materialId.Value && m.CourseId == courseId, ct);
            if (material is null) return (false, (null, ""));
            return (true, (material.Title, material.Content ?? ""));
        }

        var materials = await _db.CourseMaterials.AsNoTracking()
            .Where(m => m.CourseId == courseId && m.Content != "")
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(5)
            .ToListAsync(ct);
        var combined = string.Join("\n\n", materials.Select(m => $"{m.Title}\n{m.Content}"));
        return (true, ("Course material", combined));
    }

    /// <summary>Best-effort text extraction. Database text content is used
    /// first; uploaded plain-text files are read from disk. Binary/scan
    /// formats are rejected explicitly (no OCR in this project).</summary>
    private async Task<string> ExtractTextAsync(CourseMaterial material, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(material.Content)) return material.Content;

        if (!string.IsNullOrWhiteSpace(material.FileUrl)
            && material.FileUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(material.FileUrl).ToLowerInvariant();
            if (ext is ".txt" or ".md" or ".csv")
            {
                var physical = Path.Combine(_env.WebRootPath, material.FileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(physical))
                {
                    var info = new FileInfo(physical);
                    if (info.Length <= 5 * 1024 * 1024)
                        return await File.ReadAllTextAsync(physical, ct);
                }
            }
        }

        return "";
    }

    private static int Score(string text, List<string> keywords)
    {
        var lower = text.ToLowerInvariant();
        var score = 0;
        foreach (var k in keywords)
            if (lower.Contains(k, StringComparison.Ordinal)) score++;
        return score;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
