using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class HomeSearchTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HomeSearchTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Home_RequiresAuth()
    {
        var response = await _factory.CreateClient().GetAsync("/api/home");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Home_ReturnsModulesAndUpcomingEvents()
    {
        var client = _factory.StudentClient;

        var response = await client.GetAsync("/api/home");

        response.EnsureSuccessStatusCode();
        var home = await response.Content.ReadFromJsonAsync<Home>();
        Assert.NotNull(home);
        Assert.Equal("Mazen", home!.User.FirstName);
        Assert.True(home.UnreadNotifications > 0);
        Assert.True(home.Modules.Count >= 5);
        Assert.Contains(home.Modules, m => m.Key == "academic-advisor");
        Assert.NotEmpty(home.UpcomingEvents);   // seeder keeps these in the future
        Assert.Contains(home.UpcomingEvents, e => e.Title == "AI Workshop");
        Assert.StartsWith("Good ", home.Greeting);
        Assert.NotEmpty(home.Recommendation.Message);
    }

    [Fact]
    public async Task Modules_ReturnsAllSorted()
    {
        var response = await _factory.CreateClient().GetAsync("/api/modules");

        response.EnsureSuccessStatusCode();
        var modules = await response.Content.ReadFromJsonAsync<List<Module>>();
        Assert.NotNull(modules);
        Assert.Equal(5, modules!.Count);
        Assert.Equal("academic-advisor", modules[0].Key);  // SortOrder = 1
    }

    [Fact]
    public async Task Search_Anonymous_Unauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/search?q=machine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_ShortQuery_BadRequest()
    {
        var response = await _factory.StudentClient.GetAsync("/api/search?q=a");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_FindsGroupedCoursesAndMaterials()
    {
        var course = await _factory.StudentClient.GetAsync("/api/search?q=machine");
        course.EnsureSuccessStatusCode();
        var body = await course.Content.ReadFromJsonAsync<GroupedResult>();
        Assert.NotNull(body);
        var courses = body!.Groups.FirstOrDefault(g => g.Type == "course");
        Assert.NotNull(courses);
        Assert.Contains(courses!.Items, r => r.Title == "Machine Learning");

        var summary = await _factory.StudentClient.GetAsync("/api/search?q=summary");
        summary.EnsureSuccessStatusCode();
        var sbody = await summary.Content.ReadFromJsonAsync<GroupedResult>();
        var materials = sbody!.Groups.FirstOrDefault(g => g.Type == "material");
        Assert.NotNull(materials);
        Assert.NotEmpty(materials!.Items);
    }

    [Fact]
    public async Task Search_TypeFilter_LimitsGroups()
    {
        var response = await _factory.StudentClient.GetAsync("/api/search?q=summary&type=material");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GroupedResult>();

        Assert.NotNull(body);
        Assert.Single(body!.Groups);
        Assert.Equal("material", body.Groups[0].Type);
        Assert.NotEmpty(body.Groups[0].Items);
    }

    [Fact]
    public async Task Search_AdminSeesUsers_StudentDoesNot()
    {
        var admin = await _factory.AdminClient.GetAsync("/api/search?q=admin@unisphere");
        admin.EnsureSuccessStatusCode();
        var abody = await admin.Content.ReadFromJsonAsync<GroupedResult>();
        var users = abody!.Groups.FirstOrDefault(g => g.Type == "user");
        Assert.NotNull(users);
        Assert.Contains(users!.Items, u => u.Title == "UniSphere Admin");

        var student = await _factory.StudentClient.GetAsync("/api/search?q=admin@unisphere");
        student.EnsureSuccessStatusCode();
        var sbody = await student.Content.ReadFromJsonAsync<GroupedResult>();
        Assert.DoesNotContain(sbody!.Groups, g => g.Type == "user");
    }

    private record Home(User User, string Greeting, int UnreadNotifications,
        List<Module> Modules, List<Event> UpcomingEvents, Recommendation Recommendation);
    private record User(int Id, string FullName, string FirstName, string? AvatarUrl, string Major);
    private record Module(string Key, string Title, string Subtitle, string Icon, string BackgroundColor, string AccentColor);
    private record Event(int Id, string Title, string Location, DateTime StartsAtUtc, string? ImageUrl, string? Description);
    private record Recommendation(string Headline, string Message, string Route);
    private record SearchGroup(string Type, string Label, List<SearchItem> Items);
    private record SearchItem(int? Id, string Title, string Subtitle, string Route, string Icon);
    private record GroupedResult(List<SearchGroup> Groups);
}
