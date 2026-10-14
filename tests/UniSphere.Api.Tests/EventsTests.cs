using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class EventsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EventsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAll_UpcomingOnly_HidesNothingSeeded()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events?upcoming=true");

        response.EnsureSuccessStatusCode();
        var events = await response.Content.ReadFromJsonAsync<List<Event>>();
        Assert.NotNull(events);
        Assert.Equal(3, events!.Count);
        Assert.All(events, e => Assert.True(e.StartsAtUtc >= DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public async Task GetById_ReturnsEvent()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/1");

        response.EnsureSuccessStatusCode();
        var e = await response.Content.ReadFromJsonAsync<Event>();
        Assert.Equal("AI Workshop", e!.Title);
    }

    [Fact]
    public async Task GetById_Missing_NotFound()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateUpdateDelete()
    {
        var admin = _factory.AdminClient;
        var newEvent = new
        {
            title = "Test Symposium",
            location = "Main Hall",
            startsAtUtc = "2026-12-01T09:00:00Z",
            imageUrl = (string?)null,
            description = "Created by tests"
        };

        var created = await admin.PostAsJsonAsync("/api/events", newEvent);
        created.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var e = await created.Content.ReadFromJsonAsync<Event>();

        var updated = await admin.PutAsJsonAsync($"/api/events/{e!.Id}",
            newEvent with { title = "Test Symposium (updated)" });
        updated.EnsureSuccessStatusCode();
        var after = await updated.Content.ReadFromJsonAsync<Event>();
        Assert.Equal("Test Symposium (updated)", after!.Title);

        var deleted = await admin.DeleteAsync($"/api/events/{e.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await admin.GetAsync($"/api/events/{e.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Student_CannotCreateEvent()
    {
        var response = await _factory.StudentClient.PostAsJsonAsync("/api/events",
            new { title = "Nope", location = "Nowhere", startsAtUtc = "2026-12-01T09:00:00Z" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private record Event(int Id, string Title, string Location, DateTime StartsAtUtc, string? ImageUrl, string? Description);
}
