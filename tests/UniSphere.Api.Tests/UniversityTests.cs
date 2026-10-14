using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class UniversityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public UniversityTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_ReturnsStudentUniversity()
    {
        var response = await _factory.StudentClient.GetAsync("/api/university/me");

        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<MyUni>();
        Assert.NotNull(me);
        Assert.Equal("El Shorouk Academy", me!.UniversityName);
        Assert.Equal("Computer Engineering", me.Department);
        Assert.Equal(6, me.CoursesCount);
        Assert.True(me.MaterialsCount >= 19);
    }

    [Fact]
    public async Task Courses_MatchSeededSix()
    {
        var response = await _factory.StudentClient.GetAsync("/api/university/me/courses");

        response.EnsureSuccessStatusCode();
        var courses = await response.Content.ReadFromJsonAsync<List<UniCourse>>();
        Assert.NotNull(courses);
        Assert.Equal(6, courses!.Count);
        Assert.Contains(courses, c => c.Title == "Machine Learning" && c.MaterialsCount >= 3);
    }

    [Fact]
    public async Task Materials_FilterByType()
    {
        var response = await _factory.StudentClient.GetAsync("/api/university/me/materials?type=Exam&page=1&pageSize=10");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<MaterialPage>();
        Assert.NotNull(page);
        Assert.NotEmpty(page!.Items);
        Assert.All(page.Items, m => Assert.Equal("Exam", m.Type));
    }

    [Fact]
    public async Task Materials_OutOfScopeCourse_NotFound()
    {
        // Course 11 (Cloud Computing) belongs to no university scope.
        var response = await _factory.StudentClient.GetAsync("/api/university/me/materials?courseId=11");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_Student_Forbidden_Admin_Allowed()
    {
        var payload = new { courseId = 1, title = "X", type = "Summary" };

        var student = await _factory.StudentClient.PostAsJsonAsync("/api/university/materials", payload);
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);

        var admin = await _factory.AdminClient.PostAsJsonAsync("/api/university/materials",
            new { courseId = 1, title = "Doctor Note", type = "Summary", description = "d", content = "c" });
        Assert.Equal(HttpStatusCode.Created, admin.StatusCode);
    }

    [Fact]
    public async Task Isolation_OtherUniversity_SeesNothing()
    {
        var email = $"cairo-{Guid.NewGuid():N}@example.com";
        var reg = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new Dictionary<string, object?>
        {
            ["firstName"] = "Laila",
            ["lastName"] = "Hassan",
            ["phoneNumber"] = "01098765432",
            ["email"] = email,
            ["gender"] = "Female",
            ["age"] = 19,
            ["role"] = "Student",
            ["password"] = "Password123!",
            ["confirmPassword"] = "Password123!",
            ["academicYear"] = 2,
            ["department"] = "Engineering",
            ["universityId"] = 2
        });
        reg.EnsureSuccessStatusCode();

        var client = _factory.AuthedClient(await _factory.LoginAsync(email, "Password123!"));
        var courses = await client.GetAsync("/api/university/me/courses");
        courses.EnsureSuccessStatusCode();
        var list = await courses.Content.ReadFromJsonAsync<List<UniCourse>>();
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    private record MyUni(string UniversityName, string Department, int? AcademicYear, int CoursesCount, int MaterialsCount);
    private record UniCourse(int Id, string Code, string Title, int MaterialsCount);
    private record MaterialPage(List<Material> Items, int Total, int Page, int PageSize);
    private record Material(int Id, string Title, string Type);
}
