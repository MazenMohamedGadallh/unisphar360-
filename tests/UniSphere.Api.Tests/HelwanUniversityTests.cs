using System.Net;
using System.Net.Http.Json;
using UniSphere.Api.Dtos;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Regression coverage for the Helwan "My University" visibility issue.
/// Students see their full own-university catalog; department/year are
/// optional filters, never an isolation boundary.
/// </summary>
public class HelwanUniversityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HelwanUniversityTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> HelwanClientAsync() =>
        _factory.AuthedClient(
            await _factory.LoginAsync("helwan.student@unisphere.edu", "Password123!"));

    [Fact]
    public async Task HelwanStudent_SeesAllHelwanCourses()
    {
        var client = await HelwanClientAsync();

        var meResponse = await client.GetAsync("/api/university/me");
        meResponse.EnsureSuccessStatusCode();
        var me = await meResponse.Content.ReadFromJsonAsync<MyUniversityDto>();

        Assert.NotNull(me);
        Assert.Equal("Helwan University", me!.UniversityName);
        Assert.Equal("Computer Science", me.Department);
        Assert.Equal(3, me.AcademicYear);
        Assert.Equal(92, me.CoursesCount);

        var coursesResponse = await client.GetAsync("/api/university/me/courses");
        coursesResponse.EnsureSuccessStatusCode();
        var courses = await coursesResponse.Content.ReadFromJsonAsync<List<UniversityCourseDto>>();

        Assert.NotNull(courses);
        Assert.Equal(92, courses!.Count);
        Assert.Equal(me.CoursesCount, courses.Count);

        // The four CS/year-3 courses sort first in the UI and are flagged.
        var matching = courses.Where(c => c.MatchesStudentDepartmentYear).ToList();
        Assert.Equal(4, matching.Count);
        Assert.Equal(
            new[] { "CS 342", "CS 352", "CS 410", "CS 471" },
            matching.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.All(matching, c =>
        {
            Assert.Equal("Computer Science", c.Department);
            Assert.Equal(3, c.AcademicYear);
        });

        // A course from another department/year is still visible in the catalog.
        var nonMatching = Assert.Single(courses, c => c.Code == "AI 330");
        Assert.Equal("Artificial Intelligence", nonMatching.Department);
        Assert.False(nonMatching.MatchesStudentDepartmentYear);

        var allowedStatuses = new[] { "Passed", "InProgress", "Locked", "Available" };
        Assert.All(courses, c => Assert.Contains(c.Status, allowedStatuses));
        Assert.Equal("InProgress", Assert.Single(courses, c => c.Code == "CS 111").Status);
        Assert.Equal("Locked", Assert.Single(courses, c => c.Code == "CS 342").Status);
        Assert.Equal("Available", Assert.Single(courses, c => c.Code == "CS 471").Status);
    }

    [Fact]
    public async Task DepartmentAndYearFilters_ReturnCorrectSubsets()
    {
        var client = await HelwanClientAsync();
        var all = await client.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(all);
        Assert.Equal(92, all!.Count);

        var byDepartment = await client.GetFromJsonAsync<List<UniversityCourseDto>>(
            $"/api/university/me/courses?department={Uri.EscapeDataString("Computer Science")}");
        Assert.NotNull(byDepartment);
        Assert.NotEmpty(byDepartment!);
        Assert.True(byDepartment.Count < all.Count);
        Assert.Equal(
            all.Where(c => c.Department == "Computer Science").Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal),
            byDepartment.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(byDepartment, c => Assert.Equal("Computer Science", c.Department));

        var byYear = await client.GetFromJsonAsync<List<UniversityCourseDto>>(
            "/api/university/me/courses?year=3");
        Assert.NotNull(byYear);
        Assert.NotEmpty(byYear!);
        Assert.Equal(
            all.Where(c => c.AcademicYear == 3).Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal),
            byYear.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(byYear, c => Assert.Equal(3, c.AcademicYear));

        var combined = await client.GetFromJsonAsync<List<UniversityCourseDto>>(
            $"/api/university/me/courses?department={Uri.EscapeDataString("Computer Science")}&year=3");
        Assert.NotNull(combined);
        Assert.Equal(
            new[] { "CS 342", "CS 352", "CS 410", "CS 471" },
            combined!.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.All(combined, c => Assert.True(c.MatchesStudentDepartmentYear));

        // No filter means no narrowing: the optional parameters are opt-in.
        var noFilter = await client.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.Equal(all.Count, noFilter!.Count);
    }

    [Fact]
    public async Task OtherUniversities_DoNotSeeHelwanCourses()
    {
        var helwan = await HelwanClientAsync();
        var helwanCourses = await helwan.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(helwanCourses);
        var helwanIds = helwanCourses!.Select(c => c.Id).ToHashSet();

        var elShorouk = _factory.AuthedClient(
            await _factory.LoginAsync("mazenmohamed@unisphere.edu", "Password123!"));
        var elShoroukCourses = await elShorouk.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(elShoroukCourses);
        Assert.Equal(11, elShoroukCourses!.Count);
        Assert.DoesNotContain(elShoroukCourses, c => helwanIds.Contains(c.Id));

        var (cairo, cairoMe) = await CreateCairoStudentAsync();
        Assert.Equal("Cairo University", cairoMe.UniversityName);
        Assert.Equal(99, cairoMe.CoursesCount);

        var cairoCourses = await cairo.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(cairoCourses);
        Assert.Equal(99, cairoCourses!.Count);
        Assert.DoesNotContain(cairoCourses, c => helwanIds.Contains(c.Id));
    }

    [Fact]
    public async Task HelwanStudent_ForeignCourseId_NotFound()
    {
        var elShorouk = _factory.AuthedClient(
            await _factory.LoginAsync("mazenmohamed@unisphere.edu", "Password123!"));
        var elShoroukCourses = await elShorouk.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(elShoroukCourses);
        Assert.NotEmpty(elShoroukCourses!);

        var helwan = await HelwanClientAsync();
        var foreignId = elShoroukCourses![0].Id;
        var detail = await helwan.GetAsync($"/api/courses/{foreignId}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

        var materials = await helwan.GetAsync($"/api/university/me/materials?courseId={foreignId}");
        Assert.Equal(HttpStatusCode.NotFound, materials.StatusCode);
    }

    private async Task<(HttpClient Client, MyUniversityDto Me)> CreateCairoStudentAsync()
    {
        var anonymous = _factory.CreateClient();
        var universities = await anonymous.GetFromJsonAsync<List<UniversityDto>>("/api/auth/universities");
        Assert.NotNull(universities);
        var cairoId = universities!.Single(u => u.Name == "Cairo University").Id;

        var email = $"cairo-{Guid.NewGuid():N}@example.com";
        const string phone = "01098765432";
        var register = await anonymous.PostAsJsonAsync("/api/auth/register", new Dictionary<string, object?>
        {
            ["firstName"] = "Cairo",
            ["lastName"] = "Student",
            ["phoneNumber"] = phone,
            ["email"] = email,
            ["gender"] = "Female",
            ["age"] = 19,
            ["role"] = "Student",
            ["password"] = "Password123!",
            ["confirmPassword"] = "Password123!",
            ["academicYear"] = 1,
            ["department"] = "MTHG"
        });
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // Assign the university through the admin endpoint; the registration
        // payload deliberately does not supply UniversityId.
        var assign = await _factory.AdminClient.PutAsJsonAsync(
            $"/api/admin/users/{auth!.User.Id}",
            new
            {
                firstName = "Cairo",
                middleName = (string?)null,
                lastName = "Student",
                email,
                phoneNumber = phone,
                address = "",
                department = "MTHG",
                universityId = cairoId
            });
        assign.EnsureSuccessStatusCode();

        var client = _factory.AuthedClient(await _factory.LoginAsync(email, "Password123!"));
        var me = await client.GetFromJsonAsync<MyUniversityDto>("/api/university/me");
        Assert.NotNull(me);
        Assert.Equal("Cairo University", me!.UniversityName);
        Assert.Equal(1, me.AcademicYear);
        return (client, me);
    }
}
