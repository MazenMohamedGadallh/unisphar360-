using System.Net;
using System.Net.Http.Json;
using UniSphere.Api.Tests;
using Xunit;

namespace UniSphere.Api.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object ValidStudent(string email, string role = "Student") => new
    {
        firstName = "New",
        middleName = (string?)null,
        lastName = "User",
        phoneNumber = "01012345678",
        email,
        gender = "Male",
        age = 20,
        role,
        password = "Password123!",
        confirmPassword = "Password123!",
        academicYear = 2,
        department = "Computer Science",
        specialization = (string?)null,
        graduatedFrom = (string?)null,
        graduationYear = (int?)null
    };

    [Fact]
    public async Task Register_Student_Succeeds_ReturnsToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            ValidStudent($"student-{Guid.NewGuid():N}@example.com"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Computer Science", body!.User.Major); // department feeds major
        Assert.Equal(1, body.User.RoleId);
        Assert.Equal("Student", body.User.Role);
        Assert.NotEmpty(body.Token);
    }

    [Fact]
    public async Task Register_Doctor_Succeeds()
    {
        var email = $"doc-{Guid.NewGuid():N}@example.com";
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "New", ["lastName"] = "Doctor",
            ["phoneNumber"] = "+201012345678", ["email"] = email,
            ["gender"] = "Female", ["age"] = 40, ["role"] = "Doctor",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!",
            ["specialization"] = "Artificial Intelligence", ["department"] = "Computer Science"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(3, body!.User.RoleId);
        Assert.Equal("Doctor", body.User.Role);
    }

    [Fact]
    public async Task Register_Graduate_Succeeds()
    {
        var email = $"grad-{Guid.NewGuid():N}@example.com";
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "New", ["lastName"] = "Grad",
            ["phoneNumber"] = "01512345678", ["email"] = email,
            ["gender"] = "Male", ["age"] = 24, ["role"] = "Graduate",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!",
            ["graduatedFrom"] = "Cairo University", ["graduationYear"] = 2022,
            ["department"] = "Engineering"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(2, body!.User.RoleId);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Conflicts()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";

        var first = await _client.PostAsJsonAsync("/api/auth/register", ValidStudent(email));
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync("/api/auth/register", ValidStudent(email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_AdminRole_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            ValidStudent($"admin-{Guid.NewGuid():N}@example.com", "Admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidRole_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            ValidStudent($"badrole-{Guid.NewGuid():N}@example.com", "Superuser"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_IsRejected()
    {
        var weak = new Dictionary<string, object?>
        {
            ["firstName"] = "Weak", ["lastName"] = "Pw",
            ["phoneNumber"] = "01012345678", ["email"] = $"weak-{Guid.NewGuid():N}@example.com",
            ["gender"] = "Male", ["age"] = 20, ["role"] = "Student",
            ["password"] = "abc", ["confirmPassword"] = "abc",
            ["academicYear"] = 1, ["department"] = "Business"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", weak);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_PasswordMismatch_IsRejected()
    {
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "Mis", ["lastName"] = "Match",
            ["phoneNumber"] = "01012345678", ["email"] = $"mm-{Guid.NewGuid():N}@example.com",
            ["gender"] = "Male", ["age"] = 20, ["role"] = "Student",
            ["password"] = "Password123!", ["confirmPassword"] = "Password999!",
            ["academicYear"] = 1, ["department"] = "Business"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_MissingStudentFields_IsRejected()
    {
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "No", ["lastName"] = "Year",
            ["phoneNumber"] = "01012345678", ["email"] = $"noyear-{Guid.NewGuid():N}@example.com",
            ["gender"] = "Female", ["age"] = 19, ["role"] = "Student",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_UnderageStudent_IsRejected()
    {
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "Young", ["lastName"] = "Student",
            ["phoneNumber"] = "01012345678", ["email"] = $"young-{Guid.NewGuid():N}@example.com",
            ["gender"] = "Male", ["age"] = 17, ["role"] = "Student",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!",
            ["academicYear"] = 1, ["department"] = "Business"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_UnderageDoctor_IsRejected()
    {
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "Young", ["lastName"] = "Doctor",
            ["phoneNumber"] = "01012345678", ["email"] = $"youngdoc-{Guid.NewGuid():N}@example.com",
            ["gender"] = "Female", ["age"] = 24, ["role"] = "Doctor",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!",
            ["specialization"] = "Medicine"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_MinAgeBoundary_Succeeds()
    {
        // Graduate minimum is 22: exactly 22 must pass.
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "Edge", ["lastName"] = "Grad",
            ["phoneNumber"] = "01012345678", ["email"] = $"edge-{Guid.NewGuid():N}@example.com",
            ["gender"] = "Male", ["age"] = 22, ["role"] = "Graduate",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!",
            ["graduatedFrom"] = "Cairo University", ["graduationYear"] = 2024,
            ["department"] = "Engineering"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_Success_ReturnsTokenAndUser()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "mazenmohamed@unisphere.edu", password = "Password123!" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.Equal("mazenmohamed@unisphere.edu", body!.User.Email);
        Assert.Equal("Student", body.User.Role);
        Assert.NotEmpty(body.Token);
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WrongPassword_IsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "mazenmohamed@unisphere.edu", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_RoleMismatch_IsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "mazenmohamed@unisphere.edu", password = "Password123!", roleId = 4 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsCurrentUser()
    {
        var client = _factory.AuthedClient(
            await _factory.LoginAsync("mazenmohamed@unisphere.edu", "Password123!"));

        var response = await client.GetAsync("/api/auth/me");

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<AuthUser>();
        Assert.Equal("mazenmohamed@unisphere.edu", user!.Email);
    }

    [Fact]
    public async Task Me_WithoutToken_IsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ThenOldPasswordFails()
    {
        var email = $"pw-{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/auth/register", ValidStudent(email));
        reg.EnsureSuccessStatusCode();

        var client = _factory.AuthedClient(
            await _factory.LoginAsync(email, "Password123!"));

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Password123!", newPassword = "NewPassword123!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Password123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "NewPassword123!" });
        Assert.True(newLogin.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Roles_ListContainsAllFour()
    {
        var response = await _client.GetAsync("/api/auth/roles");

        response.EnsureSuccessStatusCode();
        var roles = await response.Content.ReadFromJsonAsync<List<Role>>();
        Assert.NotNull(roles);
        Assert.Equal(4, roles!.Count);
        Assert.Contains(roles, r => r.Id == 1 && r.Name == "Student");
        Assert.Contains(roles, r => r.Id == 4 && r.Name == "Admin");
    }

    [Fact]
    public async Task RoleById_UnknownNumber_NotFound()
    {
        var response = await _client.GetAsync("/api/auth/roles/99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record AuthResponse(string Token, DateTime ExpiresAtUtc, AuthUser User);
    private record AuthUser(int Id, string FullName, string Email, int RoleId, string Role, string Major, string? AvatarUrl);
    private record Role(int Id, string Name);
}
