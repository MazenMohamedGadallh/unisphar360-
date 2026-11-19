namespace UniSphere.Api.Services;

/// <summary>Non-secret configuration for the doctor AI teaching assistant.
/// API keys come from the same environment variables/user-secrets as the
/// chat feature (OPENAI_API_KEY, ANTHROPIC_API_KEY, GEMINI_API_KEY,
/// OPENROUTER_API_KEY, OLLAMA_*), never from appsettings.</summary>
public class DoctorAiOptions
{
    /// <summary>Preferred provider name (openai/anthropic/gemini/ollama/openrouter).
    /// Empty = try providers in the built-in order and pick the first available.</summary>
    public string PreferredProvider { get; set; } = "";
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutSeconds { get; set; } = 90;
    /// <summary>Max characters of lecture text fed to the model for one request.</summary>
    public int MaxContextChars { get; set; } = 12000;
}

/// <summary>Raised when the AI feature cannot run because no provider is
/// configured/available. Maps to HTTP 503 with a clear, honest message.</summary>
public class AiUnavailableException : Exception
{
    public AiUnavailableException(string message) : base(message) { }
}

/// <summary>Raised when the provider returned output that does not match the
/// expected schema (e.g. unparseable quiz JSON). Maps to HTTP 502.</summary>
public class AiInvalidResponseException : Exception
{
    public AiInvalidResponseException(string message) : base(message) { }
}
