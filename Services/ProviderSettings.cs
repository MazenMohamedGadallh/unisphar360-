namespace UniSphere.Api.Services;

/// <summary>Single place that resolves a provider's model name. Priority:
/// user-secrets / environment variable ("&lt;PROVIDER&gt;_MODEL", e.g. OPENROUTER_MODEL)
/// wins, then Chat:Providers:&lt;Provider&gt;:Model from appsettings.json,
/// then a built-in default. Callers pass IConfiguration so the value is read
/// at request time — after changing a secret only a restart is needed.
/// Never logs or returns API keys (model names are not secrets).</summary>
public static class ProviderSettings
{
    public static string ModelKey(string providerName) =>
        providerName.ToUpperInvariant() + "_MODEL";

    public static string GetModel(IConfiguration config, ChatOptions? options, string providerName)
    {
        var fromSecret = config[ModelKey(providerName)];
        if (!string.IsNullOrWhiteSpace(fromSecret)) return fromSecret.Trim();

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
            // gemini-2.x models stopped being available to new Gemini keys
            // (Google's own 404 message recommends the newer generations);
            // 3.5-flash-lite is the stable, high-availability default behind
            // the current free tier (verified live against a real key).
            "GEMINI" => "gemini-3.5-flash-lite",
            "OLLAMA" => "llama3.2",
            "OPENROUTER" => "openai/gpt-4o-mini",
            _ => "gpt-4o-mini",
        };
}
