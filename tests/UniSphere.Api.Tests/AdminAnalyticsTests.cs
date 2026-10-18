using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace UniSphere.Api.Tests;

public class AdminAnalyticsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AdminAnalyticsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Analytics_Me_ReturnsAnalysis()
    {
        var response = await _factory.StudentClient.GetAsync("/api/analytics/me/performance");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Analysis>();
        Assert.NotNull(body);
        Assert.Contains(body!.RiskLevel, new[] { "OnTrack", "NeedsAttention", "AtRisk" });
        Assert.InRange(body.Score, 0, 100);
        Assert.InRange(body.PredictedGpa, 0, 4.0);
        Assert.NotEmpty(body.Factors);
        Assert.NotEmpty(body.CourseRisks);
        Assert.NotEmpty(body.Recommendations);
    }

    [Fact]
    public async Task Analytics_Me_Anonymous_Unauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/analytics/me/performance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Overview_AdminOk_StudentForbidden()
    {
        var admin = await _factory.AdminClient.GetAsync("/api/admin/overview");
        admin.EnsureSuccessStatusCode();
        var overview = await admin.Content.ReadFromJsonAsync<Overview>();
        Assert.NotNull(overview);
        Assert.True(overview!.TotalStudents > 0);
        Assert.True(overview.CoursesCount > 0);

        var student = await _factory.StudentClient.GetAsync("/api/admin/overview");
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);
    }

    [Fact]
    public async Task Admin_UsersSearch_Paged()
    {
        var response = await _factory.AdminClient.GetAsync("/api/admin/users/search?page=1&pageSize=2");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<UserPage>();
        Assert.NotNull(page);
        Assert.True(page!.Total >= 4);
        Assert.True(page.Items.Count <= 2);
    }

    [Fact]
    public async Task Admin_SetStatus_Self_BadRequest()
    {
        // Admin user has Id 2 in a fresh seed.
        var response = await _factory.AdminClient.PatchAsJsonAsync("/api/admin/users/2/status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_SetRole_Invalid_BadRequest()
    {
        var response = await _factory.AdminClient.PatchAsJsonAsync("/api/admin/users/1/role",
            new { role = "Superuser" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Delete_Self_BadRequest()
    {
        var response = await _factory.AdminClient.DeleteAsync("/api/admin/users/2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_RiskDepartmentsAuditHealth_Ok()
    {
        var client = _factory.AdminClient;

        var risk = await client.GetAsync("/api/admin/analytics/at-risk-students");
        risk.EnsureSuccessStatusCode();

        var depts = await client.GetAsync("/api/admin/analytics/departments");
        depts.EnsureSuccessStatusCode();
        var deptList = await depts.Content.ReadFromJsonAsync<List<Department>>();
        Assert.NotNull(deptList);
        Assert.NotEmpty(deptList!);

        var audit = await client.GetAsync("/api/admin/audit-log?page=1&pageSize=5");
        audit.EnsureSuccessStatusCode();

        var health = await client.GetAsync("/api/admin/system-health");
        health.EnsureSuccessStatusCode();
        var body = await health.Content.ReadFromJsonAsync<Health>();
        Assert.Equal("Healthy", body!.Status);
    }

    private record Analysis(
        string RiskLevel, double PredictedGpa, double Confidence, double Score,
        List<string> Strengths, List<string> Weaknesses, List<string> Recommendations,
        List<CourseRisk> CourseRisks, List<Factor> Factors);
    private record CourseRisk(string Course, string RiskLevel, string Reason);
    private record Factor([property: JsonPropertyName("factor")] string Name, double Weight, double Value, double Contribution, string Description);
    private record Overview(
        int TotalStudents, int TotalDoctors, int TotalGraduates, int TotalAdmins,
        int NewRegistrations7d, int NewRegistrations30d, int ActiveUsersToday,
        List<DailyCount> LoginsPerDay, int OnTrackStudents, int NeedsAttentionStudents,
        int AtRiskStudents, double AverageGpa, double AverageAttendance, int CoursesCount);
    private record DailyCount(DateTime Date, int Count);
    private record UserPage(List<AdminUser> Items, int Total, int Page, int PageSize);
    private record AdminUser(
        int Id, string FullName, string Email, string Role, int RoleId,
        bool IsActive, bool IsDeleted, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, string Major);
    private record Department([property: JsonPropertyName("department")] string Name, int Students, double AverageGpa, double AverageAttendance);
    private record Health(string Status, string Database, double UptimeSeconds, string Version, DateTime ServerTimeUtc);
}
