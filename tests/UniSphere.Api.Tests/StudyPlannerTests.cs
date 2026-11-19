using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class StudyPlannerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StudyPlannerTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_ReturnsCurrentWeek()
    {
        var response = await _factory.StudentClient.GetAsync("/api/study-planner");

        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<Plan>();
        Assert.NotNull(plan);
        Assert.Equal(7, plan!.Days.Count);   // the seeder fills every day of the week
        Assert.Contains(plan.Days, d => d.Title == "Free Day" && d.Hours == 0);
    }

    [Fact]
    public async Task Get_WithWeekStartQuery_Works()
    {
        var response = await _factory.StudentClient
            .GetAsync("/api/study-planner?weekStart=2026-10-04");

        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<Plan>();
        Assert.NotNull(plan);
        Assert.Equal(new DateTime(2026, 10, 4), plan!.WeekStart);  // a Sunday
    }

    [Fact]
    public async Task Create_Toggle_Delete_Lifecycle()
    {
        var client = _factory.StudentClient;

        var created = await client.PostAsJsonAsync("/api/study-planner",
            new { date = "2026-10-15T00:00:00", title = "Review ML notes", hours = 2, note = "Chapter 3" });
        created.EnsureSuccessStatusCode();
        var item = await created.Content.ReadFromJsonAsync<Day>();
        Assert.False(item!.IsDone);
        Assert.Equal("Review ML notes", item.Title);

        var toggled = await client.PostAsync($"/api/study-planner/{item.Id}/toggle", null);
        toggled.EnsureSuccessStatusCode();
        var afterToggle = await toggled.Content.ReadFromJsonAsync<Day>();
        Assert.True(afterToggle!.IsDone);

        var deleted = await client.DeleteAsync($"/api/study-planner/{item.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var plan = await (await client.GetAsync("/api/study-planner?weekStart=2026-10-14"))
            .Content.ReadFromJsonAsync<Plan>();
        Assert.DoesNotContain(plan!.Days, d => d.Id == item.Id);
    }

    [Fact]
    public async Task Create_MissingTitle_BadRequest()
    {
        var response = await _factory.StudentClient.PostAsJsonAsync("/api/study-planner",
            new { date = "2026-10-15T00:00:00", title = "", hours = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_HoursOutOfRange_BadRequest()
    {
        var response = await _factory.StudentClient.PostAsJsonAsync("/api/study-planner",
            new { date = "2026-10-15T00:00:00", title = "Too much", hours = 13 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record Plan(string WeekLabel, DateTime WeekStart, List<Day> Days);
    private record Day(int Id, DateTime Date, string DayLabel, int DayNumber,
        string Title, int Hours, string? Note, bool IsDone);
}
