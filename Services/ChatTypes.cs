namespace UniSphere.Api.Services;

/// <summary>Non-secret chat settings (appsettings.json). API keys come only
/// from environment variables / user-secrets / the optional git-ignored
/// appsettings.Local.json — via the flat names (OPENAI_API_KEY...) or the
/// per-provider path Chat:Providers:<Provider>:ApiKey. Never from a committed file.</summary>
public class ChatOptions
{
    public Dictionary<string, ChatProviderOptions> Providers { get; set; } = new();

    /// <summary>Provider tried FIRST by every AI feature when it has no
    /// explicit per-request provider (chat UI: the user's own selection still
    /// wins). Empty = features fall back to their built-in orders.</summary>
    public string DefaultProvider { get; set; } = "";

    public int MaxTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 60;
    public int DailyMessageLimit { get; set; } = 100;
    public int PerMinuteLimit { get; set; } = 20;
}

public class ChatProviderOptions
{
    public string Model { get; set; } = "";

    /// <summary>Per-provider base URL override (e.g. Chat:Providers:Ollama:BaseUrl).
    /// Falls back to the flat &lt;NAME&gt;_BASE_URL key, then the hard-coded default.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Per-provider API key override (appsettings.Local.json only in
    /// practice). Falls back to the flat &lt;NAME&gt;_API_KEY key.</summary>
    public string ApiKey { get; set; } = "";
}

public class ChatTurn
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public class ChatRequest
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public List<ChatTurn> Messages { get; set; } = new();
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Authorised course documents for the RAG-backed student
    /// assistant. Other providers ignore this field.</summary>
    public List<RagDocument> RagDocuments { get; set; } = new();
}

/// <summary>Public-facing failure (safe to send to the client).
/// Provider internals are logged server-side, never forwarded.</summary>
public class ChatProviderException : Exception
{
    public ChatProviderException(string message) : base(message) { }
}

public class ChatLimitExceededException : Exception
{
    public ChatLimitExceededException(string message) : base(message) { }
}
