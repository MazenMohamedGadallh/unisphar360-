using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class AcademicTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AcademicTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_ReturnsWholeScreen()
    {
        var client = _factory.StudentClient;

        var response = await client.GetAsync("/api/academic/dashboard");

        response.EnsureSuccessStatusCode();
        var d = await response.Content.ReadFromJsonAsync<Dashboard>();
        Assert.NotNull(d);

        // Greeting + user
        Assert.Equal("Mazen", d!.User.FirstName);
        Assert.StartsWith("Good ", d.Greeting);
        Assert.True(d.UnreadNotifications > 0);

        // Stats: seeded current semester
        Assert.Equal(3.16, d.Stats.Gpa);
        Assert.Equal(0.20, d.Stats.GpaDelta, 2); // 3.16 - 2.96 = 0.20
        Assert.Equal(4.0, d.Stats.MaxGpa);
        Assert.Equal(18, d.Stats.CompletedCourses);
        Assert.Equal(26, d.Stats.TotalCourses);
        Assert.Equal(92, d.Stats.AttendanceRate);
        Assert.Equal(5, d.Stats.AttendanceDelta);          // 92 - 87
        Assert.Equal(3, d.Stats.SubjectsToImprove); // CS210 (3.0), CS320 (2.3), CS120 (3.0)
        Assert.True(d.Stats.RecommendedCourses > 0);

        // Performance table: 6 seeded enrollments
        Assert.Equal(6, d.Performance.Count);
        var ml = d.Performance.First(r => r.Course == "Machine Learning");
        Assert.Equal("B+", ml.GradeLetter);
        Assert.Equal(3.3, ml.GradePoints);
        Assert.Equal(95, ml.Attendance);

        // Study planner: 7 days this week
        Assert.Equal(7, d.StudyPlanner.Days.Count);
        Assert.Equal(3, d.StudyPlanner.Days.First(x => x.Title == "Machine Learning").Hours);

        // Insights, recommendations, quick actions, activity
        Assert.NotEmpty(d.Insights);
        Assert.NotEmpty(d.RecommendedCourses);
        Assert.DoesNotContain(d.RecommendedCourses, c => c.Title == "Machine Learning"); // not enrolled
        Assert.Equal(4, d.QuickActions.Count);
        Assert.NotEmpty(d.RecentActivity);
    }

    [Fact]
    public async Task Stats_ReturnsSeededValues()
    {
        var response = await _factory.StudentClient.GetAsync("/api/academic/stats");

        response.EnsureSuccessStatusCode();
        var stats = await response.Content.ReadFromJsonAsync<Stats>();
        Assert.Equal(3.16, stats!.Gpa);
        Assert.Equal(3, stats.SubjectsToImprove);   // CS210 (3.0), CS320 (2.3), CS120 (3.0)
    }

    [Fact]
    public async Task Performance_ReturnsGradeRows()
    {
        var response = await _factory.StudentClient.GetAsync("/api/academic/performance");

        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<GradeRow>>();
        Assert.Equal(6, rows!.Count);
        Assert.Contains(rows, r => r.Course == "Database Systems" && r.GradeLetter == "A-");
    }

    [Fact]
    public async Task Insights_ReturnsCards()
    {
        var response = await _factory.StudentClient.GetAsync("/api/academic/insights");

        response.EnsureSuccessStatusCode();
        var insights = await response.Content.ReadFromJsonAsync<List<Insight>>();
        Assert.NotEmpty(insights!);   // weakest course (2.3) + attendance delta + recommendation
    }

    [Fact]
    public async Task QuickActions_ReturnsFourTiles()
    {
        var response = await _factory.StudentClient.GetAsync("/api/academic/quick-actions");

        response.EnsureSuccessStatusCode();
        var actions = await response.Content.ReadFromJsonAsync<List<QuickAction>>();
        Assert.Equal(4, actions!.Count);
        Assert.Contains(actions, a => a.Key == "check-grades");
    }

    private record Dashboard(User User, string Greeting, string Tagline, int UnreadNotifications,
        Stats Stats, List<GradeRow> Performance, Planner StudyPlanner,
        List<Insight> Insights, List<Course> RecommendedCourses,
        List<QuickAction> QuickActions, List<Activity> RecentActivity);
    private record User(int Id, string FullName, string FirstName, string? AvatarUrl, string Major);
    private record Stats(double Gpa, double GpaDelta, double MaxGpa,
        int CompletedCourses, int TotalCourses, int CompletedPercent,
        int AttendanceRate, int AttendanceDelta,
        int SubjectsToImprove, int RecommendedCourses);
    private record GradeRow(int CourseId, string Course, string GradeLetter,
        double GradePoints, string GradeDisplay, int Attendance, string Status);
    private record Planner(string WeekLabel, DateTime WeekStart, List<Day> Days);
    private record Day(int Id, DateTime Date, string DayLabel, int DayNumber,
        string Title, int Hours, string? Note, bool IsDone);
    private record Insight(string Kind, string Title, string Message, string? ActionLabel, string? ActionRoute);
    private record Course(int Id, string Code, string Title, string Description, string Track, int Credits);
    private record QuickAction(string Key, string Title, string Icon, string Route, string BackgroundColor, string AccentColor);
    private record Activity(int Id, string Kind, string Message, DateTime CreatedAtUtc, string TimeAgo);
}
