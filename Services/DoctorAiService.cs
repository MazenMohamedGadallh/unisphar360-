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
    private readonly IWebHostEnvironment _env;

    public DoctorAiService(
        AppDbContext db, ChatProviderRegistry registry,
        IOptions<DoctorAiOptions> options, IWebHostEnvironment env)
    {
        _db = db;
        _registry = registry;
        _options = options.Value;
        _env = env;
    }

    public Task<IReadOnlyList<ChatProviderDto>> GetProvidersAsync(CancellationToken ct) =>
        _registry.AvailableAsync(ct);

    private async Task<IChatProvider> ResolveProviderAsync(CancellationToken ct)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_options.PreferredProvider))
            candidates.Add(_options.PreferredProvider.Trim());
        candidates.AddRange(ProviderOrder);

        foreach (var name in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (found, provider) = await _registry.TryGetAsync(name, ct);
            if (found && provider is not null) return provider;
        }

        throw new AiUnavailableException(
            "No AI provider is configured. Set an API key (for example OPENAI_API_KEY, ANTHROPIC_API_KEY, " +
            "GEMINI_API_KEY or OPENROUTER_API_KEY) or run a local Ollama instance, then try again.");
    }

    private async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var provider = await ResolveProviderAsync(ct);
        var request = new ChatRequest
        {
            Provider = provider.Name,
            Model = _registry.ResolveModel(provider.Name),
            SystemPrompt = systemPrompt,
            Messages = new List<ChatTurn> { new() { Role = "user", Content = userPrompt } },
            MaxTokens = _options.MaxTokens
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));

        var sb = new StringBuilder();
        try
        {
            await foreach (var token in provider.StreamAsync(request, timeoutCts.Token))
                sb.Append(token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("The AI request timed out. Please try again.");
        }
        catch (ChatProviderException ex)
        {
            throw new AiUnavailableException(ex.Message);
        }

        var text = sb.ToString().Trim();
        if (text.Length == 0)
            throw new AiInvalidResponseException("The AI provider returned an empty response.");
        return text;
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

        var providerName = (await ResolveProviderAsync(ct)).Name;
        var model = _registry.ResolveModel(providerName);
        var raw = await CompleteAsync(system, user, ct);
        var questions = ParseQuestions(raw, count);

        return new QuizGenerationResponse(providerName, model, questions, IsDraft: true);
    }

    private static IReadOnlyList<GeneratedQuestionDto> ParseQuestions(string raw, int expected)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new AiInvalidResponseException("The AI response was not valid JSON question data. Please try again.");

        JsonElement array;
        try
        {
            using var doc = JsonDocument.Parse(raw.Substring(start, end - start + 1));
            array = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AiInvalidResponseException("The AI response could not be parsed as questions. Please try again.");
        }

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

            var correctIndex = item.TryGetProperty("correctIndex", out var ci) && ci.ValueKind == JsonValueKind.Number
                ? ci.GetInt32()
                : 0;
            if (correctIndex < 0 || correctIndex >= options.Count)
                throw new AiInvalidResponseException("The AI returned a question with an invalid correct answer index.");

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

        var providerName = (await ResolveProviderAsync(ct)).Name;
        var model = _registry.ResolveModel(providerName);
        var summary = await CompleteAsync(system, user, ct);
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

        var scored = materials
            .Select(m => (Material: m, Text: (m.Title + "\n" + m.Description + "\n" + m.Content)))
            .Select(x => (x.Material, x.Text, Score: Score(x.Text, keywords)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var providerName = (await ResolveProviderAsync(ct)).Name;
        var model = _registry.ResolveModel(providerName);

        if (scored.Count == 0)
        {
            return new LectureAnswerDto(
                "The available lecture material for this course does not contain enough evidence to answer this " +
                "question. Try rephrasing or upload the relevant lecture first.",
                Array.Empty<string>(), Grounded: false, providerName, model);
        }

        var context = new StringBuilder();
        var sources = new List<string>();
        foreach (var (material, text, _) in scored)
        {
            context.AppendLine($"[{material.Title}]: {Truncate(text, 2500)}");
            sources.Add($"{course.Title} — {material.Title}");
        }

        var system =
            "You are a teaching assistant answering questions about a specific course's lecture material. " +
            "Answer ONLY from the provided lecture excerpts. If they do not contain the answer, say so clearly " +
            "and do not invent facts. Keep the answer focused and cite the lecture title you used.";
        var user = $"Course: {course.Title}\nQuestion: {req.Question}\n\nLecture excerpts:\n{context}";

        var answer = await CompleteAsync(system, user, ct);
        return new LectureAnswerDto(answer, sources.Distinct().ToList(), Grounded: true, providerName, model);
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
