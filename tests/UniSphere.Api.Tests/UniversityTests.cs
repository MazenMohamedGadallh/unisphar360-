using System.Net;
using System.Net.Http.Json;
using UniSphere.Api.Dtos;
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
        Assert.Equal(11, me.CoursesCount);
        Assert.True(me.MaterialsCount >= 19);
    }

    [Fact]
    public async Task Courses_MatchSeededCatalog()
    {
        var response = await _factory.StudentClient.GetAsync("/api/university/me/courses");

        response.EnsureSuccessStatusCode();
        var courses = await response.Content.ReadFromJsonAsync<List<UniCourse>>();
        Assert.NotNull(courses);
        Assert.Equal(11, courses!.Count);
        Assert.Contains(courses, c => c.Title == "Machine Learning" && c.MaterialsCount >= 3);
        Assert.Contains(courses, c => c.Code == "CS420" && c.Title == "Cloud Computing");
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
        var helwan = _factory.AuthedClient(
            await _factory.LoginAsync("helwan.student@unisphere.edu", "Password123!"));
        var helwanCourses = await helwan.GetFromJsonAsync<List<UniCourse>>("/api/university/me/courses");
        Assert.NotNull(helwanCourses);
        Assert.NotEmpty(helwanCourses!);

        // El Shorouk's student must not read a Helwan course's materials.
        var response = await _factory.StudentClient.GetAsync(
            $"/api/university/me/materials?courseId={helwanCourses![0].Id}");

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
    public async Task Isolation_OtherUniversity_DoesNotSeeElShoroukCourses()
    {
        var anonymous = _factory.CreateClient();
        var universities = await anonymous.GetFromJsonAsync<List<UniversityDto>>("/api/auth/universities");
        Assert.NotNull(universities);
        var cairoId = universities!.Single(u => u.Name == "Cairo University").Id;

        var email = $"cairo-{Guid.NewGuid():N}@example.com";
        var reg = await anonymous.PostAsJsonAsync("/api/auth/register", new Dictionary<string, object?>
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
            ["universityId"] = cairoId
        });
        reg.EnsureSuccessStatusCode();

        var elShoroukCourses = await _factory.StudentClient.GetFromJsonAsync<List<UniCourse>>(
            "/api/university/me/courses");
        Assert.NotNull(elShoroukCourses);
        var elShoroukIds = elShoroukCourses!.Select(c => c.Id).ToHashSet();

        var client = _factory.AuthedClient(await _factory.LoginAsync(email, "Password123!"));
        var courses = await client.GetAsync("/api/university/me/courses");
        courses.EnsureSuccessStatusCode();
        var list = await courses.Content.ReadFromJsonAsync<List<UniCourse>>();
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
        Assert.DoesNotContain(list!, c => elShoroukIds.Contains(c.Id));
    }

    [Fact]
    public async Task MaterialDetail_OwnUniversity_ReturnsFullMaterial()
    {
        var list = await _factory.StudentClient.GetFromJsonAsync<MaterialPage>(
            "/api/university/me/materials?page=1&pageSize=10");
        Assert.NotNull(list);
        Assert.NotEmpty(list!.Items);
        var expected = list.Items[0];

        var response = await _factory.StudentClient.GetAsync($"/api/university/me/materials/{expected.Id}");
        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<MaterialDto>();

        Assert.NotNull(detail);
        Assert.Equal(expected.Id, detail!.Id);
        Assert.Equal(expected.Title, detail.Title);
        Assert.Equal(expected.Type, detail.Type);
        Assert.NotEmpty(detail.CourseName);
        Assert.Contains(detail.Type,
            new[] { "Summary", "Lecture", "Assignment", "Exam", "Announcement" });
    }

    [Fact]
    public async Task MaterialDetail_MaterialOfOtherUniversity_NotFound()
    {
        var elShorouk = await _factory.StudentClient.GetFromJsonAsync<MaterialPage>(
            "/api/university/me/materials?page=1&pageSize=10");
        Assert.NotNull(elShorouk);
        Assert.NotEmpty(elShorouk!.Items);

        // A Helwan student must not open an El Shorouk material by id.
        var client = _factory.AuthedClient(
            await _factory.LoginAsync("helwan.student@unisphere.edu", "Password123!"));
        var response = await client.GetAsync($"/api/university/me/materials/{elShorouk.Items[0].Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Materials_Pagination_SecondPage_KeepsTotal_UniqueItems()
    {
        var page1 = await _factory.StudentClient.GetFromJsonAsync<MaterialPage>(
            "/api/university/me/materials?page=1&pageSize=10");
        Assert.NotNull(page1);
        Assert.Equal(10, page1!.Items.Count);

        var page2 = await _factory.StudentClient.GetFromJsonAsync<MaterialPage>(
            "/api/university/me/materials?page=2&pageSize=10");
        Assert.NotNull(page2);
        Assert.Equal(page1.Total, page2!.Total);
        Assert.NotEmpty(page2.Items);
        Assert.DoesNotContain(page2!.Items, m => page1.Items.Any(p => p.Id == m.Id));
    }

    private record MyUni(string UniversityName, string Department, int? AcademicYear, int CoursesCount, int MaterialsCount);
    private record UniCourse(int Id, string Code, string Title, int MaterialsCount);
    private record MaterialPage(List<Material> Items, int Total, int Page, int PageSize);
    private record Material(int Id, string Title, string Type);
}
