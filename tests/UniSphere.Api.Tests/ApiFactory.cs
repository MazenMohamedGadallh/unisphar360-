using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;

namespace UniSphere.Api.Tests;

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
        LoginAsync("doctor@unisphere.edu", "Doctor123!").GetAwaiter().GetResult());

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
