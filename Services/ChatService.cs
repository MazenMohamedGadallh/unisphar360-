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

    /// <summary>Providers in auto-selection order: Chat:DefaultProvider first (when
    /// set), then Gemini → OpenRouter → OpenAI → Claude → Ollama, then anything else.
    /// The first entry of every availability list is therefore the best choice.</summary>
    private IEnumerable<IChatProvider> Ordered()
    {
        var order = new List<string>();
        if (!string.IsNullOrWhiteSpace(_options.DefaultProvider)) order.Add(_options.DefaultProvider.Trim());
        order.AddRange(ProviderSettings.PreferenceOrder);
        int Rank(string name)
        {
            var i = order.FindIndex(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }
        return _all.Values.OrderBy(p => Rank(p.Name)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Live providers in failover order. <paramref name="preferred"/> (when
    /// live) goes first, then the auto-selection order.</summary>
    public async Task<IReadOnlyList<IChatProvider>> OrderedLiveAsync(string? preferred = null, CancellationToken ct = default)
    {
        var live = new List<IChatProvider>();
        foreach (var p in Ordered())
        {
            try
            {
                if (p is not IProviderStatus st || await st.IsAvailableAsync(ct)) live.Add(p);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat provider {Provider} availability check failed", p.Name);
            }
        }
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var first = live.FirstOrDefault(p => string.Equals(p.Name, preferred.Trim(), StringComparison.OrdinalIgnoreCase));
            if (first is not null) { live.Remove(first); live.Insert(0, first); }
        }
        return live;
    }

    /// <summary>Sync snapshot (IsConfigured only). Prefer <see cref="AvailableAsync"/>.</summary>
    public IReadOnlyList<Dtos.ChatProviderDto> Available() =>
        Ordered()
            .Where(p => p is not IProviderStatus status || status.IsConfigured)
            .Select(p => new Dtos.ChatProviderDto(
                p.Name,
                DisplayNames.TryGetValue(p.Name, out var display) ? display : p.Name,
                ResolveModel(p.Name)))
            .ToList();

    /// <summary>Live availability (probes Ollama, hides it when down). No keys logged.</summary>
    public async Task<IReadOnlyList<Dtos.ChatProviderDto>> AvailableAsync(CancellationToken ct = default)
    {
        var list = new List<Dtos.ChatProviderDto>();
        foreach (var p in Ordered())
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
        foreach (var p in Ordered())
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
        "don't do graded assignments in full (guide instead), and only decline genuinely harmful requests.";

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
    private readonly IAiGroundingService _grounding;

    public ChatService(
        AppDbContext db, ChatProviderRegistry registry, IOptions<ChatOptions> options,
        IUniversityScope scope, RagChatProvider ragProvider, IOptions<RagOptions> ragOptions,
        IAiGroundingService grounding)
    {
        _grounding = grounding;
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

        // The RAG provider is routed through the RAG service for every role that
        // has documents to ground on (AiGroundingService.BuildDocumentsAsync:
        // student/doctor course materials, graduate CV). Admins keep the normal
        // providers, and anyone who explicitly picks another provider keeps
        // that choice instead of being silently redirected.
        var useRag = user?.Role is UserRole.Student or UserRole.Doctor or UserRole.Graduate
            && string.Equals(providerName, "rag", StringComparison.OrdinalIgnoreCase)
            && _ragOptions.Enabled
            && _ragProvider.IsConfigured;

        IChatProvider provider;
        string model;
        var degradedFromRag = false;
        var wantsRag = string.Equals(providerName, "rag", StringComparison.OrdinalIgnoreCase);
        if (useRag && await _ragProvider.IsAvailableAsync(ct))
        {
            provider = _ragProvider;
            model = _ragProvider.ModelName;
        }
        else
        {
            // RAG is down / not selected-able, or the requested provider has no
            // key: never fail while any cloud provider is live. Order: the
            // requested one, Chat:DefaultProvider, then any other live provider.
            var (found, resolved) = wantsRag
                ? (false, (IChatProvider?)null)
                : await _registry.TryGetAsync(providerName, ct);
            if (!found || resolved is null)
            {
                var live = await _registry.AvailableAsync(ct);
                var pick = live.FirstOrDefault(p => string.Equals(p.Name, _options.DefaultProvider?.Trim(), StringComparison.OrdinalIgnoreCase))
                           ?? live.FirstOrDefault();
                if (pick is null)
                {
                    // Nothing live anywhere. Keep RAG (when usable) so the stream
                    // reports a clear, safe error event instead of an HTTP failure.
                    if (!useRag) throw new ChatProviderException(AiSetupHint.Message);
                    resolved = _ragProvider;
                    found = true;
                }
                else
                {
                    (found, resolved) = await _registry.TryGetAsync(pick.Name, ct);
                    if (!found || resolved is null) throw new ChatProviderException(AiSetupHint.Message);
                }
                degradedFromRag = (wantsRag || useRag) && !ReferenceEquals(resolved, _ragProvider);
            }
            provider = resolved;
            model = ReferenceEquals(provider, _ragProvider) ? _ragProvider.ModelName : _registry.ResolveModel(provider.Name);
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

        var context = "";
        var ragDocuments = new List<RagDocument>();
        if (user is not null && useRag && !degradedFromRag)
        {
            ragDocuments = await _grounding.BuildDocumentsAsync(user, content, ct);
            // Hybrid: the RAG provider also gets live platform data (it was
            // dropped before), and the Python service answers from general
            // knowledge when no passage matches instead of refusing.
            try { context = (await _grounding.BuildAsync(userId, content, ct, null, 3000)).Context; }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* optional */ }
        }
        else if (user is not null)
        {
            // Unified grounding for EVERY role: RAG retrieval when the service is
            // up, plus live UniSphere data (courses, grades, schedule, career
            // profile, analytics...). If RAG is down the database context alone
            // is passed to whichever LLM provider answers.
            var grounding = await _grounding.BuildAsync(userId, content, ct);
            context = grounding.Context;
        }
        var systemPrompt = user?.Role switch
        {
            UserRole.Graduate => GraduateSystemPrompt,
            UserRole.Doctor => DoctorSystemPrompt,
            UserRole.Admin => AdminSystemPrompt,
            _ => SystemPrompt
        };

        var request = new ChatRequest
        {
            Provider = provider.Name,
            Model = model,
            SystemPrompt = HybridGroundingPolicy.Apply(systemPrompt, context),
            Messages = history.Select(m => new ChatTurn { Role = m.Role, Content = m.Content }).ToList(),
            MaxTokens = _options.MaxTokens,
            RagDocuments = ragDocuments
        };
        return new PreparedTurn(conversation, provider, request);
    }

    public const string DoctorSystemPrompt =
        "You are UniSphere Teaching Assistant for university doctors/instructors. " +
        "You know their assigned courses, enrolled-student counts, exams and lecture materials (provided as context). " +
        "Answer in the user's language (Arabic/English/Egyptian dialect). Help plan lectures, explain concepts, " +
        "draft quizzes and exams from the provided material first, and use your own general knowledge when the material does not cover the question (say which is which). " +
        "Never invent course content or student data.";

    public const string AdminSystemPrompt =
        "You are UniSphere Admin Assistant. You see platform-wide analytics and announcements (provided as context). " +
        "Answer in the user's language (Arabic/English/Egyptian dialect). Summarise usage, spot problems and suggest " +
        "administrative actions. Quote only numbers present in the context; never invent statistics or user data.";

    public const string GraduateSystemPrompt =
        "You are UniSphere Graduate Coach, a career assistant for university graduates. " +
        "You know their profile, skills, CV highlights and job matches (provided as context when relevant). " +
        "Answer in the user's language (Arabic/English/Egyptian dialect). " +
        "Give concrete, actionable career advice (applications, interviews, skills, CVs). " +
        "Never invent job postings, employers or deadlines; say when unsure; refuse harmful content.";

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
}
