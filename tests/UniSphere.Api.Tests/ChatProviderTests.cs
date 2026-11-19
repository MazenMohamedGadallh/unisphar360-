using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Unit coverage for the chat provider layer, using a fake HttpMessageHandler —
/// never a real or billed provider call. Covers provider selection/registration,
/// Authorization header rules, OpenAI-compatible SSE parsing, error mapping and
/// retry behavior, plus the appsettings.Local.json priority chain
/// (Chat:Providers:&lt;Name&gt;:ApiKey|BaseUrl vs the flat OPENAI_API_KEY-style keys).
/// </summary>
public class ChatProviderTests
{
    // ---------- fake ASP.NET provider transport ----------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _script = new();
        public List<HttpRequestMessage> Requests { get; } = new();
        /// <summary>Bodies captured at send time — the provider base class
        /// disposes request+content after the response, so tests must not
        /// read request content afterwards.</summary>
        public List<string> RequestBodies { get; } = new();

        public void Respond(HttpStatusCode status, string body) =>
            _script.Enqueue(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(ct));
            var next = _script.Count > 0 ? _script.Dequeue() : throw new InvalidOperationException("Script exhausted.");
            return next;
        }
    }

    private static IConfiguration Config(params (string Key, string? Value)[] entries)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in entries) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    /// <summary>OpenAI-compatible SSE body: one delta frame per text, then [DONE].</summary>
    private static string Sse(params string[] deltaTexts)
    {
        var sb = new StringBuilder();
        foreach (var t in deltaTexts)
            sb.Append("data: ").Append(JsonSerializer.Serialize(new
            {
                choices = new[] { new { delta = new { content = t } } }
            })).Append("\n\n");
        sb.Append("data: [DONE]\n\n");
        return sb.ToString();
    }

    private static ChatRequest Req(string model = "test-model") => new()
    {
        Provider = "test",
        Model = model,
        SystemPrompt = "sys",
        Messages = [new ChatTurn { Role = "user", Content = "hi" }],
        MaxTokens = 16
    };

    private static async Task<string> CollectAsync(IChatProvider provider, ChatRequest request)
    {
        var sb = new StringBuilder();
        await foreach (var token in provider.StreamAsync(request, default))
            sb.Append(token);
        return sb.ToString();
    }

    private static HttpRequestMessage SentChat(ScriptedHandler handler) =>
        handler.Requests.First(r => r.RequestUri!.ToString().Contains("chat/completions"));

    // ---------- OpenAI provider ----------

    [Fact]
    public async Task OpenAI_ParsesDeltas_StopsAtDone()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, Sse("Hello", " world"));
        var provider = new OpenAiChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Config(("OPENAI_API_KEY", "flat-key")),
            Options.Create(new ChatOptions()));

        var text = await CollectAsync(provider, Req());

        Assert.Equal("Hello world", text);
        var sent = SentChat(handler);
        Assert.Equal("Bearer flat-key", sent.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task OpenAI_SkipsMalformedLines()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK,
            "data: {not json}\n\n" +
            "data: {\"other\":1}\n\n" +
            Sse("valid"));

        var provider = new OpenAiChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Config(("OPENAI_API_KEY", "flat-key")),
            Options.Create(new ChatOptions()));

        var text = await CollectAsync(provider, Req());
        Assert.DoesNotContain("{not json}", text);
    }

    [Fact]
    public async Task OpenAI_UsesProviderPathKey_OverFlatKey()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, Sse("ok"));
        var cfg = Config(
            ("Chat:Providers:OpenAI:ApiKey", "local-key"),
            ("OPENAI_API_KEY", "flat-key"));
        var provider = new OpenAiChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            cfg, Options.Create(new ChatOptions()));

        await CollectAsync(provider, Req());

        Assert.Equal("Bearer local-key", SentChat(handler).Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task OpenAI_MissingKey_HidesProvider_AndFailsExplicitly()
    {
        var provider = new OpenAiChatProvider(
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Config(),
            Options.Create(new ChatOptions()));
        Assert.False(provider.IsConfigured);

        var ex = await Assert.ThrowsAsync<ChatProviderException>(
            () => CollectAsync(provider, Req()));
        Assert.Contains("OPENAI_API_KEY", ex.Message);
        Assert.Contains("Chat:Providers:OpenAI:ApiKey", ex.Message);
    }

    [Theory]
    [InlineData(401, "rejected the API key")]
    [InlineData(403, "rejected the API key")]
    [InlineData(402, "no credits")]
    [InlineData(404, "model not found")]
    public async Task ProviderErrorStatus_MapsToSafeMessage(int status, string expectedPhrase)
    {
        var handler = new ScriptedHandler();
        handler.Respond((HttpStatusCode)status, "{\"error\":\"x\"}");
        var provider = new OpenAiChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Config(("OPENAI_API_KEY", "k")),
            Options.Create(new ChatOptions()));

        var ex = await Assert.ThrowsAsync<ChatProviderException>(() => CollectAsync(provider, Req()));
        Assert.Contains(expectedPhrase, ex.Message);
        // The raw provider body must never reach the client message.
        Assert.DoesNotContain("\"x\"", ex.Message);
    }

    [Fact]
    public async Task ProviderRetries_429_ThenSurfacesRateLimitMessage()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.TooManyRequests, "{}");
        handler.Respond(HttpStatusCode.TooManyRequests, "{}");
        handler.Respond(HttpStatusCode.TooManyRequests, "{}");
        var provider = new OpenAiChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Config(("OPENAI_API_KEY", "k")),
            Options.Create(new ChatOptions()));

        var ex = await Assert.ThrowsAsync<ChatProviderException>(() => CollectAsync(provider, Req()));
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task ProviderRetries_Transient5xx_ThenSucceeds()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.BadGateway, "{}");
        handler.Respond(HttpStatusCode.InternalServerError, "{}");
        handler.Respond(HttpStatusCode.OK, Sse("after", " retry"));
        var provider = new OpenAiChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Config(("OPENAI_API_KEY", "k")),
            Options.Create(new ChatOptions()));

        var text = await CollectAsync(provider, Req());
        Assert.Equal("after retry", text);
        Assert.Equal(3, handler.Requests.Count);
    }

    // ---------- OpenRouter provider ----------

    [Fact]
    public async Task OpenRouter_SendsAuthAndOpenRouterHeaders()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, Sse("ok"));
        var provider = new OpenRouterChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") },
            Config(("OPENROUTER_API_KEY", "or-key")));

        await CollectAsync(provider, Req());

        var sent = SentChat(handler);
        Assert.Equal("Bearer or-key", sent.Headers.Authorization!.ToString());
        Assert.Equal("http://localhost:5080", sent.Headers.GetValues("HTTP-Referer").Single());
        Assert.Equal("UniSphere 360+", sent.Headers.GetValues("X-Title").Single());
    }

    [Fact]
    public async Task OpenRouter_InStreamError_MapsToSafeMessage()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK,
            "data: " + JsonSerializer.Serialize(new { error = new { code = 429, message = "limiter hit" } }) + "\n\n");
        var provider = new OpenRouterChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") },
            Config(("OPENROUTER_API_KEY", "k")));

        var ex = await Assert.ThrowsAsync<ChatProviderException>(() => CollectAsync(provider, Req()));
        Assert.Contains("429", ex.Message);
        Assert.DoesNotContain("limiter hit", ex.Message);
    }

    [Fact]
    public async Task OpenRouter_IncludesModelAndStreamBody()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, Sse("ok"));
        var provider = new OpenRouterChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") },
            Config(("OPENROUTER_API_KEY", "k")));

        await CollectAsync(provider, Req(model: "openai/gpt-4o-mini"));

        var body = handler.RequestBodies[0];
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("openai/gpt-4o-mini", doc.RootElement.GetProperty("model").GetString());
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(16, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    // ---------- Ollama provider ----------

    [Fact]
    public async Task Ollama_MissingModel_FailsWithPullHint()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, "{\"models\":[{\"name\":\"alpha-test-model\"}]}");
        var cfg = Config(
            ("Chat:Providers:Ollama:BaseUrl", "http://pullcheck.local:11434"),
            ("OLLAMA_MODEL", "zzz-missing"));
        var provider = new OllamaChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://pullcheck.local:11434/") },
            cfg, Options.Create(new ChatOptions()),
            NullLogger<OllamaChatProvider>.Instance);

        var ex = await Assert.ThrowsAsync<ChatProviderException>(() => CollectAsync(provider, Req()));
        Assert.Contains("ollama pull zzz-missing", ex.Message);
    }

    [Fact]
    public async Task Ollama_NoAuthorization_ByDefault_AndOpenAiCompatiblePath()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, "{\"models\":[]}"); // tags probe
        handler.Respond(HttpStatusCode.OK, Sse("from", " ollama"));
        var cfg = Config(
            ("Chat:Providers:Ollama:BaseUrl", "http://noauth.local:11434"),
            ("OLLAMA_MODEL", "llama3.2"));
        var provider = new OllamaChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://noauth.local:11434/") },
            cfg, Options.Create(new ChatOptions()),
            NullLogger<OllamaChatProvider>.Instance);

        await CollectAsync(provider, Req());

        var chat = handler.Requests
            .First(r => r.RequestUri!.ToString().Contains("chat/completions"));
        Assert.Contains("/v1/chat/completions", chat.RequestUri!.ToString());
        Assert.Null(chat.Headers.Authorization);
    }

    [Fact]
    public async Task Ollama_SendBearerOnlyWhenKeyConfigured()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, "{\"models\":[]}");
        handler.Respond(HttpStatusCode.OK, Sse("ok"));
        var cfg = Config(
            ("Chat:Providers:Ollama:BaseUrl", "http://bearer.local:11434"),
            ("OLLAMA_MODEL", "llama3.2"),
            ("Chat:Providers:Ollama:ApiKey", "hardened-ollama"));
        var provider = new OllamaChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://bearer.local:11434/") },
            cfg, Options.Create(new ChatOptions()),
            NullLogger<OllamaChatProvider>.Instance);

        await CollectAsync(provider, Req());

        var chat = handler.Requests
            .First(r => r.RequestUri!.ToString().Contains("chat/completions"));
        Assert.Equal("Bearer hardened-ollama", chat.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Ollama_BaseUrlWithV1Suffix_NormalizesPaths()
    {
        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, "{\"models\":[]}");
        handler.Respond(HttpStatusCode.OK, Sse("ok"));
        var cfg = Config(
            ("Chat:Providers:Ollama:BaseUrl", "http://remote:11434/v1"),
            ("OLLAMA_MODEL", "llama3.2"));
        var provider = new OllamaChatProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://remote:11434/v1/") },
            cfg, Options.Create(new ChatOptions()),
            NullLogger<OllamaChatProvider>.Instance);

        await CollectAsync(provider, Req());

        var chat = handler.Requests
            .First(r => r.RequestUri!.ToString().Contains("chat/completions"));
        // BaseUrl carried /v1 → chat posts /chat/completions, never /v1/v1/...
        Assert.EndsWith("/chat/completions", chat.RequestUri!.ToString());
        // The /api/tags probe must still hit the server root.
        var tags = handler.Requests.FirstOrDefault(r => r.RequestUri!.ToString().Contains("/api/tags"));
        Assert.NotNull(tags);
        Assert.StartsWith("http://remote:11434/api/tags", tags!.RequestUri!.ToString());
    }

    // ---------- provider registry ----------

    private sealed class StubProvider : IChatProvider, IProviderStatus
    {
        public StubProvider(string name, bool configured) { Name = name; IsConfigured = configured; }
        public string Name { get; }
        public bool IsConfigured { get; }
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(IsConfigured);
        private static async IAsyncEnumerable<string> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
        public IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken ct) => Empty();
    }

    [Fact]
    public async Task Registry_ListsOnlyConfigured_AndResolvesCaseInsensitive()
    {
        var registry = new ChatProviderRegistry(
            new IChatProvider[]
            {
                new StubProvider("openai", true),
                new StubProvider("openrouter", false),
            },
            Config(),
            Options.Create(new ChatOptions()),
            NullLogger<ChatProviderRegistry>.Instance);

        var available = registry.Available();
        Assert.Equal(new[] { "openai" }, available.Select(p => p.Name).ToArray());

        // Sync TryGet is a pure name lookup (case-insensitive) and does NOT
        // filter by availability — that is the availability-filtered
        // TryGetAsync's job.
        Assert.True(registry.TryGet("OpenAI", out var found));
        Assert.Equal("openai", found!.Name);
        Assert.False(registry.TryGet(null, out _));

        var (hit, hidden) = await registry.TryGetAsync("openrouter", default);
        Assert.False(hit);
        Assert.Null(hidden);

        var (visHit, visible) = await registry.TryGetAsync("OPENAI", default);
        Assert.True(visHit);
        Assert.Equal("openai", visible!.Name);
    }

    [Fact]
    public void Registry_DefaultProvider_ComesFromChatOptions()
    {
        var cfg = Config(("Chat:DefaultProvider", "openrouter"), ("Chat:Providers:Ollama:ApiKey", "x"));
        var options = cfg.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
        Assert.Equal("openrouter", options.DefaultProvider);
        // The per-provider ApiKey/BaseUrl projection parses too.
        options = Config(
            ("Chat:Providers:OpenAI:ApiKey", "k"),
            ("Chat:Providers:OpenAI:BaseUrl", "http://x/v1/"))
            .GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
        Assert.Equal("k", options.Providers["OpenAI"].ApiKey);
        Assert.Equal("http://x/v1/", options.Providers["OpenAI"].BaseUrl);
    }

    // ---------- provider settings priority chain ----------

    [Fact]
    public void GetBaseUrl_Section_Wins_Over_FlatName_Over_Default()
    {
        // GetBaseUrl returns the raw trimmed value; trailing-slash
        // normalization is the caller's job (Program.cs appends "/").
        Assert.Equal("http://section",
            ProviderSettings.GetBaseUrl(
                Config(("Chat:Providers:OpenRouter:BaseUrl", "http://section")),
                "openrouter", "https://openrouter.ai/api/v1/"));
        Assert.Equal("http://env",
            ProviderSettings.GetBaseUrl(
                Config(("OPENROUTER_BASE_URL", "http://env")),
                "openrouter", "https://openrouter.ai/api/v1/"));
        Assert.Equal("https://openrouter.ai/api/v1/",
            ProviderSettings.GetBaseUrl(
                Config(), "openrouter", "https://openrouter.ai/api/v1/"));
    }

    [Fact]
    public void GetApiKey_Section_Wins_Over_FlatName()
    {
        Assert.Equal("section",
            ProviderSettings.GetApiKey(
                Config(("Chat:Providers:OpenAI:ApiKey", "section"), ("OPENAI_API_KEY", "flat")), "OpenAI"));
        Assert.Equal("flat",
            ProviderSettings.GetApiKey(Config(("OPENAI_API_KEY", "flat")), "OpenAI"));
        Assert.True(string.IsNullOrWhiteSpace(
            ProviderSettings.GetApiKey(Config(), "OpenAI")));
    }

    // ---------- local user-secrets file shape (flat names) ----------

    /// <summary>The real dev secrets file carries exactly these flat names
    /// (dotnet user-secrets with id unisphere-360-plus-dev merges them into
    /// IConfiguration as root keys). This test proves the REAL provider
    /// classes resolve the actual secret names end-to-end, with placeholder
    /// values standing in for the real secrets.</summary>
    [Fact]
    public async Task UserSecrets_FlatNames_FlowThroughProvidersEndToEnd()
    {
        var cfg = Config(
            ("OPENROUTER_API_KEY", "test-key"),
            ("OPENROUTER_MODEL", "test-free-model"),
            ("GEMINI_API_KEY", "test-gemini"),
            ("OLLAMA_MODEL", "test-ollama"),
            ("OLLAMA_BASE_URL", "http://secrets-ollama:11434"));

        var handler = new ScriptedHandler();
        handler.Respond(HttpStatusCode.OK, Sse("ok"));
        var openRouter = new OpenRouterChatProvider(
            new HttpClient(handler)
            {
                // Same resolution as Program.cs: GetBaseUrl decides the address.
                BaseAddress = new Uri(ProviderSettings.GetBaseUrl(
                    cfg, "openrouter", "https://openrouter.ai/api/v1/").TrimEnd('/') + "/")
            },
            cfg);

        Assert.True(openRouter.IsConfigured);

        // Model resolution goes through the central resolver first (this is
        // exactly what registry.ResolveModel does upstream), then the feature
        // wrapper passes it into the ChatRequest the provider sends.
        var resolvedModel = ProviderSettings.GetModel(cfg, new ChatOptions(), "openrouter");
        Assert.Equal("test-free-model", resolvedModel);

        await CollectAsync(openRouter, Req(model: resolvedModel));

        var sent = SentChat(handler);
        Assert.Equal("Bearer test-key", sent.Headers.Authorization!.ToString());
        var body = handler.RequestBodies[0];
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("test-free-model", doc.RootElement.GetProperty("model").GetString());

        // The other keys in the same file light up their providers too.
        var gemini = new GeminiChatProvider(
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") },
            cfg, NullLogger<GeminiChatProvider>.Instance);
        Assert.True(gemini.IsConfigured);

        var ollama = new OllamaChatProvider(
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("http://secrets-ollama:11434/") },
            cfg, Options.Create(new ChatOptions()), NullLogger<OllamaChatProvider>.Instance);
        Assert.Equal("http://secrets-ollama:11434", ollama.BaseUrl.TrimEnd('/'));
        Assert.Equal("test-ollama", ollama.ConfiguredModel);
        Assert.True(ollama.IsConfigured);
    }

    [Fact]
    public void GetApiKey_HierarchicalSectionName_IsWeakestFallback()
    {
        // "OpenRouter:ApiKey"-style root sections resolve through the chain…
        Assert.Equal("hier",
            ProviderSettings.GetApiKey(Config(("OPENROUTER:ApiKey", "hier")), "OpenRouter"));
        // …but the flat user-secrets name still wins, matching today's file.
        Assert.Equal("flat",
            ProviderSettings.GetApiKey(
                Config(("OPENROUTER:ApiKey", "hier"), ("OPENROUTER_API_KEY", "flat")), "OpenRouter"));
        // And the per-provider path (appsettings.Local.json) beats both.
        Assert.Equal("local",
            ProviderSettings.GetApiKey(
                Config(("Chat:Providers:OpenRouter:ApiKey", "local"),
                    ("OPENROUTER:ApiKey", "hier"), ("OPENROUTER_API_KEY", "flat")), "OpenRouter"));
    }

    [Fact]
    public void GetBaseUrl_HierarchicalSectionName_IsWeakestFallback()
    {
        Assert.Equal("http://hier",
            ProviderSettings.GetBaseUrl(Config(("Gemini:BaseUrl", "http://hier")), "gemini", "https://default/"));
        Assert.Equal("http://flat",
            ProviderSettings.GetBaseUrl(
                Config(("Gemini:BaseUrl", "http://hier"), ("GEMINI_BASE_URL", "http://flat")), "gemini", "https://default/"));
        Assert.Equal("https://default/",
            ProviderSettings.GetBaseUrl(Config(), "gemini", "https://default/"));
    }

    [Fact]
    public void GetModel_HierarchicalSectionName_SitsBetweenSecretAndAppsettings()
    {
        var options = new ChatOptions { Providers = new() { ["OpenRouter"] = new ChatProviderOptions { Model = "appsettings-model" } } };

        // A hierarchical model entry (e.g. OpenRouter:Model in a local secret
        // file) overrides the committed appsettings model…
        Assert.Equal("hier-model",
            ProviderSettings.GetModel(Config(("OpenRouter:Model", "hier-model")), options, "openrouter"));
        // …but the flat user-secrets name remains strongest.
        Assert.Equal("flat-model",
            ProviderSettings.GetModel(
                Config(("OPENROUTER_MODEL", "flat-model"), ("OpenRouter:Model", "hier-model")), options, "openrouter"));
        // With no secret at all the committed appsettings model applies.
        Assert.Equal("appsettings-model",
            ProviderSettings.GetModel(Config(), options, "openrouter"));
    }

    [Fact]
    public async Task Gemini_MissingKey_HidesProvider_AndFailsExplicitly()
    {
        var provider = new GeminiChatProvider(
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") },
            Config(), NullLogger<GeminiChatProvider>.Instance);
        Assert.False(provider.IsConfigured);

        var ex = await Assert.ThrowsAsync<ChatProviderException>(
            () => CollectAsync(provider, Req()));
        Assert.Contains("GEMINI_API_KEY", ex.Message);
        Assert.Contains("Chat:Providers:Gemini:ApiKey", ex.Message);
    }
}

