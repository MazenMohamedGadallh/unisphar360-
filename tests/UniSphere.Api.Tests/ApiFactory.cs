using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;

namespace UniSphere.Api.Tests;

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
        // Never load the developer's real .env (it holds live API keys).
        Environment.SetEnvironmentVariable("UNISPHERE_SKIP_DOTENV", "1");
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", "http://127.0.0.1:1");
    }
}

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
                ["OPENAI_API_KEY"] = null,
                ["ANTHROPIC_API_KEY"] = null,
                ["GEMINI_API_KEY"] = null,
                ["OPENROUTER_API_KEY"] = null,
                ["OLLAMA_MODEL"] = null,
                ["OLLAMA_BASE_URL"] = "http://127.0.0.1:1",
                ["OPENROUTER_MODEL"] = null,
                ["Chat:DefaultProvider"] = null,
                ["Chat:Providers:OpenAI:ApiKey"] = null,
                ["Chat:Providers:Anthropic:ApiKey"] = null,
                ["Chat:Providers:Gemini:ApiKey"] = null,
                ["Chat:Providers:Ollama:ApiKey"] = null,
                ["Chat:Providers:OpenRouter:ApiKey"] = null,
                ["RagApi:Enabled"] = "false",
                ["RagApi:BaseUrl"] = null,
                ["RagApi:ApiKey"] = null,
            });
        });
    }

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
