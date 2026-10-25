using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class ChatProviderRegistry
{
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "OpenAI",
        ["anthropic"] = "Claude",
        ["gemini"] = "Gemini",
        ["ollama"] = "Ollama",
        ["openrouter"] = "OpenRouter"
    };

    private static readonly Dictionary<string, string> MissingKeyHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "OPENAI_API_KEY",
        ["anthropic"] = "ANTHROPIC_API_KEY",
        ["gemini"] = "GEMINI_API_KEY",
        ["openrouter"] = "OPENROUTER_API_KEY"
    };

    private readonly Dictionary<string, IChatProvider> _all;
    private readonly IConfiguration _config;
    private readonly ChatOptions _options;
    private readonly ILogger<ChatProviderRegistry> _logger;

    public ChatProviderRegistry(
        IEnumerable<IChatProvider> providers,
        IConfiguration config,
        IOptions<ChatOptions> options,
        ILogger<ChatProviderRegistry> logger)
    {
        _config = config;
        _options = options.Value;
        _logger = logger;
        _all = providers
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sync snapshot (IsConfigured only). Prefer <see cref="AvailableAsync"/>.</summary>
    public IReadOnlyList<Dtos.ChatProviderDto> Available() =>
        _all.Values
            .Where(p => p is not IProviderStatus status || status.IsConfigured)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new Dtos.ChatProviderDto(
                p.Name,
                DisplayNames.TryGetValue(p.Name, out var display) ? display : p.Name,
                ResolveModel(p.Name)))
            .ToList();

    /// <summary>Live availability (probes Ollama, hides it when down). No keys logged.</summary>
    public async Task<IReadOnlyList<Dtos.ChatProviderDto>> AvailableAsync(CancellationToken ct = default)
    {
        var list = new List<Dtos.ChatProviderDto>();
        foreach (var p in _all.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool available;
            try
            {
                available = p is IProviderStatus status
                    ? await status.IsAvailableAsync(ct)
                    : true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat provider {Provider} availability check failed", p.Name);
                available = false;
            }
            if (available)
                list.Add(new Dtos.ChatProviderDto(
                    p.Name,
                    DisplayNames.TryGetValue(p.Name, out var display) ? display : p.Name,
                    ResolveModel(p.Name)));
        }
        return list;
    }

    /// <summary>All providers with availability + human-readable reason (key names only, never values).</summary>
    public async Task<IReadOnlyList<Dtos.ChatProviderStatusDto>> DescribeAsync(CancellationToken ct = default)
    {
        var list = new List<Dtos.ChatProviderStatusDto>();
        foreach (var p in _all.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var display = DisplayNames.TryGetValue(p.Name, out var d) ? d : p.Name;
            var model = ResolveModel(p.Name);
            bool configured = p is not IProviderStatus s || s.IsConfigured;
            bool available = false;
            string reason = "";
            try
            {
                available = p is IProviderStatus st ? await st.IsAvailableAsync(ct) : true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat provider {Provider} availability check failed", p.Name);
                available = false;
            }
            if (!available)
            {
                if (!configured)
                {
                    if (p is OllamaChatProvider)
                        reason = "Ollama is not configured (missing OLLAMA_MODEL)";
                    else if (MissingKeyHints.TryGetValue(p.Name, out var keyName))
                        reason = $"{display} is not configured (set {keyName} or Chat:Providers:{p.Name}:ApiKey)";
                    else
                        reason = $"{display} is not configured";
                }
                else if (p is OllamaChatProvider ollama)
                {
                    reason = ollama.UnavailabilityReason();
                }
                else
                {
                    reason = $"{display} is temporarily unavailable";
                }
            }
            list.Add(new Dtos.ChatProviderStatusDto(p.Name, display, model, available, reason));
        }
        return list;
    }

    /// <summary>Startup log: which providers are enabled and why others are hidden. No keys.</summary>
    public async Task LogStartupAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Dtos.ChatProviderStatusDto> statuses;
        try
        {
            statuses = await DescribeAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat provider startup check failed");
            return;
        }
        foreach (var s in statuses)
        {
            if (s.Available)
                _logger.LogInformation("Chat provider enabled: {Provider} ({Model})", s.Name, s.Model);
            else
                _logger.LogInformation("Chat provider hidden: {Provider} ({Reason})", s.Name, s.Reason);
        }
    }

    public bool TryGet(string? name, out IChatProvider? provider)
    {
        provider = null;
        return !string.IsNullOrWhiteSpace(name) && _all.TryGetValue(name.Trim(), out provider);
    }

    /// <summary>Availability-aware lookup (hides Ollama when down).</summary>
    public async Task<(bool Found, IChatProvider? Provider)> TryGetAsync(string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || !_all.TryGetValue(name.Trim(), out var provider))
            return (false, null);
        try
        {
            if (provider is IProviderStatus status && !await status.IsAvailableAsync(ct))
                return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat provider {Provider} availability check failed", provider.Name);
            return (false, null);
        }
        return (true, provider);
    }

    /// <summary>Model name with user-secrets/env override priority
    /// (OPENAI_MODEL, ANTHROPIC_MODEL, GEMINI_MODEL, OLLAMA_MODEL, OPENROUTER_MODEL),
    /// read at call time so only a restart is needed after changing a secret.</summary>
    public string ResolveModel(string providerName) =>
        ProviderSettings.GetModel(_config, _options, providerName);
}

