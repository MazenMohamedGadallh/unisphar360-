using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UniSphere.Api.Data;

namespace UniSphere.Api.Services;

/// <summary>
/// Real-provider AI helper for graduate features (CV analysis, interview
/// questions/feedback, roadmap suggestions). Uses the same provider registry
/// as the chat module; when no provider is available it throws a real
/// <see cref="ChatProviderException"/> — output is never faked.
/// </summary>
public class GraduateAiService(
    IAiCompletionService completion,
    IAiGroundingService grounding,
    IRagClient rag,
    ILogger<GraduateAiService> logger)
{
    /// <summary>Run a one-shot prompt on the best live provider (Gemini →
    /// OpenRouter → ...), failing over to the next one on error.</summary>
    private async Task<(string Text, string ProviderName, string Model)> GenerateAsync(
        string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct)
    {
        var result = await completion.CompleteAsync(systemPrompt, userPrompt, maxTokens, ct);
        return (result.Text, result.Provider, result.Model);
    }

    /// <summary>Live UniSphere context (profile, skills, CV, target roles, job
    /// listings; or course data for students) appended to a prompt. Best effort.</summary>
    private async Task<string> ContextBlockAsync(int userId, string query, CancellationToken ct, int maxChars = 4000)
    {
        if (userId <= 0) return "";
        try
        {
            var g = await grounding.BuildAsync(userId, query, ct, maxChars: maxChars);
            return g.HasContext ? "\n\n" + g.Context : "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation("Grounding skipped: {Reason}", ex.Message);
            return "";
        }
    }

    public sealed record AiMeta(string ProviderName, string Model);

    /// <summary>Generate JSON and deserialize; failures become explicit
    /// <see cref="ChatProviderException"/> messages only — internals are
    /// logged, never faked and never leaked to the client.</summary>
    private async Task<(T Value, AiMeta Meta)> GenerateJsonAsync<T>(
        string systemPrompt, string userPrompt, int maxTokens,
        Func<JsonElement, T>? parse, CancellationToken ct)
    {
        var (raw, providerName, model) = await GenerateAsync(systemPrompt, userPrompt, maxTokens, ct);
        var json = ExtractJson(raw);
        try
        {
            T value;
            if (parse is null)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                value = json.Deserialize<T>(options)
                    ?? throw new ChatProviderException("The AI response was not in the expected format.");
            }
            else
                value = parse(json);

            return (value, new AiMeta(providerName, model));
        }
        catch (ChatProviderException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            logger.LogWarning("AI JSON parse failed. Raw snippet: {Snippet}", raw[..Math.Min(400, raw.Length)]);
            throw new ChatProviderException("The AI response was not valid JSON. Please try again.");
        }
    }

    // ---------------- CV analysis ----------------

    public sealed record CvAnalysisData(
        string OverallAssessment, int ScorePercent,
        string[] Strengths, string[] Weaknesses, string[] Suggestions,
        KeywordRewriteData[] Rewrites, string[] Keywords);

    public sealed record KeywordRewriteData(string Original, string Rewritten, string Why);

    public async Task<(CvAnalysisData Value, AiMeta Meta)> AnalyzeCvAsync(
        int userId, string cvText, string? targetRole, CancellationToken ct)
    {
        var truncated = cvText.Length > 8000 ? cvText[..8000] : cvText;
        var role = string.IsNullOrWhiteSpace(targetRole) ? "the graduate's target role" : targetRole!.Trim();

        var system = "You are a senior technical recruiter and evidence-based ATS analyst. " +
            "Analyze only claims supported by the supplied CV. Distinguish explicit skills from inferred skills; never invent experience. " +
            "Be specific, constructive and concise. Return ONLY a JSON object, no markdown fences, no commentary.";
        var template = """
            Analyze this CV for the target role: {role}

            CV TEXT:
            ---
            {cv}
            ---

            Return JSON with exactly this shape:
            {
              "overallAssessment": "2-3 sentences",
              "scorePercent": 0-100,
              "strengths": ["..."],
              "weaknesses": ["..."],
              "suggestions": ["..."],
              "rewrites": [{ "original": "weak bullet from the CV", "rewritten": "improved bullet", "why": "reason" }],
              "keywords": ["job-description keywords the CV should include for {role}"]
            }
            Rules: 3-5 items per list; scorePercent reflects realistic junior-level hiring competitiveness; rewrites must quote actual CV text.
            """ .Replace("{role}", role);
        var user = template.Replace("{cv}", truncated);

        // The RAG service analyses first, grounded in the CV itself: the CV goes in
        // once, as the retrieved document, so small local models keep the whole
        // prompt in context. The general provider chain is the fallback.
        var ragQuestion = template.Replace("{cv}", "(provided above as the excerpts titled \"Uploaded CV\")");
        var viaRag = await TryRagJsonAsync(system, ragQuestion, truncated, ParseCvAnalysis, ct);
        if (viaRag is { } ragResult) return ragResult;
        return await GenerateJsonAsync<CvAnalysisData>(system, user, 2048, ParseCvAnalysis, ct);
    }

    /// <summary>Ask the RAG service for a JSON answer grounded in <paramref name="documentText"/>.
    /// Null when RAG is off, unreachable or answers with something unparsable.</summary>
    private async Task<(T Value, AiMeta Meta)?> TryRagJsonAsync<T>(
        string systemPrompt, string userPrompt, string documentText,
        Func<JsonElement, T> parse, CancellationToken ct)
    {
        if (!rag.IsConfigured) return null;
        try
        {
            var answer = await rag.QueryAsync(userPrompt,
                [new RagDocument("cv", "Uploaded CV", documentText)], systemPrompt, ct);
            var value = parse(ExtractJson(answer.Answer));
            return (value, new AiMeta(rag.DisplayName, $"{answer.Provider}/{answer.Model}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogInformation("RAG CV analysis unavailable, using the provider chain: {Reason}", ex.Message);
            return null;
        }
    }

    private static CvAnalysisData ParseCvAnalysis(JsonElement root)
    {
        string GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int GetInt(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? Math.Clamp(i, 0, 100) : 0;
        string[] GetList(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
                return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray();
            return [];
        }
        var rewrites = new List<KeywordRewriteData>();
        if (root.TryGetProperty("rewrites", out var rw) && rw.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rw.EnumerateArray())
            {
                var original = GetString(item, "original");
                var rewritten = GetString(item, "rewritten");
                if (original.Length > 0 && rewritten.Length > 0)
                    rewrites.Add(new KeywordRewriteData(original, rewritten, GetString(item, "why")));
            }
        }
        return new CvAnalysisData(
            GetString(root, "overallAssessment"), GetInt(root, "scorePercent"),
            GetList(root, "strengths"), GetList(root, "weaknesses"), GetList(root, "suggestions"),
            [.. rewrites], GetList(root, "keywords"));
    }

    // ---------------- Practice quiz ----------------

    public sealed record PracticeQuestionData(string Question, string[] Options, int CorrectIndex, string Explanation, string? Reference);
    public sealed record PracticeQuizData(PracticeQuestionData[] Questions);

    public async Task<(PracticeQuizData Value, AiMeta Meta)> GeneratePracticeQuizAsync(
        string courseTitle, string materialTitle, string materialText, int count, CancellationToken ct,
        int userId = 0)
    {
        var system = "You are a university learning assistant. Generate accurate multiple-choice practice questions strictly from the supplied lecture material. " +
            "Do not invent facts. Return ONLY JSON with exactly: {\"questions\":[{\"question\":\"...\",\"options\":[\"...\",\"...\",\"...\",\"...\"],\"correctIndex\":0,\"explanation\":\"...\",\"reference\":\"...\"}]}.";
        var user = $"Course: {courseTitle}\nMaterial: {materialTitle}\nGenerate {count} questions.\nLecture content:\n{materialText[..Math.Min(materialText.Length, 12000)]}";

        return await GenerateJsonAsync<PracticeQuizData>(system, user, 2048, ParsePracticeQuiz, ct);
    }

    private static PracticeQuizData ParsePracticeQuiz(JsonElement root)
    {
        // Reuse JSON extractor-compatible array parsing via JSON string conversion.
        if (!root.TryGetProperty("questions", out var array) || array.ValueKind != JsonValueKind.Array)
            throw new ChatProviderException("The AI response did not contain a valid quiz question list.");

        var questions = new List<PracticeQuestionData>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("question", out var q) || q.ValueKind != JsonValueKind.String) continue;
            if (!item.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array) continue;
            var options = opts.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToArray();
            if (options.Length < 2) continue;
            var correct = AiJsonExtractor.ReadAnswerIndex(item, options);
            if (correct < 0) continue; // unusable question: drop it, keep the rest of the quiz
            var explanation = item.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String ? ex.GetString() ?? "" : "";
            var reference = item.TryGetProperty("reference", out var rf) && rf.ValueKind == JsonValueKind.String ? rf.GetString() : null;
            questions.Add(new PracticeQuestionData(q.GetString() ?? "", options, correct, explanation, reference));
        }
        if (questions.Count == 0) throw new ChatProviderException("The AI did not return any usable practice questions.");
        return new PracticeQuizData([.. questions]);
    }

    // ---------------- Interview questions ----------------

    public sealed record QuestionsData(string[] Questions);

    public async Task<(QuestionsData Value, AiMeta Meta)> GenerateInterviewQuestionsAsync(
        int userId, string targetRole, string? jobDescription, int count, CancellationToken ct)
    {
        var system = "You are a professional interview coach preparing a junior job candidate. " +
            "Return ONLY a JSON object, no markdown fences, no commentary.";
        var user = """
            Target role: {role}
            {jobContext}
            Generate {count} realistic interview questions for this junior role.
            Mix technical questions about the role's core skills, behavioral questions, and questions about the candidate's projects.
            Use the candidate's profile, skills, CV and the listed jobs below (when present) to tailor the questions; never invent employers.
            Return JSON: { "questions": ["...", "..."] }
            """.Replace("{role}", targetRole?.Trim() ?? "Software Engineer")
               .Replace("{jobContext}", string.IsNullOrWhiteSpace(jobDescription) ? "" : $"Job description context:\n{jobDescription[..Math.Min(1500, jobDescription.Length)]}\n")
               .Replace("{count}", count.ToString());

        user += await ContextBlockAsync(userId, $"{targetRole} interview", ct);
        return await GenerateJsonAsync<QuestionsData>(system, user, 2048, ParseQuestions, ct);
    }

    private static QuestionsData ParseQuestions(JsonElement root)
    {
        if (root.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array)
        {
            var list = q.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => (x.GetString() ?? "").Trim())
                .Where(s => s.Length > 0 && s.Length <= 500)
                .Take(12)
                .ToArray();
            if (list.Length == 0)
                throw new ChatProviderException("The AI returned no interview questions.");
            return new QuestionsData(list);
        }
        throw new ChatProviderException("The AI response was missing the questions list.");
    }

    // ---------------- Interview answer feedback ----------------

    public sealed record FeedbackData(string Feedback, int Score);

    public async Task<(FeedbackData Value, AiMeta Meta)> FeedbackAnswerAsync(
        int userId, string targetRole, string question, string answer, CancellationToken ct)
    {
        var system = "You are an interview coach. Grade the candidate's answer and give actionable feedback. " +
            "Return ONLY a JSON object, no markdown fences.";
        var user = """
            Target role: {role}
            Interview question: {question}
            Candidate answer: {answer}

            Return JSON: { "feedback": "2-4 sentences: what was strong, what to improve, concrete example to use", "score": 0-10 }
            """.Replace("{role}", targetRole)
               .Replace("{question}", question)
               .Replace("{answer}", answer.Length > 4000 ? answer[..4000] : answer);

        user += await ContextBlockAsync(userId, targetRole, ct, 2000);
        return await GenerateJsonAsync<FeedbackData>(system, user, 1024, ParseFeedback, ct);
    }

    private static FeedbackData ParseFeedback(JsonElement root)
    {
        var feedback = root.TryGetProperty("feedback", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() ?? "" : "";
        var score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var i) ? Math.Clamp(i, 0, 10) : 5;
        if (feedback.Length == 0)
            throw new ChatProviderException("The AI returned empty feedback.");
        return new FeedbackData(feedback, score);
    }

    // ---------------- Roadmap suggestions ----------------

    public sealed record RoadmapSuggestionsData(RoadmapSuggestionData[] Items);
    public sealed record RoadmapSuggestionData(string Title, string Category, string? Notes, string? Why);

    public async Task<(RoadmapSuggestionsData Value, AiMeta Meta)> SuggestRoadmapAsync(
        int userId, string goal, string skillsSummary, CancellationToken ct)
    {
        var system = "You are a career coach building a concrete 3-month action plan for a junior graduate. " +
            "Return ONLY a JSON object, no markdown fences.";
        var user = """
            Career goal: {goal}
            Candidate skills: {skills}
            Return JSON: { "items": [ { "title": "action", "category": "Skill | Course | Project | Application | Milestone", "notes": "short practical detail", "why": "one sentence" } ] }
            Exactly 5 items, each title under 90 characters.
            """.Replace("{goal}", goal)
               .Replace("{skills}", skillsSummary);

        user += await ContextBlockAsync(userId, goal, ct);
        return await GenerateJsonAsync<RoadmapSuggestionsData>(system, user, 2048, ParseRoadmap, ct);
    }

    private static RoadmapSuggestionsData ParseRoadmap(JsonElement root)
    {
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var list = items.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => new RoadmapSuggestionData(
                    x.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "",
                    x.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "Milestone" : "Milestone",
                    x.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                    x.TryGetProperty("why", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null))
                .Where(i => i.Title.Length > 0)
                .Take(8)
                .ToArray();
            if (list.Length == 0)
                throw new ChatProviderException("The AI returned no roadmap suggestions.");
            return new RoadmapSuggestionsData(list);
        }
        throw new ChatProviderException("The AI response was missing the items list.");
    }

    // ---------------- JSON extraction ----------------

    internal static JsonElement ExtractJson(string raw)
    {
        // Tolerates <think> blocks, ```json fences, prose around the JSON and
        // truncated arrays (see AiJsonExtractor).
        if (!AiJsonExtractor.TryExtract(raw, out var element))
            throw new ChatProviderException("The AI response did not contain valid JSON. Please try again.");

        // Models sometimes return a bare array instead of {"questions":[...]}
        // (or the extractor salvages one from truncated output). Wrap it so
        // every parser can read it from "questions" / "items".
        if (element.ValueKind == JsonValueKind.Array)
        {
            using var doc = JsonDocument.Parse(
                "{\"questions\":" + element.GetRawText() + ",\"items\":" + element.GetRawText() + "}");
            return doc.RootElement.Clone();
        }
        if (element.ValueKind != JsonValueKind.Object)
            throw new ChatProviderException("The AI response was not in the expected format. Please try again.");
        return element;
    }
}
