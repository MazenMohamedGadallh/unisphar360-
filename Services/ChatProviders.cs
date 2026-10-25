using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UniSphere.Api.Services;

public interface IChatProvider
{
    string Name { get; }
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken ct);
}

/// <summary>Implemented by every provider so the registry can hide
/// providers whose key is not configured (without crashing).</summary>
public interface IProviderStatus
{
    bool IsConfigured { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}

public abstract class StreamingChatProviderBase : IChatProvider, IProviderStatus
{
    protected HttpClient _http;
    private readonly ILogger _logger;

    protected StreamingChatProviderBase(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public abstract string Name { get; }
    public abstract bool IsConfigured { get; }

    /// <summary>Model of the in-flight request, for stream-error logs.
    /// One provider instance serves one request at a time.</summary>
    protected string CurrentModel { get; private set; } = "";

    public virtual Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(IsConfigured);

    protected abstract HttpRequestMessage BuildRequest(ChatRequest request);
    protected abstract IAsyncEnumerable<string> ReadTokensAsync(Stream stream, CancellationToken ct);

    /// <summary>Body/detail text for logs: truncated so a huge provider error
    /// can never flood the log file (never contains keys: only response bodies).</summary>
    protected static string Truncate(string? text, int max = 500)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length > max ? text[..max] : text;
    }

    protected static string MapStatusToMessage(int statusCode) => statusCode switch
    {
        401 or 403 => $"The AI provider rejected the API key (HTTP {statusCode}). Check this provider's key.",
        402 => "The AI provider account has no credits left (HTTP 402). Add credits or switch provider.",
        404 => "AI model not found (HTTP 404). Check the model name for this provider.",
        429 => "Rate limited by the AI provider (HTTP 429). Wait a moment and try again.",
        400 => "The AI provider rejected the request (HTTP 400).",
        _ when statusCode >= 500 => $"The AI provider is temporarily unavailable (HTTP {statusCode}). Try again later.",
        _ => $"The AI provider returned an unexpected error (HTTP {statusCode})."
    };

    /// <summary>Per-provider error mapping. Defaults to <see cref="MapStatusToMessage"/>. The
    /// server-side body is passed for providers that refine the message (e.g. invalid-key
    /// detection). Bodies are provider diagnostic output — never log or echo API keys.</summary>
    protected virtual string MapError(int statusCode, string body) => MapStatusToMessage(statusCode);

    /// <summary>Logs (Warning) and maps an error object received INSIDE a stream,
    /// e.g. OpenRouter's {"error":{"message":"...","code":429}}. Provider details
    /// are logged truncated server-side; the client only gets the safe mapping.</summary>
    protected string MapStreamError(string? model, JsonElement error)
    {
        string? message = null;
        int? code = null;
        if (error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                message = m.GetString();
            if (error.TryGetProperty("code", out var c))
            {
                if (c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var numeric))
                    code = numeric;
                else if (c.ValueKind == JsonValueKind.String && int.TryParse(c.GetString(), out var parsed))
                    code = parsed;
            }
        }
        else if (error.ValueKind == JsonValueKind.String)
        {
            message = error.GetString();
        }

        _logger.LogWarning("{Provider} ({Model}) stream error. Response body truncated: {Body}",
            Name, model ?? "", Truncate(message));
        return code is int status
            ? MapError(status, Truncate(message))
            : "The AI provider returned an error while answering. Please try again.";
    }

    public virtual async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        CurrentModel = request.Model;