public record PreparedTurn(
    ChatConversation Conversation,
    IChatProvider Provider,
    ChatRequest Request);

public class ChatService
{
    public const string SystemPrompt =
        "You are UniSphere Assistant, an academic helper for university students. " +
        "Answer in the user's language (Arabic/English/Egyptian dialect), be accurate, " +
        "explain step by step, say when unsure, don't invent facts, " +
        "don't do graded assignments in full (guide instead), refuse harmful content.";

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "how", "what", "why", "when", "with", "from",
        "that", "this", "please", "explain", "help", "about", "does", "can", "you",
        "ما", "هل", "كيف", "في", "من", "على", "ان", "إلى", "الى", "عن", "هو", "هي",
        "التي", "الذي", "لماذا", "متى", "أين", "اين"
    };

    private readonly AppDbContext _db;
    private readonly ChatProviderRegistry _registry;
    private readonly ChatOptions _options;
    private readonly IUniversityScope _scope;
    private readonly RagChatProvider _ragProvider;
    private readonly RagOptions _ragOptions;

    public ChatService(
        AppDbContext db, ChatProviderRegistry registry, IOptions<ChatOptions> options,
        IUniversityScope scope, RagChatProvider ragProvider, IOptions<RagOptions> ragOptions)
    {
        _db = db;
        _registry = registry;
        _options = options.Value;
        _scope = scope;
        _ragProvider = ragProvider;
        _ragOptions = ragOptions.Value;
    }

    public IReadOnlyList<Dtos.ChatProviderDto> GetProviders() => _registry.Available();

    public Task<IReadOnlyList<Dtos.ChatProviderDto>> GetProvidersAsync(CancellationToken ct = default) =>
        _registry.AvailableAsync(ct);

    public Task<IReadOnlyList<Dtos.ChatProviderStatusDto>> GetProviderStatusesAsync(CancellationToken ct = default) =>
        _registry.DescribeAsync(ct);

    public async Task<PreparedTurn> PrepareTurnAsync(
        int userId, string conversationId, string providerName, string content,
        bool regenerate, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        // Only the Student AI Assistant's RAG provider is routed through the
        // RAG service. Graduates, doctors and admins keep the existing provider
        // behaviour, and a student who explicitly picks another provider keeps
        // that choice instead of being silently redirected.
        var useRag = user?.Role == UserRole.Student
            && string.Equals(providerName, "rag", StringComparison.OrdinalIgnoreCase)
            && _ragOptions.Enabled
            && _ragProvider.IsConfigured;

        IChatProvider provider;
        string model;
        if (useRag)
        {
            provider = _ragProvider;
            model = _ragProvider.ModelName;
        }
        else
        {
            var (found, resolved) = await _registry.TryGetAsync(providerName, ct);
            if (!found || resolved is null)
                throw new ChatProviderException($"The AI provider '{providerName}' is not available right now.");
            provider = resolved;
            model = _registry.ResolveModel(provider.Name);
        }

        var today = DateTime.UtcNow.Date;
        var usage = await _db.ChatUsages
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Date == today, ct);
        if (usage is not null && usage.Count >= _options.DailyMessageLimit)
            throw new ChatLimitExceededException(
                $"Daily message limit reached ({_options.DailyMessageLimit}/day). Try again tomorrow.");

        ChatConversation conversation;
        if (string.Equals(conversationId?.Trim(), "new", StringComparison.OrdinalIgnoreCase))
        {
            var title = content.Trim();
            if (title.Length > 40) title = title[..40].TrimEnd() + "…";
            conversation = new ChatConversation
            {
                UserId = userId,
                Title = title,
                Provider = provider.Name
            };
            _db.ChatConversations.Add(conversation);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (!int.TryParse(conversationId, out var id)) throw new KeyNotFoundException();
            conversation = await _db.ChatConversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct)
                ?? throw new KeyNotFoundException();
            conversation.Provider = provider.Name;
        }

        if (!regenerate)
        {
            _db.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = content,
                Provider = provider.Name,
                Model = model
            });
            conversation.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var history = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.Id)
            .Take(10)
            .ToListAsync(ct);
        history.Reverse();

        if (regenerate)
        {
            // Regeneration replaces the last assistant answer: don't feed it
            // back to the provider as context (the previous user turn is
            // already the last remaining message).
            var lastAssistant = history.FindLastIndex(m => m.Role == "assistant");
            if (lastAssistant >= 0) history.RemoveAt(lastAssistant);
        }

        var isGraduate = user?.Role == UserRole.Graduate;
        var context = "";
        var ragDocuments = new List<RagDocument>();
        if (user is not null && useRag)
        {
            ragDocuments = await BuildStudentRagDocumentsAsync(user, content, ct);
        }
        else if (user is not null)
        {
            context = isGraduate
                ? await BuildGraduateContextAsync(user, content, ct)
                : await BuildStudentContextAsync(user, content, ct);
        }
        var systemPrompt = isGraduate ? GraduateSystemPrompt : SystemPrompt;

        var request = new ChatRequest
        {
            Provider = provider.Name,
            Model = model,
            SystemPrompt = string.IsNullOrEmpty(context) ? systemPrompt : systemPrompt + "\n\n" + context,
            Messages = history.Select(m => new ChatTurn { Role = m.Role, Content = m.Content }).ToList(),
            MaxTokens = _options.MaxTokens,
            RagDocuments = ragDocuments
        };
        return new PreparedTurn(conversation, provider, request);
    }

    public const string GraduateSystemPrompt =
        "You are UniSphere Graduate Coach, a career assistant for university graduates. " +
        "You know their profile, skills, CV highlights and job matches (provided as context when relevant). " +
        "Answer in the user's language (Arabic/English/Egyptian dialect). " +
        "Give concrete, actionable career advice (applications, interviews, skills, CVs). " +
        "Never invent job postings, employers or deadlines; say when unsure; refuse harmful content.";

    private async Task<string> BuildGraduateContextAsync(User user, string question, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("Graduate context: ").Append(user.FullName).Append(", department: ").Append(user.Department);
        if (user.UniversityId.HasValue)
        {
            var uni = await _db.Universities.AsNoTracking()
                .Where(u => u.Id == user.UniversityId.Value)
                .Select(u => u.Name).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(uni)) sb.Append(", university: ").Append(uni);
        }
        if (!string.IsNullOrWhiteSpace(user.GraduatedFrom)) sb.Append(", graduated from ").Append(user.GraduatedFrom);
        if (user.GraduationYear.HasValue) sb.Append(" (").Append(user.GraduationYear.Value).Append(')');
        sb.AppendLine(".");

        var profile = await _db.GraduateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is not null)
        {
            if (!string.IsNullOrWhiteSpace(profile.Headline)) sb.AppendLine("Headline: " + profile.Headline + ".");
            if (!string.IsNullOrWhiteSpace(profile.PreferredJobTitles))
                sb.AppendLine("Target job titles: " + profile.PreferredJobTitles + ".");
            if (!string.IsNullOrWhiteSpace(profile.PreferredLocations))
                sb.AppendLine("Preferred locations: " + profile.PreferredLocations + ".");
            if (!string.IsNullOrWhiteSpace(profile.WorkModes))
                sb.AppendLine("Work modes: " + profile.WorkModes.Replace(',', ',') + ".");
            if (!string.IsNullOrWhiteSpace(profile.CareerGoal))
                sb.AppendLine("Career goal: " + profile.CareerGoal + ".");
        }

        var skills = await _db.GraduateSkills.AsNoTracking()
            .Where(s => s.UserId == user.Id).OrderBy(s => s.Name)
            .Select(s => s.Name).ToListAsync(ct);
        if (skills.Count > 0)
            sb.AppendLine("Skills: " + string.Join(", ", skills) + ".");

        var latestCv = await _db.CvDocuments.AsNoTracking()
            .Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.UploadedAtUtc).FirstOrDefaultAsync(ct);
        var cvText = latestCv is null
            ? null
            : latestCv.ManualText is { Length: > 0 } m ? m : latestCv.ExtractedText;
        if (!string.IsNullOrEmpty(cvText))
        {
            var snippet = cvText.Length > 600 ? cvText[..600] : cvText;
            sb.AppendLine("CV excerpt: " + snippet + "…");
        }

        var saved = await _db.SavedJobs.AsNoTracking()
            .Include(s => s.JobListing)
            .Where(s => s.UserId == user.Id).Take(5).ToListAsync(ct);
        if (saved.Count > 0)
            sb.AppendLine("Saved jobs: " + string.Join("; ",
                saved.Where(s => s.JobListing is not null)
                    .Select(s => s.JobListing!.Title + " at " + s.JobListing!.CompanyName)) + ".");
        var applications = await _db.JobApplications.AsNoTracking()
            .Where(a => a.UserId == user.Id).Take(5).ToListAsync(ct);
        if (applications.Count > 0)
            sb.AppendLine("Applications: " + string.Join("; ",
                applications.Select(a => a.JobTitle + " at " + a.CompanyName + " (" + a.Status + ")")) + ".");

        return sb.ToString();
    }

    /// <summary>Persists the assistant turn when the stream ends. On
    /// <paramref name="replaceLastAssistant"/> (regenerate) the previous
    /// assistant answer is overwritten instead of adding a duplicate.
    /// Partial text (user pressed Stop) is saved the same way. Every query
    /// is scoped to <paramref name="userId"/>; foreign conversations are
    /// silently ignored (the controller already returned 404 earlier).</summary>
    public async Task CompleteAssistantMessageAsync(
        int userId, int conversationId, string providerName, string model,
        string fullText, bool replaceLastAssistant, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fullText)) return;

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (conversation is null) return;

        var history = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync(ct);
        var promptChars = history.Sum(m => m.Content.Length) + SystemPrompt.Length;
        var tokensIn = Math.Max(1, promptChars / 4);
        var tokensOut = Math.Max(1, fullText.Length / 4);

        if (replaceLastAssistant)
        {
            var lastAssistant = await _db.ChatMessages
                .Where(m => m.ConversationId == conversationId && m.Role == "assistant")
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);
            if (lastAssistant is not null)
            {
                lastAssistant.Content = fullText;
                lastAssistant.Provider = providerName;
                lastAssistant.Model = model;
                lastAssistant.TokensIn = tokensIn;
                lastAssistant.TokensOut = tokensOut;
                lastAssistant.CreatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _db.ChatMessages.Add(new ChatMessage
                {
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = fullText,
                    Provider = providerName,
                    Model = model,
                    TokensIn = tokensIn,
                    TokensOut = tokensOut
                });
            }
        }
        else
        {
            _db.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = fullText,
                Provider = providerName,
                Model = model,
                TokensIn = tokensIn,
                TokensOut = tokensOut
            });
        }

        var today = DateTime.UtcNow.Date;
        var usage = await _db.ChatUsages.FirstOrDefaultAsync(u => u.UserId == userId && u.Date == today, ct);
        if (usage is null)
            _db.ChatUsages.Add(new ChatUsage { UserId = userId, Date = today, Count = 1 });
        else
            usage.Count++;

        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Role-specific grounding. Doctors only ever get their own
    /// assigned courses and materials; everyone else stays inside their own
    /// university and eligible courses.</summary>
    private Task<string> BuildUserContextAsync(User user, string question, CancellationToken ct) =>
        user.Role == UserRole.Doctor
            ? BuildDoctorContextAsync(user, question, ct)
            : BuildStudentContextAsync(user, question, ct);

    /// <summary>Doctor context: only the courses assigned to this doctor
    /// (CourseInstructor) inside the doctor's own university.</summary>
    private async Task<string> BuildDoctorContextAsync(User user, string question, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var scope = await _scope.GetAsync(user.Id);
        var uni = scope.UniversityId.HasValue
            ? await _db.Universities.AsNoTracking()
                .Where(u => u.Id == scope.UniversityId.Value)
                .Select(u => u.Name).FirstOrDefaultAsync(ct)
            : null;
        sb.Append("Doctor context: ").Append(user.FullName);
        if (!string.IsNullOrWhiteSpace(user.Specialization))
            sb.Append(", specialization: ").Append(user.Specialization);
        if (!string.IsNullOrWhiteSpace(uni)) sb.Append(", university: ").Append(uni);
        sb.AppendLine(".");

        var assignedIds = await _db.CourseInstructors.AsNoTracking()
            .Where(ci => ci.DoctorUserId == user.Id)
            .Select(ci => ci.CourseId)
            .Distinct()
            .ToListAsync(ct);

        var courses = assignedIds.Count == 0 ? new List<Course>() :
            await _db.Courses.AsNoTracking()
                .VisibleToCurrentUser(scope)
                .Where(c => assignedIds.Contains(c.Id))
                .ToListAsync(ct);
        if (courses.Count > 0)
            sb.AppendLine("Assigned courses: " + string.Join(", ", courses.Select(c => $"{c.Code} {c.Title}")) + ".");

        var keywords = Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(w => w.Length > 2 && !Stopwords.Contains(w))
            .Distinct()
            .Take(12)
            .ToList();

        if (keywords.Count > 0 && courses.Count > 0)
        {
            var scopedIds = courses.Select(c => c.Id).ToList();
            var materials = await _db.CourseMaterials.AsNoTracking()
                .Include(m => m.Course)
                .Where(m => scopedIds.Contains(m.CourseId))
                .ToListAsync(ct);

            var scored = materials
                .Select(m => (Material: m, Score: ScoreMaterial(m, keywords)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(5)
                .ToList();

            foreach (var (material, _) in scored)
            {
                var snippet = (material.Content ?? "").Trim();
                if (snippet.Length > 400) snippet = snippet[..400].TrimEnd() + "…";
                sb.AppendLine($"Reference [{material.Course?.Title} — {material.Title}]: {snippet}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Builds the authorised document set for the student RAG turn.
    /// Only courses visible to this student and materials they are eligible to
    /// read are sent; the RAG service never receives another student's data.</summary>
    private async Task<List<RagDocument>> BuildStudentRagDocumentsAsync(
        User user, string question, CancellationToken ct)
    {
        var scope = await _scope.GetAsync(user.Id);
        if (!scope.UniversityId.HasValue)
            return new List<RagDocument>();

        var scopedIds = await _db.Courses.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .EligibleForStudent(user)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (scopedIds.Count == 0)
            return new List<RagDocument>();

        var materials = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .VisibleToCurrentUser(scope)
            .Where(m => scopedIds.Contains(m.CourseId)
                        && !string.IsNullOrWhiteSpace(m.Content))
            .ToListAsync(ct);
        if (materials.Count == 0)
            return new List<RagDocument>();

        var keywords = Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(w => w.Length > 2 && !Stopwords.Contains(w))
            .Distinct()
            .Take(12)
            .ToList();

        var selected = keywords.Count == 0
            ? materials
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(8)
                .Select(m => (Material: m, Score: 0))
                .ToList()
            : materials
                .Select(m => (Material: m, Score: ScoreMaterial(m, keywords)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Material.CreatedAtUtc)
                .Take(8)
                .ToList();

        if (selected.Count == 0)
        {
            selected = materials
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(5)
                .Select(m => (Material: m, Score: 0))
                .ToList();
        }

        var documents = new List<RagDocument>();
        var totalChars = 0;
        foreach (var (material, _) in selected)
        {
            var text = (material.Content ?? "").Trim();
            if (text.Length > 6000) text = text[..6000].TrimEnd() + "…";
            if (totalChars + text.Length > 30_000) break;
            totalChars += text.Length;

            var course = material.Course;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["courseId"] = material.CourseId.ToString(),
                ["courseCode"] = course?.Code ?? "",
                ["courseTitle"] = course?.Title ?? "",
                ["materialId"] = material.Id.ToString(),
                ["materialType"] = material.Type
            };

            documents.Add(new RagDocument(
                material.Id.ToString(),
                course is null ? material.Title : $"{course.Code} — {material.Title}",
                text,
                metadata));
        }

        return documents;
    }

    private async Task<string> BuildStudentContextAsync(User user, string question, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var scope = await _scope.GetAsync(user.Id);
        var uni = scope.UniversityId.HasValue
            ? await _db.Universities.AsNoTracking()
                .Where(u => u.Id == scope.UniversityId.Value)
                .Select(u => u.Name).FirstOrDefaultAsync(ct)
            : null;
        sb.Append("Student context: ").Append(user.FullName).Append(", ").Append(user.Role);
        if (!string.IsNullOrWhiteSpace(uni)) sb.Append(", university: ").Append(uni);
        if (!string.IsNullOrWhiteSpace(user.Department)) sb.Append(", department: ").Append(user.Department);
        if (user.AcademicYear.HasValue) sb.Append(", year ").Append(user.AcademicYear.Value);
        sb.AppendLine(".");

        var enrolled = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Course)
            .Where(e => e.UserId == user.Id
                && scope.UniversityId.HasValue
                && e.Course!.UniversityId == scope.UniversityId)
            .Select(e => e.Course!.Title)
            .ToListAsync(ct);
        if (enrolled.Count > 0)
            sb.AppendLine("Enrolled courses: " + string.Join(", ", enrolled) + ".");

        var keywords = Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(w => w.Length > 2 && !Stopwords.Contains(w))
            .Distinct()
            .Take(12)
            .ToList();

        if (keywords.Count > 0 && scope.UniversityId.HasValue)
        {
            var scopedIds = await _db.Courses.AsNoTracking()
                .VisibleToCurrentUser(scope)
                .EligibleForStudent(user)
                .Select(c => c.Id).ToListAsync(ct);
            var materials = await _db.CourseMaterials.AsNoTracking()
                .Include(m => m.Course)
                .VisibleToCurrentUser(scope)
                .Where(m => scopedIds.Contains(m.CourseId))
                .ToListAsync(ct);

            var scored = materials
                .Select(m => (Material: m, Score: ScoreMaterial(m, keywords)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(5)
                .ToList();

            foreach (var (material, _) in scored)
            {
                var snippet = (material.Content ?? "").Trim();
                if (snippet.Length > 400) snippet = snippet[..400].TrimEnd() + "…";
                sb.AppendLine($"Reference [{material.Course?.Title} — {material.Title}]: {snippet}");
            }
        }

        return sb.ToString();
    }

    private static int ScoreMaterial(CourseMaterial material, List<string> keywords)
    {
        var title = material.Title.ToLowerInvariant();
        var description = (material.Description ?? "").ToLowerInvariant();
        var content = (material.Content ?? "").ToLowerInvariant();
        var score = 0;
        foreach (var keyword in keywords)
        {
            if (title.Contains(keyword, StringComparison.Ordinal)) score += 3;
            if (description.Contains(keyword, StringComparison.Ordinal)) score += 2;
            if (content.Contains(keyword, StringComparison.Ordinal)) score += 1;
        }
        return score;
    }
}
