namespace UniSphere.Api.Services;

/// <summary>Single place that resolves a provider's model name (see
/// <see cref="GetModel"/> for the priority chain), API key (<see
/// cref="GetApiKey"/>) and base URL (<see cref="GetBaseUrl"/>) — the central
/// configuration resolver shared by every AI provider. Callers pass
/// IConfiguration so values are read at request time — after changing a
/// secret only a restart is needed. Never logs or returns API keys.</summary>
/// <summary>Single, exact "where do I paste my key" message shared by every AI feature.</summary>
public static class AiSetupHint
{
    public const string Message =
        "No AI provider is configured. Create a file named \".env\" in the project root " +
        "(next to UniSphere.Api.csproj; copy .env.example) and set OPENROUTER_API_KEY=<your key> " +
        "(or GEMINI_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY). Alternative: appsettings.Local.json -> " +
        "Chat:Providers:OpenRouter:ApiKey. Then restart the app.";
}

public static class ProviderSettings
{
    public static string ModelKey(string providerName) =>
        providerName.ToUpperInvariant() + "_MODEL";

    /// <summary>Flat (environment-style) API key name for a provider,
    /// e.g. OPENAI_API_KEY.</summary>
    public static string ApiKeyKeyName(string providerName) =>
        providerName.ToUpperInvariant() + "_API_KEY";

    /// <summary>Config path of a provider's settings section,
    /// e.g. "Chat:Providers:OpenAI:ApiKey". Configuration path lookups are
    /// case-insensitive, so callers may pass the lowercase provider name.</summary>
    public static string ProviderKey(string providerName, string leaf) =>
        $"Chat:Providers:{providerName}:{leaf}";

    /// <summary>Root-level hierarchical config name used by some local secret
    /// files, e.g. "OpenRouter:ApiKey" instead of the flat "OPENROUTER_API_KEY".
    /// Configuration indexers are case-insensitive, so any casing resolves.</summary>
    public static string SectionKeyName(string providerName, string leaf) =>
        $"{providerName.ToUpperInvariant()}:{leaf}";

    /// <summary>Resolved API key for a provider, or null/empty when none.
    /// Priority: Chat:Providers:<Provider>:ApiKey (appsettings.Local.json) →
    /// flat <PROVIDER>_API_KEY (dotnet user-secrets, environment variables,
    /// secret.json — the single local source of truth) → root-level
    /// hierarchical <Provider>:ApiKey fallback for other secret files.
    /// Read at call time.</summary>
    public static string? GetApiKey(IConfiguration config, string providerName)
    {
        var candidates = new[]
        {
            config[ProviderKey(providerName, "ApiKey")],
            config[ApiKeyKeyName(providerName)],
            config[SectionKeyName(providerName, "ApiKey")],
            Environment.GetEnvironmentVariable(ApiKeyKeyName(providerName))
        };

        foreach (var candidate in candidates)
        {
            var key = candidate?.Trim();
            if (string.IsNullOrWhiteSpace(key) || LooksPlaceholder(key)) continue;
            return key;
        }
        return null;
    }

    /// <summary>Auto-selection order when nothing is explicitly chosen: the
    /// free-tier cloud providers first, local Ollama last.</summary>
    public static readonly string[] PreferenceOrder = ["gemini", "openrouter", "openai", "anthropic", "ollama"];

    private static bool LooksPlaceholder(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower is "changeme" or "replace-me" or "your-api-key" or "your_openrouter_api_key"
            || lower.Contains("placeholder")
            || lower.StartsWith("<")
            || lower.EndsWith(">");
    }

    /// <summary>Resolved base URL (root form, best-effort trailing slash) for a
    /// provider. Priority: Chat:Providers:<Provider>:BaseUrl → flat
    /// <PROVIDER>_BASE_URL (dotnet user-secrets, environment variables,
    /// secret.json) → root-level hierarchical <Provider>:BaseUrl → the caller's
    /// hard-coded default. Read at call time so only a restart is needed after
    /// changing config.</summary>
    public static string GetBaseUrl(IConfiguration config, string providerName, string fallback)
    {
        var fromSection = config[ProviderKey(providerName, "BaseUrl")];
        if (!string.IsNullOrWhiteSpace(fromSection)) return fromSection.Trim();

        var flat = config[providerName.ToUpperInvariant() + "_BASE_URL"];
        if (!string.IsNullOrWhiteSpace(flat)) return flat.Trim();

        var hierarchical = config[SectionKeyName(providerName, "BaseUrl")];
        if (!string.IsNullOrWhiteSpace(hierarchical)) return hierarchical.Trim();

        return fallback;
    }

    /// <summary>Resolved model name for a provider. Priority: user-secrets /
    /// environment variable ("&lt;PROVIDER&gt;_MODEL") wins, then a root-level
    /// hierarchical "&lt;Provider&gt;:Model" fallback, then
    /// Chat:Providers:&lt;Provider&gt;:Model from appsettings.json, then a
    /// built-in default. Callers pass IConfiguration so the value is read
    /// at request time — after changing a secret only a restart is needed.
    /// Never logs or returns API keys (model names are not secrets).</summary>
    public static string GetModel(IConfiguration config, ChatOptions? options, string providerName)
    {
        var fromSecret = config[ModelKey(providerName)];
        if (!string.IsNullOrWhiteSpace(fromSecret)) return fromSecret.Trim();

        var hierarchical = config[SectionKeyName(providerName, "Model")];
        if (!string.IsNullOrWhiteSpace(hierarchical)) return hierarchical.Trim();

        if (options?.Providers is not null)
        {
            foreach (var kv in options.Providers)
            {
                if (string.Equals(kv.Key, providerName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(kv.Value.Model))
                    return kv.Value.Model.Trim();
            }
        }

        return BuiltInDefault(providerName);
    }

    private static string BuiltInDefault(string providerName) =>
        providerName.ToUpperInvariant() switch
        {
            "ANTHROPIC" => "claude-3-5-haiku-latest",
            // Alias that always points at the current free-tier Flash-Lite model: fast and reliable
            // under load (full Flash models are often overloaded) and immune to model retirement
            // (named models such as gemini-2.5-flash get retired for new API users).
            "GEMINI" => "gemini-flash-lite-latest",
            "OLLAMA" => "llama3.2",
            "OPENROUTER" => "openrouter/free",
            _ => "gpt-4o-mini",
        };
}