        // Retry loop only establishes the response (no yields inside try:
        // C# forbids yield in a try block with a catch clause).
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            HttpResponseMessage? attemptResponse = null;
            bool shouldRetry;
            ChatProviderException? fatal;
            try
            {
                using var req = BuildRequest(request);
                attemptResponse = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (attemptResponse.IsSuccessStatusCode)
                {
                    response = attemptResponse;
                    attemptResponse = null;
                    fatal = null;
                    shouldRetry = false;
                    break;
                }

                // Failed response: read body for server-side diagnostics only.
                var status = (int)attemptResponse.StatusCode;
                string body = "";
                try { body = await attemptResponse.Content.ReadAsStringAsync(ct); } catch { /* best effort */ }
                attemptResponse.Dispose();
                attemptResponse = null;

                // Never log keys or full URLs (old Gemini code put the key in the URL).
                _logger.LogWarning("{Provider} ({Model}) request failed: HTTP {Status}. Response body truncated: {Body}",
                    Name, request.Model, status, Truncate(body));

                var retryable = status == 429 || status >= 500;
                if (retryable && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt * attempt), ct);
                    shouldRetry = true;
                    fatal = null;
                }
                else
                {
                    // 4xx (except 429) are NOT retried.
                    shouldRetry = false;
                    fatal = new ChatProviderException(MapError(status, Truncate(body)));
                }
            }
            catch (OperationCanceledException)
            {
                attemptResponse?.Dispose();
                throw;
            }
            catch (ChatProviderException)
            {
                attemptResponse?.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TimeoutException)
            {
                // Network errors only: retry.
                attemptResponse?.Dispose();
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(ex, "{Provider} ({Model}) transient network error, retrying ({Attempt}/{Max})",
                        Name, request.Model, attempt, maxAttempts);
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt * attempt), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    continue;
                }
                _logger.LogWarning(ex, "{Provider} ({Model}) request failed after retries", Name, request.Model);
                throw new ChatProviderException("The AI provider is temporarily unavailable. Please try again.");
            }
            catch (Exception ex)
            {
                attemptResponse?.Dispose();
                _logger.LogWarning(ex, "{Provider} ({Model}) request failed", Name, request.Model);
                throw new ChatProviderException("The AI provider is temporarily unavailable. Please try again.");
            }

            if (fatal is not null) throw fatal;
            if (!shouldRetry) break;
        }

        if (response is null)
        {
            _logger.LogWarning("{Provider} ({Model}) request failed after retries", Name, request.Model);
            throw new ChatProviderException("The AI provider is temporarily unavailable. Please try again.");
        }

        // Stream tokens outside the retry try/catch (mid-stream errors are not retried).
        using (response)
        await using (var stream = await response.Content.ReadAsStreamAsync(ct))
        {
            await foreach (var token in ReadTokensAsync(stream, ct).WithCancellation(ct))
                yield return token;
        }
    }

    protected static async IAsyncEnumerable<string> ReadSseLinesAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        string? currentEvent = null;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            // SSE comments are keep-alives (OpenRouter sends
            // ": OPENROUTER PROCESSING" while a slow model is thinking):
            // they carry no data and must never reach the JSON parser.
            if (line.StartsWith(':')) continue;
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].Trim();
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            yield return currentEvent + "\n" + line["data:".Length..].Trim();
        }
    }

    /// <summary>Shared token parser for OpenAI-compatible chat/completions SSE
    /// (OpenAI, OpenRouter, and Ollama's /v1 endpoint). Yields only
    /// delta.content text (reasoning fields are ignored), stops at [DONE],
    /// skips malformed lines, and surfaces in-stream {"error":{...}} objects
    /// (e.g. OpenRouter 429) through <see cref="MapStreamError"/>.</summary>
    protected async IAsyncEnumerable<string> ReadOpenAiCompatibleTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in ReadSseLinesAsync(stream, ct).WithCancellation(ct))
        {
            var payload = frame[(frame.IndexOf('\n') + 1)..];
            if (payload == "[DONE]") yield break;
            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    // Only delta.content is answer text (reasoning fields ignored).
                    if (choice.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                        text = content.GetString();
                    if (choice.TryGetProperty("error", out var err))
                        throw new ChatProviderException(MapStreamError(CurrentModel, err));
                }
                else if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    throw new ChatProviderException(MapStreamError(CurrentModel, err));
                }
            }
            catch (ChatProviderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Provider}: skipping malformed stream line", Name);
                continue;
            }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }
}

public class OpenAiChatProvider : StreamingChatProviderBase
{
    private readonly IConfiguration _config;
    private readonly ChatOptions _options;

    public OpenAiChatProvider(
        HttpClient http, IConfiguration config, IOptions<ChatOptions> options)
        : base(http, NullLogger<OpenAiChatProvider>.Instance)
    {
        _config = config;
        _options = options.Value;
    }

    public override string Name => "openai";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(ProviderSettings.GetApiKey(_config, Name));

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        // Priority: Chat:Providers:OpenAI:ApiKey (appsettings.Local.json) → OPENAI_API_KEY (env/user-secrets/secret.json).
        var key = ProviderSettings.GetApiKey(_config, Name);
        if (string.IsNullOrWhiteSpace(key))
            throw new ChatProviderException(
                "The AI provider 'OpenAI' is not configured right now (set OPENAI_API_KEY or Chat:Providers:OpenAI:ApiKey).");
        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = JsonContent.Create(new
        {
            model = request.Model,
            messages,
            max_tokens = request.MaxTokens,
            stream = true,
            stream_options = new { include_usage = true }
        });
        return req;
    }

    protected override async IAsyncEnumerable<string> ReadTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var token in ReadOpenAiCompatibleTokensAsync(stream, ct).WithCancellation(ct))
            yield return token;
    }
}

