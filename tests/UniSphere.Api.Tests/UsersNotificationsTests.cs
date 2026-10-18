using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class UsersNotificationsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public UsersNotificationsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetOwnProfile_ReturnsUser()
    {
        var response = await _factory.StudentClient.GetAsync("/api/users/1");

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<User>();
        Assert.Equal("Mazen Mohamed", user!.FullName);
        Assert.Equal("Mazen", user.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(user.Major));
    }

    [Fact]
    public async Task GetOtherProfile_Forbidden()
    {
        var response = await _factory.StudentClient.GetAsync("/api/users/2");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOwnProfile_ReturnsUpdatedUser()
    {
        var response = await _factory.StudentClient.PutAsJsonAsync("/api/users/1",
            new { fullName = "Mazen Mohamed", major = "AI Engineering", avatarUrl = (string?)null });

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<User>();
        Assert.Equal("AI Engineering", user!.Major);
    }

    [Fact]
    public async Task UpdateProfile_MissingMajor_BadRequest()
    {
        var response = await _factory.StudentClient.PutAsJsonAsync("/api/users/1",
            new { fullName = "Mazen Mohamed", major = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ByRole_AdminAllowed_StudentForbidden()
    {
        var student = await _factory.StudentClient.GetAsync("/api/users/by-role/1");
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);

        var admin = await _factory.AdminClient.GetAsync("/api/users/by-role/4");
        admin.EnsureSuccessStatusCode();
        var admins = await admin.Content.ReadFromJsonAsync<List<AuthUser>>();
        Assert.Single(admins!);   // only the seeded admin
        Assert.Equal("admin@unisphere.edu", admins![0].Email);
    }

    [Fact]
    public async Task ByRole_UnknownRole_NotFound()
    {
        var response = await _factory.AdminClient.GetAsync("/api/users/by-role/99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UserRole_ReturnsRole_OrNotFound()
    {
        var ok = await _factory.AdminClient.GetAsync("/api/users/1/role");
        ok.EnsureSuccessStatusCode();
        var role = await ok.Content.ReadFromJsonAsync<Role>();
        Assert.Equal(1, role!.Id);
        Assert.Equal("Student", role.Name);

        var missing = await _factory.AdminClient.GetAsync("/api/users/9999/role");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Notifications_ListMarkReadMarkAllRead()
    {
        var client = _factory.StudentClient;

        var list = await client.GetAsync("/api/notifications");
        list.EnsureSuccessStatusCode();
        var notifications = await list.Content.ReadFromJsonAsync<List<Notification>>();
        Assert.NotEmpty(notifications!);
        var first = notifications!.First();

        var read = await client.PostAsync($"/api/notifications/{first.Id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        var after = await (await client.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<List<Notification>>();
        Assert.True(after!.First(n => n.Id == first.Id).IsRead);

        var all = await client.PostAsync("/api/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);

        var final = await (await client.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<List<Notification>>();
        Assert.All(final!, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_NotFound()
    {
        // Notification ids 1-3 belong to user 1; id 999 does not exist.
        var response = await _factory.StudentClient.PostAsync("/api/notifications/999/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record User(int Id, string FullName, string FirstName, string? AvatarUrl, string Major);
    private record AuthUser(int Id, string FullName, string Email, int RoleId, string Role, string Major, string? AvatarUrl);
    private record Role(int Id, string Name);
    private record Notification(int Id, string Title, string Body, DateTime CreatedAtUtc, bool IsRead);
}
