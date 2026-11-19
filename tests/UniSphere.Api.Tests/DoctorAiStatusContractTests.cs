using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>Guards the doctor AI status contract: /api/doctor/ai/status must
/// return Available(boolean), Providers as provider OBJECTS (name/displayName/
/// model) and Message(string) — the exact shape Pages/DoctorAiAssistant.razor
/// binds. A provider-objects-into-strings mismatch previously made the page's
/// status binding throw and flip the "No AI provider" banner on permanently.</summary>
public class DoctorAiStatusContractTests : IClassFixture<RagApiFactory>
{
    private readonly RagApiFactory _factory;

    public DoctorAiStatusContractTests(RagApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Status_ReturnsProviderObjects_MessageAndAvailability()
    {
        var response = await _factory.DoctorClient.GetAsync("/api/doctor/ai/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(doc);
        var root = doc!.RootElement;
        Assert.Equal(JsonValueKind.True, root.GetProperty("available").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("providers").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("message").ValueKind);

        foreach (var provider in root.GetProperty("providers").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, provider.ValueKind);
            Assert.Equal(JsonValueKind.String, provider.GetProperty("name").ValueKind);
            Assert.Equal(JsonValueKind.String, provider.GetProperty("displayName").ValueKind);
            Assert.Equal(JsonValueKind.String, provider.GetProperty("model").ValueKind);
        }
    }
}