public class OpenRouterChatProvider : StreamingChatProviderBase
{
    private readonly IConfiguration _config;

    public OpenRouterChatProvider(
        HttpClient http, IConfiguration config)
        : base(http, NullLogger<OpenRouterChatProvider>.Instance)
    {
        _config = config;
    }

    public override string Name => "openrouter";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(ProviderSettings.GetApiKey(_config, Name));

    protected override string MapError(int statusCode, string body) => statusCode switch
    {
        401 or 403 => "OpenRouter rejected the API key (HTTP 401/403). Check your OpenRouter key.",
        402 => "OpenRouter has no credits left (HTTP 402). Add credits or pick another provider.",
        404 => "AI model not found on OpenRouter (HTTP 404). Check the OpenRouter model name in settings.",
        429 => "OpenRouter rate limit reached (HTTP 429). Wait a moment and try again.",
        _ when statusCode >= 500 => $"OpenRouter is temporarily unavailable (HTTP {statusCode}). Try again later.",
        _ => MapStatusToMessage(statusCode)
    };

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        // Priority: Chat:Providers:OpenRouter:ApiKey (appsettings.Local.json) → OPENROUTER_API_KEY (env/user-secrets/secret.json).
        var key = ProviderSettings.GetApiKey(_config, Name);
        if (string.IsNullOrWhiteSpace(key))
            throw new ChatProviderException(
                "The AI provider 'OpenRouter' is not configured right now (set OPENROUTER_API_KEY or Chat:Providers:OpenRouter:ApiKey).");
        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost:5080");
        req.Headers.TryAddWithoutValidation("X-Title", "UniSphere 360+");
        req.Content = JsonContent.Create(new
        {
            model = request.Model,
            messages,
            max_tokens = request.MaxTokens,
            stream = true
        });
        return req;
    }

    protected override async IAsyncEnumerable<string> ReadTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        // OpenAI-compatible SSE; comment/keep-alive lines and reasoning
        // fields are handled inside the shared parser.
        await foreach (var token in ReadOpenAiCompatibleTokensAsync(stream, ct).WithCancellation(ct))
            yield return token;
    }
}

public class AnthropicChatProvider : StreamingChatProviderBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AnthropicChatProvider> _typedLogger;

    public AnthropicChatProvider(
        HttpClient http, IConfiguration config,
        ILogger<AnthropicChatProvider> logger) : base(http, logger)
    {
        _config = config;
        _typedLogger = logger;
    }

    public override string Name => "anthropic";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(ProviderSettings.GetApiKey(_config, Name));

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        // Priority: Chat:Providers:Anthropic:ApiKey → ANTHROPIC_API_KEY.
        var key = ProviderSettings.GetApiKey(_config, Name);
        if (string.IsNullOrWhiteSpace(key))
            throw new ChatProviderException(
                "The AI provider 'Claude' is not configured right now (set ANTHROPIC_API_KEY or Chat:Providers:Anthropic:ApiKey).");
        // Anthropic takes system separately and only user/assistant turns.
        var turns = request.Messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToList();

        var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = JsonContent.Create(new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            system = request.SystemPrompt,
            messages = turns,
            stream = true
        });
        return req;
    }

    protected override async IAsyncEnumerable<string> ReadTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in ReadSseLinesAsync(stream, ct).WithCancellation(ct))
        {
            var sep = frame.IndexOf('\n');
            var evt = frame[..sep];
            var payload = frame[(sep + 1)..];
            string? text = null;
            var failed = false;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (evt == "content_block_delta"
                    && doc.RootElement.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("type", out var kind)
                    && kind.GetString() == "text_delta"
                    && delta.TryGetProperty("text", out var textProp)
                    && textProp.ValueKind == JsonValueKind.String)
                {
                    text = textProp.GetString();
                }
                else if (evt == "error")
                {
                    throw new ChatProviderException("The AI provider returned an error. Please try again.");
                }
            }
            catch (ChatProviderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _typedLogger.LogWarning(ex, "Anthropic: skipping malformed stream line");
                failed = true;
            }
            if (failed) continue;
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }
}

