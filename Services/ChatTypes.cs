namespace UniSphere.Api.Services;

/// <summary>Non-secret chat settings (appsettings.json). API keys come only
/// from environment variables / user-secrets: OPENAI_API_KEY, ANTHROPIC_API_KEY, GEMINI_API_KEY, OPENROUTER_API_KEY.</summary>
public class ChatOptions
{
    public Dictionary<string, ChatProviderOptions> Providers { get; set; } = new();
    public int MaxTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 60;
    public int DailyMessageLimit { get; set; } = 100;
    public int PerMinuteLimit { get; set; } = 20;
}

public class ChatProviderOptions
{
    public string Model { get; set; } = "";
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
