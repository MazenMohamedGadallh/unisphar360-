using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UniSphere.Api.Data;

namespace UniSphere.Api.Services;

/// <summary>
/// Real-provider AI helper for graduate features (CV analysis, interview
/// questions/feedback, roadmap suggestions). Uses the same provider registry
/// as the chat module; when no provider is available it throws a real
/// <see cref="ChatProviderException"/> — output is never faked.
/// </summary>
public class GraduateAiService(
    ChatProviderRegistry registry,
    ILogger<GraduateAiService> logger)
{
    private static readonly string[] ProviderOrder = ["openai", "anthropic", "gemini", "openrouter", "ollama"];

    /// <summary>Providers that currently pass configuration + availability checks, in order.
    /// Empty when nothing is configured (caller throws the explicit setup message).</summary>
    private async Task<List<(IChatProvider Provider, string Model)>> CandidateProvidersAsync(CancellationToken ct)
    {
        var candidates = new List<(IChatProvider, string)>();
        foreach (var name in ProviderOrder)
        {
            var (found, provider) = await registry.TryGetAsync(name, ct);
            if (found && provider is not null)
                candidates.Add((provider, registry.ResolveModel(provider.Name)));
        }
        return candidates;
    }

    /// <summary>Run a one-shot prompt against the first configured provider that succeeds,
    /// falling back to the next configured provider when one fails. Config errors are
    /// never masked silently: when every provider fails, the aggregated per-provider
    /// messages are thrown so the user sees exactly why each was rejected.</summary>
    private async Task<(string Text, string ProviderName, string Model)> GenerateAsync(
        string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct)
    {
        var candidates = await CandidateProvidersAsync(ct);
        if (candidates.Count == 0)
            throw new ChatProviderException("No AI provider is configured. Set GEMINI_API_KEY (or OPENAI_API_KEY, " +
                "ANTHROPIC_API_KEY, OPENROUTER_API_KEY, or run a local Ollama server) to enable AI-powered features.");

        var failures = new List<string>();
        foreach (var (provider, model) in candidates)
        {
            var request = new ChatRequest
            {
                Provider = provider.Name,
                Model = model,
                SystemPrompt = systemPrompt,
                Messages = [new ChatTurn { Role = "user", Content = userPrompt }],
                MaxTokens = maxTokens
            };

            var text = new StringBuilder();
            try
            {
                await foreach (var token in provider.StreamAsync(request, ct))
                    text.Append(token);

                var result = text.ToString();
                if (string.IsNullOrWhiteSpace(result))
                {
                    failures.Add($"{provider.Name}: empty response");
                    continue;
                }
                return (result, provider.Name, model);
            }
            catch (OperationCanceledException) { throw; }
            catch (ChatProviderException ex)
            {
                logger.LogWarning("Graduate AI call failed on provider {Provider}: {Message}", provider.Name, ex.Message);
                failures.Add($"{provider.Name}: {ex.Message}");
            }
            catch (ChatLimitExceededException ex)
            {
                failures.Add($"{provider.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Graduate AI call failed on provider {Provider}", provider.Name);
                failures.Add($"{provider.Name}: temporarily unavailable");
            }
        }

        // One provider: surface its message verbatim (it is already actionable).
        // Several: join trimmed notes so the client's 300-char error budget still
        // carries every provider's reason.
        var note = failures.Count == 1
            ? failures[0]
            : string.Join("; ", failures.Select(f => f.Length > 120 ? f[..120] : f));
        throw new ChatProviderException("AI provider error — " + note);
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
        catch (JsonException)
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

        var system = "You are a senior technical recruiter and CV expert. " +
            "Analyze graduate CVs for real job-market readiness. Be specific and constructive. " +
            "Return ONLY a JSON object, no markdown fences, no commentary.";
        var user = """
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
            """ .Replace("{role}", role).Replace("{cv}", truncated);

        return await GenerateJsonAsync<CvAnalysisData>(system, user, 2048, ParseCvAnalysis, ct);
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
            Return JSON: { "questions": ["...", "..."] }
            """.Replace("{role}", targetRole?.Trim() ?? "Software Engineer")
               .Replace("{jobContext}", string.IsNullOrWhiteSpace(jobDescription) ? "" : $"Job description context:\n{jobDescription[..Math.Min(1500, jobDescription.Length)]}\n")
               .Replace("{count}", count.ToString());

        return await GenerateJsonAsync<QuestionsData>(system, user, 1536, ParseQuestions, ct);
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

        return await GenerateJsonAsync<RoadmapSuggestionsData>(system, user, 1536, ParseRoadmap, ct);
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
        // Tolerate ```json fences / prose around the object.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new ChatProviderException("The AI response did not contain JSON.");
        try
        {
            return JsonDocument.Parse(raw[start..(end + 1)]).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ChatProviderException("The AI response contained invalid JSON. Please try again.");
        }
    }
}