public class GeminiChatProvider : StreamingChatProviderBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiChatProvider> _typedLogger;
    private static readonly JsonSerializerOptions NullIgnoringOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiChatProvider(
        HttpClient http, IConfiguration config,
        ILogger<GeminiChatProvider> logger) : base(http, logger)
    {
        _config = config;
        _typedLogger = logger;
    }

    public override string Name => "gemini";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(ProviderSettings.GetApiKey(_config, Name));

    /// <summary>Gemini signals an invalid/absent key with HTTP 400 + "API key not
    /// valid" in the body. Turn that into an actionable message (spell out the exact
    /// configuration path) instead of a vague "request rejected". The key itself is
    /// never included in any error output.</summary>
    protected override string MapError(int statusCode, string body) =>
        statusCode == 400 && (body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
            || body.Contains("API key not valid", StringComparison.OrdinalIgnoreCase))
            ? "Gemini rejected the API key (API_KEY_INVALID). Set GEMINI_API_KEY to a valid Gemini " +
              "API key from Google AI Studio (aistudio.google.com/apikey) via dotnet user-secrets, " +
              "the GEMINI_API_KEY environment variable, or secret.json, then restart the app."
            : MapStatusToMessage(statusCode);

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        // Priority: Chat:Providers:Gemini:ApiKey → GEMINI_API_KEY.
        var key = ProviderSettings.GetApiKey(_config, Name)
            ?? throw new ChatProviderException(
                "The AI provider 'Gemini' is not configured right now (set GEMINI_API_KEY or Chat:Providers:Gemini:ApiKey).");

        // streamGenerateContent SSE endpoint. Key goes ONLY in the header, never the URL.
        var req = new HttpRequestMessage(
            HttpMethod.Post, $"models/{request.Model}:streamGenerateContent?alt=sse");
        req.Headers.Add("x-goog-api-key", key);

        var contents = request.Messages.Select(m => new
        {
            role = m.Role == "assistant" ? "model" : "user",
            parts = new[] { new { text = m.Content } }
        }).ToList();

        object? systemInstruction = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? null
            : new { parts = new[] { new { text = request.SystemPrompt } } };

        var body = new
        {
            systemInstruction,
            contents,
            generationConfig = new { maxOutputTokens = request.MaxTokens }
        };

        req.Content = JsonContent.Create(body, options: NullIgnoringOptions);
        return req;
    }

    protected override async IAsyncEnumerable<string> ReadTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in ReadSseLinesAsync(stream, ct).WithCancellation(ct))
        {
            var payload = frame[(frame.IndexOf('\n') + 1)..];
            if (payload == "[DONE]") yield break;
            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            // Skip thought parts (reasoning), keep only visible text.
                            if (part.TryGetProperty("thought", out var thought)
                                && thought.ValueKind == JsonValueKind.True)
                                continue;
                            if (part.TryGetProperty("text", out var textEl)
                                && textEl.ValueKind == JsonValueKind.String)
                                sb.Append(textEl.GetString());
                        }
                        var joined = sb.ToString();
                        if (!string.IsNullOrEmpty(joined)) text = joined;
                    }
                }
                else if (doc.RootElement.TryGetProperty("error", out _))
                {
                    throw new ChatProviderException("The AI provider returned an error. Please try again.");
                }
            }
            catch (ChatProviderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _typedLogger.LogWarning(ex, "Gemini: skipping malformed stream line");
                continue;
            }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }
}

public class OllamaChatProvider : StreamingChatProviderBase
{
    // Static so the 30s cache survives per-request DI scopes (registry is scoped).
    private static List<string>? _availableModels;
    private static DateTime _tagsCacheTime = DateTime.MinValue;
    private static string? _cachedRoot;
    private readonly TimeSpan _tagsCacheExpiry = TimeSpan.FromSeconds(30);
    private readonly IConfiguration _config;
    private readonly ChatOptions _options;
    private readonly ILogger<OllamaChatProvider> _typedLogger;

    public OllamaChatProvider(
        HttpClient http, IConfiguration config, IOptions<ChatOptions> options,
        ILogger<OllamaChatProvider> logger) : base(http, logger)
    {
        _config = config;
        _options = options.Value;
        _typedLogger = logger;
    }

    public override string Name => "ollama";

    /// <summary>Ollama server root. Priority: Chat:Providers:Ollama:BaseUrl →
    /// OLLAMA_BASE_URL → http://localhost:11434. A "/v1"-suffixed value is
    /// acceptable and is normalized away for the tags probe below.</summary>
    public string BaseUrl => ProviderSettings.GetBaseUrl(_config, Name, "http://localhost:11434");

