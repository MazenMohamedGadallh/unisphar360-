using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace UniSphere.Api.Services;

/// <summary>Server-side settings for the existing RAG service. The base URL is
/// non-secret configuration; an optional API key must come from environment
/// variables/user-secrets (RagApi:ApiKey), never from a committed file.</summary>
public class RagOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8000/";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    public int TopK { get; set; } = 4;
    public string DisplayName { get; set; } = "UniSphere RAG";
    public string ModelName { get; set; } = "rag-groq";
}

public sealed record RagDocument(
    string? Id,
    string Title,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record RagSource(
    string Title,
    string? Source,
    int? Page,
    string Snippet,
    double? Distance);

public sealed record RagAnswer(
    string Answer,
    IReadOnlyList<RagSource> Sources,
    bool Grounded,
    string Provider,
    string Model);

public sealed record RagRetrieval(
    string Context,
    IReadOnlyList<RagSource> Sources,
    string Provider,
    string EmbeddingModel);

public sealed class RagUnavailableException : Exception
{
    public RagUnavailableException(string message) : base(message) { }
}

public sealed class RagInvalidResponseException : Exception
{
    public RagInvalidResponseException(string message) : base(message) { }
}

public interface IRagClient
{
    bool IsConfigured { get; }
    string DisplayName { get; }
    string ModelName { get; }

    Task<RagAnswer> QueryAsync(
        string question,
        IReadOnlyList<RagDocument> documents,
        string? systemPrompt,
        CancellationToken ct);

    Task<RagRetrieval> RetrieveAsync(
        string query,
        IReadOnlyList<RagDocument> documents,
        int? topK,
        CancellationToken ct);

    Task<bool> IsHealthyAsync(CancellationToken ct);
}

/// <summary>Typed server-side client for the existing RAG HTTP adapter. It is
/// used only by the Student AI Assistant and the Doctor Exam Assistant.</summary>
public sealed class RagClient : IRagClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly RagOptions _options;
    private readonly ILogger<RagClient> _logger;

    public RagClient(HttpClient http, IOptions<RagOptions> options, ILogger<RagClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        _options.Enabled && _http.BaseAddress is not null;

    public string DisplayName => string.IsNullOrWhiteSpace(_options.DisplayName)
        ? "UniSphere RAG"
        : _options.DisplayName;

    public string ModelName => string.IsNullOrWhiteSpace(_options.ModelName)
        ? "rag-groq"
        : _options.ModelName;

    public async Task<RagAnswer> QueryAsync(
        string question,
        IReadOnlyList<RagDocument> documents,
        string? systemPrompt,
        CancellationToken ct)
    {
        var payload = new
        {
            question,
            documents = ToPayload(documents),
            top_k = Math.Clamp(_options.TopK, 1, 8),
            system_prompt = systemPrompt
        };

        var result = await PostAsync<RagAnswer>("query", payload, ct);
        if (string.IsNullOrWhiteSpace(result.Answer))
            throw new RagInvalidResponseException("The RAG service returned an empty answer.");
        return result;
    }

    public async Task<RagRetrieval> RetrieveAsync(
        string query,
        IReadOnlyList<RagDocument> documents,
        int? topK,
        CancellationToken ct)
    {
        var payload = new
        {
            query,
            documents = ToPayload(documents),
            top_k = Math.Clamp(topK ?? _options.TopK, 1, 8)
        };

        return await PostAsync<RagRetrieval>("retrieve", payload, ct);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        if (!IsConfigured) return false;
        try
        {
            using var response = await _http.GetAsync("health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<T> PostAsync<T>(string path, object payload, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new RagUnavailableException("The RAG service is not enabled or configured.");

        try
        {
            using var response = await _http.PostAsJsonAsync(path, payload, Json, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RAG request to {Path} failed with HTTP {Status}", path, (int)response.StatusCode);
                throw new RagUnavailableException(
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "The RAG service rejected the server credentials."
                        : "The RAG service is unavailable right now. Please try again later.");
            }

            var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return result ?? throw new RagInvalidResponseException(
                "The RAG service returned an invalid response.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new RagUnavailableException("The RAG service timed out. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RAG request to {Path} failed", path);
            throw new RagUnavailableException("The RAG service is unavailable right now. Please try again later.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "RAG request to {Path} returned invalid JSON", path);
            throw new RagInvalidResponseException("The RAG service returned an invalid response.");
        }
    }

    private static IReadOnlyList<object> ToPayload(IReadOnlyList<RagDocument> documents) =>
        documents
            .Where(d => !string.IsNullOrWhiteSpace(d.Text))
            .Select(d => (object)new
            {
                id = d.Id,
                title = d.Title,
                text = d.Text,
                metadata = d.Metadata
            })
            .ToList();
}
