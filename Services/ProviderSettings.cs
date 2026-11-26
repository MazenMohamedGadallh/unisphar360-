namespace UniSphere.Api.Services;

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

    public static string ApiKeyKeyName(string providerName) =>
        providerName.ToUpperInvariant() + "_API_KEY";

    public static string ProviderKey(string providerName, string leaf) =>
        $"Chat:Providers:{providerName}:{leaf}";

    public static string SectionKeyName(string providerName, string leaf) =>
        $"{providerName.ToUpperInvariant()}:{leaf}";

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

    public static readonly string[] PreferenceOrder = ["gemini", "openrouter", "openai", "anthropic", "ollama"];

    private static bool LooksPlaceholder(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower is "changeme" or "replace-me" or "your-api-key" or "your_openrouter_api_key"
            || lower.Contains("placeholder")
            || lower.StartsWith("<")
            || lower.EndsWith(">");
    }

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
            "GEMINI" => "gemini-flash-lite-latest",
            "OLLAMA" => "llama3.2",
            "OPENROUTER" => "openrouter/free",
            _ => "gpt-4o-mini",
        };
}