/// <summary>
/// Resolves every AI-related service through the real application's
/// dependency container (in-process host) to prove the shared AI
/// infrastructure is registered exactly once and injectable everywhere.
/// </summary>
public class AiDependencyResolutionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void EveryAiService_ResolvesThroughDI_WithSharedConfiguration()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Shared infrastructure
        Assert.NotNull(sp.GetRequiredService<ChatProviderRegistry>());
        Assert.NotNull(sp.GetRequiredService<ChatService>());

        // All five provider clients (typed HttpClient per provider)
        Assert.NotNull(sp.GetRequiredService<OpenAiChatProvider>());
        Assert.NotNull(sp.GetRequiredService<AnthropicChatProvider>());
        Assert.NotNull(sp.GetRequiredService<GeminiChatProvider>());
        Assert.NotNull(sp.GetRequiredService<OllamaChatProvider>());
        Assert.NotNull(sp.GetRequiredService<OpenRouterChatProvider>());

        // Registered exactly once each as IChatProvider
        Assert.Equal(5, sp.GetServices<IChatProvider>().Count());

        // Feature-level AI services that all funnel into the registry
        Assert.NotNull(sp.GetRequiredService<GraduateAiService>());
        Assert.NotNull(sp.GetRequiredService<IDoctorAiService>());
        Assert.NotNull(sp.GetRequiredService<IExamAssistantService>());
        Assert.NotNull(sp.GetRequiredService<IExamGeneratorService>());
    }
}
