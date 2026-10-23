using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public virtual Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(IsConfigured);

    protected abstract HttpRequestMessage BuildRequest(ChatRequest request);
    protected abstract IAsyncEnumerable<string> ReadTokensAsync(Stream stream, CancellationToken ct);

    protected static string MapStatusToMessage(int statusCode) => statusCode switch
    {
        401 or 403 => "The AI provider rejected the API key",
        404 => "AI model not found, check the model name in settings",
        429 => "Free quota reached, try again later",
        400 => "The AI provider rejected the request",
        _ => "The AI provider is temporarily unavailable"
    };

    /// <summary>Per-provider error mapping. Defaults to <see cref="MapStatusToMessage"/>. The
    /// server-side body is passed for providers that refine the message (e.g. invalid-key
    /// detection). Bodies are provider diagnostic output — never log or echo API keys.</summary>
    protected virtual string MapError(int statusCode, string body) => MapStatusToMessage(statusCode);

    public virtual async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
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
                var truncated = body.Length > 500 ? body[..500] : body;
                _logger.LogWarning("{Provider} request failed: HTTP {Status}. Body: {Body}", Name, status, truncated);

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
                    fatal = new ChatProviderException(MapError(status, truncated));
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
                    _logger.LogWarning(ex, "{Provider} transient network error, retrying ({Attempt})", Name, attempt);
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
                _logger.LogWarning(ex, "{Provider} request failed after retries", Name);
                throw new ChatProviderException("The AI provider is temporarily unavailable");
            }
            catch (Exception ex)
            {
                attemptResponse?.Dispose();
                _logger.LogWarning(ex, "{Provider} request failed", Name);
                throw new ChatProviderException("The AI provider is temporarily unavailable");
            }

            if (fatal is not null) throw fatal;
            if (!shouldRetry) break;
        }

        if (response is null)
        {
            _logger.LogWarning("{Provider} request failed after retries", Name);
            throw new ChatProviderException("The AI provider is temporarily unavailable");
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
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].Trim();
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            yield return currentEvent + "\n" + line["data:".Length..].Trim();
        }
    }
}

public class OpenAiChatProvider : StreamingChatProviderBase
{
    private readonly IConfiguration _config;
    private readonly ChatOptions _options;
    private readonly ILogger<OpenAiChatProvider> _typedLogger;

    public OpenAiChatProvider(
        HttpClient http, IConfiguration config, IOptions<ChatOptions> options,
        ILogger<OpenAiChatProvider> logger) : base(http, logger)
    {
        _config = config;
        _options = options.Value;
        _typedLogger = logger;
    }

    public override string Name => "openai";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_config["OPENAI_API_KEY"]);

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        var key = _config["OPENAI_API_KEY"]
            ?? throw new ChatProviderException("The AI provider 'OpenAI' is not configured right now.");
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
                    if (choice.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                        text = content.GetString();
                    if (choice.TryGetProperty("error", out var err))
                        throw new ChatProviderException("The AI provider returned an error. Please try again.");
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
                _typedLogger.LogWarning(ex, "OpenAI: skipping malformed stream line");
                continue;
            }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }
}

public class OpenRouterChatProvider : StreamingChatProviderBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<OpenRouterChatProvider> _typedLogger;

    public OpenRouterChatProvider(
        HttpClient http, IConfiguration config,
        ILogger<OpenRouterChatProvider> logger) : base(http, logger)
    {
        _config = config;
        _typedLogger = logger;
    }

    public override string Name => "openrouter";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_config["OPENROUTER_API_KEY"]);

    protected override string MapError(int statusCode, string body) => statusCode switch
    {
        401 or 403 => "The AI provider rejected the API key",
        402 => "The AI provider has run out of credits",
        404 => "AI model not found, check OPENROUTER_MODEL in settings",
        429 => "Free-tier rate limit reached, try again later",
        _ => "The AI provider is temporarily unavailable"
    };

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        var key = _config["OPENROUTER_API_KEY"]
            ?? throw new ChatProviderException("The AI provider 'OpenRouter' is not configured right now.");
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
        // OpenAI-compatible SSE. Comment/keep-alive lines (": ...") never reach
        // here: ReadSseLinesAsync only yields "data:" frames.
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
                    // Only delta.content is answer text; delta.reasoning is ignored.
                    if (choice.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                        text = content.GetString();
                    if (choice.TryGetProperty("error", out var err))
                        throw new ChatProviderException("The AI provider returned an error. Please try again.");
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
                _typedLogger.LogWarning(ex, "OpenRouter: skipping malformed stream line");
                continue;
            }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
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
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_config["ANTHROPIC_API_KEY"]);

    protected override HttpRequestMessage BuildRequest(ChatRequest request)
    {
        var key = _config["ANTHROPIC_API_KEY"]
            ?? throw new ChatProviderException("The AI provider 'Claude' is not configured right now.");
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
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_config["GEMINI_API_KEY"]);

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
        var key = _config["GEMINI_API_KEY"]
            ?? throw new ChatProviderException("The AI provider 'Gemini' is not configured right now.");

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

    public string BaseUrl => _config["OLLAMA_BASE_URL"] ?? "http://localhost:11434";

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
        if (_availableModels != null && DateTime.UtcNow - _tagsCacheTime < _tagsCacheExpiry)
            return _availableModels;

        try
        {
            using var response = await _http.GetAsync("api/tags", ct);
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
        return _availableModels;
    }

    public override async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = ProviderSettings.GetModel(_config, _options, Name);

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

        var req = new HttpRequestMessage(HttpMethod.Post, "api/chat");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Ollama chat format: {model, messages: [{role, content}], stream}.
        // System prompt goes first as a system message.
        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var body = new
        {
            model = model,
            messages = messages,
            stream = true
        };

        req.Content = JsonContent.Create(body);
        return req;
    }

    protected override async IAsyncEnumerable<string> ReadTokensAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        // Ollama streams NDJSON (one JSON object per line), NOT SSE.
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            string? text = null;
            var done = false;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("done", out var doneEl)
                    && doneEl.ValueKind == JsonValueKind.True)
                    done = true;
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    text = content.GetString();
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
                _typedLogger.LogWarning(ex, "Ollama: skipping malformed stream line");
                continue;
            }
            if (!string.IsNullOrEmpty(text)) yield return text;
            if (done) yield break;
        }
    }
}
