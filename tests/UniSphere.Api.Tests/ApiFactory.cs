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
        // Ollama dresses up as "configured" via its built-in default model
        // (see ProviderSettings). This machine may run Ollama locally; aim
        // the test process' probe at a dead port so it reads as down.
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                // AI keys may exist in env vars / user-secrets on a dev machine
                // (added after those sources, so this override wins). Strip them
                // so provider-availability paths (Ai status -> 503) are
                // deterministic in tests and never hit a real provider.
                ["OPENAI_API_KEY"] = null,
                ["ANTHROPIC_API_KEY"] = null,
                ["GEMINI_API_KEY"] = null,
                ["OPENROUTER_API_KEY"] = null,
                ["OLLAMA_MODEL"] = null,
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

    public HttpClient AdminClient => AuthedClient(
        LoginAsync("admin@unisphere.edu", "Admin123!").GetAwaiter().GetResult());

    public HttpClient DoctorClient => AuthedClient(
        LoginAsync("doctor@unisphere.edu", "sasaeldoctor123!").GetAwaiter().GetResult());

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private record LoginResponse(string Token, DateTime ExpiresAtUtc, JsonElement User);
}
