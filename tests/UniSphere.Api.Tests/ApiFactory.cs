using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;

namespace UniSphere.Api.Tests;

/// <summary>
/// Runs once when the test assembly loads, before any test server is built.
/// The host machine (or CI) may export personal AI keys; strip them in the
/// test process only so provider-availability checks (AI status -> 503 paths)
/// are deterministic and tests never make a real, billed provider call.
/// Separate dev-server processes on this machine are unaffected.
/// </summary>
internal static class TestEnvironmentSetup
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        foreach (var key in new[]
        {
            "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "GEMINI_API_KEY",
            "OPENROUTER_API_KEY", "OLLAMA_MODEL"
        })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
        // Ollama reads as "configured" via its built-in default model. This
        // machine may run Ollama locally; aim the test process' probe at a
        // dead port so it reads as down.
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", "http://127.0.0.1:1");
    }
}

/// <summary>
/// Spins the API up on an in-process test server with its own throw-away SQLite
/// file, so tests never touch the developer's unisphere-v6.db.
/// The app seeds demo data on startup, exactly like a real first run.
/// </summary>
public class ApiFactory : WebApplicationFactory<UniSphere.Api.Services.TokenService>, IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"unisphere-test-{Guid.NewGuid():N}.db");

    private readonly string _researchPath = Path.Combine(
        Path.GetTempPath(), $"unisphere-test-research-{Guid.NewGuid():N}");

    private readonly string _materialsPath = Path.Combine(
        Path.GetTempPath(), $"unisphere-test-materials-{Guid.NewGuid():N}");

    public string MaterialsPath => _materialsPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Research:StoragePath"] = _researchPath,
                ["Uploads:StoragePath"] = _materialsPath,
                // AI keys may exist on the dev machine (env vars, user-secrets,
                // secret.json, or appsettings.Local.json); null them so
                // provider-availability paths (AI status -> 503) stay
                // deterministic and never bill.
                ["OPENAI_API_KEY"] = null,
                ["ANTHROPIC_API_KEY"] = null,
                ["GEMINI_API_KEY"] = null,
                ["OPENROUTER_API_KEY"] = null,
                ["OLLAMA_MODEL"] = null,
                // The dev machine's user secrets also carry OLLAMA_BASE_URL
                // (a live local Ollama) and OPENROUTER_MODEL; pin a dead port
                // (NOT null — null would fall through to the real default
                // localhost:11434) so the probe can never reach a real server.
                ["OLLAMA_BASE_URL"] = "http://127.0.0.1:1",
                ["OPENROUTER_MODEL"] = null,
                ["Chat:DefaultProvider"] = null,
                ["Chat:Providers:OpenAI:ApiKey"] = null,
                ["Chat:Providers:Anthropic:ApiKey"] = null,
                ["Chat:Providers:Gemini:ApiKey"] = null,
                ["Chat:Providers:Ollama:ApiKey"] = null,
                ["Chat:Providers:OpenRouter:ApiKey"] = null,
                // RAG integration is opt-in for tests. The dedicated RAG test
                // factory enables it with a fake IRagClient; normal tests must
                // never call the developer's local RAG service.
                ["RagApi:Enabled"] = "false",
                ["RagApi:BaseUrl"] = null,
                ["RagApi:ApiKey"] = null,
            });
        });
    }

    /// <summary>Logs in with a demo account and returns the JWT.</summary>
    public async Task<string> LoginAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    public HttpClient AuthedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient StudentClient => AuthedClient(
        LoginAsync("mazenmohamed@unisphere.edu", "Password123!").GetAwaiter().GetResult());

    public HttpClient DoctorClient => AuthedClient(
        LoginAsync("doctor@unisphere.edu", "Doctor123!").GetAwaiter().GetResult());

    public HttpClient AdminClient => AuthedClient(
        LoginAsync("admin@unisphere.edu", "Admin123!").GetAwaiter().GetResult());

    public HttpClient GraduateClient => AuthedClient(
        LoginAsync("graduate@unisphere.edu", "Graduate123!").GetAwaiter().GetResult());

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { File.Delete(_dbPath); } catch { /* best effort */ }
            try
            {
                if (Directory.Exists(_researchPath))
                    Directory.Delete(_researchPath, recursive: true);
            }
            catch { /* best effort */ }
            try
            {
                if (Directory.Exists(_materialsPath))
                    Directory.Delete(_materialsPath, recursive: true);
            }
            catch { /* best effort */ }
        }
    }

    private record LoginResponse(string Token, DateTime ExpiresAtUtc, JsonElement User);
}