    /// <summary>Server root without any trailing "/v1" segment, so both
    /// "{root}/v1/chat/completions" (chat) and "{root}/api/tags" (probe) stay
    /// correct regardless of how the BaseUrl was configured.</summary>
    public string ServerRoot
    {
        get
        {
            var root = BaseUrl.TrimEnd('/');
            return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root[..^3] : root;
        }
    }

    public string ConfiguredModel => ProviderSettings.GetModel(_config, _options, Name);

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(ConfiguredModel);

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            await GetAvailableModelsAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string UnavailabilityReason()
    {
        if (!IsConfigured) return "Ollama is not configured (missing OLLAMA_MODEL)";
        return $"Ollama is not reachable ({BaseUrl} — is Ollama running?)";
    }

    // Cache the /api/tags response (30s) to avoid hammering Ollama on every request.
    // Callers treat failure as "not available" (hidden from dropdown, no crash).
    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct)
    {
        if (_availableModels != null
            && _cachedRoot == ServerRoot
            && DateTime.UtcNow - _tagsCacheTime < _tagsCacheExpiry)
            return _availableModels;

        try
        {
            // Absolute URL against the server root so a BaseUrl containing
            // "/v1" cannot redirect the probe to the wrong path.
            using var response = await _http.GetAsync($"{ServerRoot}/api/tags", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var models = new List<string>();
                if (doc.RootElement.TryGetProperty("models", out var modelsElem))
                {
                    foreach (var m in modelsElem.EnumerateArray())
                    {
                        string? name = null;
                        if (m.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            name = nameEl.GetString();
                        if (!string.IsNullOrEmpty(name)) models.Add(name);
                    }
                }
                _availableModels = models;
                _tagsCacheTime = DateTime.UtcNow;
                // Key the cache on the server root: a BaseUrl config change
                // must not keep serving the previous server's model list.
                _cachedRoot = ServerRoot;
                return models;
            }
            // Non-success: treat as unavailable but cache empty briefly to avoid hammering.
            _typedLogger.LogWarning("Ollama: /api/tags returned HTTP {Status}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Debug only: connection-refused stack traces would spam startup logs.
            _typedLogger.LogDebug(ex, "Ollama: failed to fetch /api/tags");
            throw;
        }

        _availableModels = new List<string>();
        _tagsCacheTime = DateTime.UtcNow;
        _cachedRoot = ServerRoot;
        return _availableModels;
    }

    public override async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = ConfiguredModel;

        // Clear model name check: tell the user exactly how to fix it.
        try
        {
            var available = await GetAvailableModelsAsync(ct);
            if (available.Count > 0)
            {
                var wanted = (model ?? "").Split(':')[0];
                var found = available.Any(m =>
                    m.Equals(model, StringComparison.OrdinalIgnoreCase) ||
                    m.Split(':')[0].Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (!found)
                    throw new ChatProviderException($"Model not installed, run: ollama pull {model}");
            }
        }
        catch (ChatProviderException)
        {
            throw;
        }
        catch
        {
            // Probe failed (Ollama down): let the base attempt stream so the
            // user gets the standard "temporarily unavailable" mapping.
        }

        await foreach (var token in base.StreamAsync(request, ct).WithCancellation(ct))
            yield return token;
    }

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        var model = ProviderSettings.GetModel(_config, _options, Name);

        // OpenAI-compatible endpoint on the Ollama server: {root}/v1/chat/completions.
        // (If the configured BaseUrl already ends with "/v1", use
        // {root}/chat/completions so the path must not read "/v1/v1/...".
        var root = ServerRoot;
        var chatPath = BaseUrl.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{root}/chat/completions"
            : $"{root}/v1/chat/completions";
        var req = new HttpRequestMessage(HttpMethod.Post, chatPath);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Ollama servers normally need no Authorization; a hardened deployment
        // behind a proxy may — send Bearer ONLY when a key IS configured.
        var key = ProviderSettings.GetApiKey(_config, Name);
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        // OpenAI chat format: {model, messages: [{role, content}], stream}.
        // System prompt goes first as a system message.
        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var body = new
        {
            model = model,
            messages = messages,
            max_tokens = request.MaxTokens,
            stream = true
        };

        req.Content = JsonContent.Create(body);
        return req;
    }

    protected override async IAsyncEnumerable<string> ReadTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        // Ollama's /v1 endpoint speaks OpenAI-compatible SSE, so the shared
        // parser applies (NDJSON-only api/chat is no longer used).
        await foreach (var token in ReadOpenAiCompatibleTokensAsync(stream, ct).WithCancellation(ct))
            yield return token;
    }
}
