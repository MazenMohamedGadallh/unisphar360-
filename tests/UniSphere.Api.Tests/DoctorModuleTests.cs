using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

public class DoctorModuleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DoctorModuleTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_ReturnsDoctorOverview()
    {
        var resp = await _factory.DoctorClient.GetAsync("/api/doctor/dashboard");
        resp.EnsureSuccessStatusCode();
        var d = await resp.Content.ReadFromJsonAsync<DoctorDash>();
        Assert.NotNull(d);
        Assert.True(d!.CourseCount >= 1);
        Assert.True(d.StudentCount >= 1);
        Assert.Contains(d.Courses, c => c.Code == "CS350");
    }

    [Fact]
    public async Task Courses_ListOnlyAssigned()
    {
        var resp = await _factory.DoctorClient.GetAsync("/api/doctor/courses");
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<List<DoctorCourse>>();
        Assert.NotNull(list);
        Assert.Equal(4, list.Count);
        Assert.All(list, c => Assert.Contains(c.Code, new[] { "CS350", "CS330", "CS240", "CS320" }));
        Assert.DoesNotContain(list, c => c.Code == "CS210");
    }

    [Fact]
    public async Task CourseDetail_Assigned_ReturnsInfo()
    {
        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");

        var resp = await _factory.DoctorClient.GetAsync($"/api/doctor/courses/{cs350.CourseId}");
        resp.EnsureSuccessStatusCode();
        var detail = await resp.Content.ReadFromJsonAsync<DoctorCourseDetail>();
        Assert.NotNull(detail);
        Assert.Equal(cs350.CourseId, detail.Id);
        Assert.Equal("CS350", detail.Code);
    }

    [Fact]
    public async Task Students_ReturnsEnrolledWithGrades()
    {
        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");

        var resp = await _factory.DoctorClient.GetAsync($"/api/doctor/courses/{cs350.CourseId}/students");
        resp.EnsureSuccessStatusCode();
        var students = await resp.Content.ReadFromJsonAsync<List<DoctorStudent>>();
        Assert.NotNull(students);
        Assert.NotEmpty(students);
        // Mazen is enrolled and graded in CS350.
        Assert.Contains(students, s => s.FullName == "Mazen Mohamed");
    }

    [Fact]
    public async Task Analytics_ReturnsDistributionsAndAlerts()
    {
        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");

        var resp = await _factory.DoctorClient.GetAsync($"/api/doctor/courses/{cs350.CourseId}/analytics");
        resp.EnsureSuccessStatusCode();
        var a = await resp.Content.ReadFromJsonAsync<DoctorAnalytics>();
        Assert.NotNull(a);
        Assert.True(a.EnrollmentCount >= 1);
        Assert.NotNull(a.GradeDistribution);
        Assert.NotNull(a.AttendanceDistribution);
    }

    [Fact]
    public async Task Materials_Create_Get_Delete()
    {
        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");

        var createResp = await _factory.DoctorClient.PostAsJsonAsync($"/api/doctor/courses/{cs350.CourseId}/materials",
            new { title = "Test Lecture", type = "Lecture", description = "d", content = "c" });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<DoctorMaterial>();
        Assert.NotNull(created);
        Assert.Equal("Test Lecture", created.Title);

        var listResp = await _factory.DoctorClient.GetAsync($"/api/doctor/courses/{cs350.CourseId}/materials");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<DoctorMaterial>>();
        Assert.Contains(list!, m => m.Title == "Test Lecture");

        var delResp = await _factory.DoctorClient.DeleteAsync($"/api/doctor/materials/{created.Id}");
        delResp.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
    }

    [Fact]
    public async Task Announcements_Create_Get_Delete()
    {
        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");

        var createResp = await _factory.DoctorClient.PostAsJsonAsync($"/api/doctor/courses/{cs350.CourseId}/announcements",
            new { title = "Hall change", body = "Labs move to Hall B", category = "general" });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<DoctorAnnouncement>();
        Assert.NotNull(created);
        Assert.Equal("Hall change", created.Title);

        var listResp = await _factory.DoctorClient.GetAsync($"/api/doctor/courses/{cs350.CourseId}/announcements");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<DoctorAnnouncement>>();
        Assert.Contains(list!, a => a.Title == "Hall change");

        var delResp = await _factory.DoctorClient.DeleteAsync($"/api/doctor/announcements/{created.Id}");
        delResp.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
    }

    [Fact]
    public async Task GradeUpdate_AssignedCourse_Success()
    {
        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");
        var students = await GetStudentsAsync(cs350.CourseId);
        var mazen = students.First(s => s.FullName == "Mazen Mohamed");
        Assert.NotEqual(default, mazen.EnrollmentId);

        var resp = await _factory.DoctorClient.PutAsJsonAsync($"/api/enrollments/{mazen.EnrollmentId}/grade",
            new { GradeLetter = "A" });
        resp.EnsureSuccessStatusCode();
        var updated = await resp.Content.ReadFromJsonAsync<EnrollmentGradeDto>();
        Assert.Equal("A", updated!.GradeLetter);
    }

    [Fact]
    public async Task GradeUpdate_UnassignedCourse_Forbidden()
    {
        // Mazen's CS120 enrollment is NOT one of the demo doctor's assigned
        // courses, so the grade update must be rejected server-side.
        var mazen = await _factory.StudentClient.GetAsync("/api/enrollments");
        mazen.EnsureSuccessStatusCode();
        var rows = (await mazen.Content.ReadFromJsonAsync<List<EnrollmentGradeDto>>())!;
        var unassigned = rows.First(r => r.CourseCode == "CS120");

        var resp = await _factory.DoctorClient.PutAsJsonAsync($"/api/enrollments/{unassigned.Id}/grade",
            new { GradeLetter = "A" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Doctor_CannotAccessAnotherDoctorsCourse()
    {
        var email = $"doc2-{Guid.NewGuid():N}@unisphere.edu";
        var reg = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new Dictionary<string, object?>
        {
            ["firstName"] = "Second", ["lastName"] = "Doctor", ["phoneNumber"] = "01212345678",
            ["email"] = email, ["gender"] = "Male", ["age"] = 40,
            ["role"] = "Doctor", ["specialization"] = "Artificial Intelligence",
            ["password"] = "Password123!", ["confirmPassword"] = "Password123!",
            ["department"] = "Computer Science"
        });
        reg.EnsureSuccessStatusCode();

        var doc2 = _factory.AuthedClient(await _factory.LoginAsync(email, "Password123!"));

        var courses = await GetAssignedCourseIds();
        var cs350 = courses.First(c => c.Code == "CS350");

        var resp = await doc2.GetAsync($"/api/doctor/courses/{cs350.CourseId}/students");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Student_CannotAccessDoctorEndpoints()
    {
        var dashboard = await _factory.StudentClient.GetAsync("/api/doctor/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, dashboard.StatusCode);

        var courses = await _factory.StudentClient.GetAsync("/api/doctor/courses");
        Assert.Equal(HttpStatusCode.Forbidden, courses.StatusCode);
    }

    [Fact]
    public async Task AiStatus_NoProvider_AvailableFalse()
    {
        var resp = await _factory.DoctorClient.GetAsync("/api/doctor/ai/status");
        resp.EnsureSuccessStatusCode();
        var status = await resp.Content.ReadFromJsonAsync<AiStatus>();
        Assert.NotNull(status);
        Assert.False(status!.Available);
        Assert.NotEmpty(status.Message);
    }

    [Fact]
    public async Task AiQuiz_NoProvider_Returns503()
    {
        var resp = await _factory.DoctorClient.PostAsJsonAsync("/api/doctor/ai/quiz",
            new { courseId = 5, questionCount = 3 });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    private async Task<List<DoctorCourse>> GetAssignedCourseIds()
    {
        var resp = await _factory.DoctorClient.GetAsync("/api/doctor/courses");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<DoctorCourse>>())!;
    }

    private async Task<List<DoctorStudent>> GetStudentsAsync(int courseId)
    {
        var resp = await _factory.DoctorClient.GetAsync($"/api/doctor/courses/{courseId}/students");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<DoctorStudent>>())!;
    }

    private record DoctorDash(int CourseCount, int StudentCount, double? AverageGradePoints,
        double? AverageAttendance, List<DoctorCourse> Courses, List<object> Alerts, List<object> RecentAnnouncements);

    private record DoctorCourse(int CourseId, string Code, string Title, string Track, int Credits,
        string Semester, int StudentCount, double? AverageGradePoints, double? AverageAttendance);

    private record DoctorCourseDetail(int Id, string Code, string Title, string Description,
        string Track, int Credits, string Semester, string Department, int? AcademicYear);

    private record DoctorStudent(int EnrollmentId, int StudentId, string FullName, string Email,
        string Department, int? AcademicYear, string GradeLetter, double GradePoints,
        int AttendancePercent, string Status);

    private record DoctorAnalytics(int CourseId, string Code, string Title, int EnrollmentCount,
        int GradedCount, double? AverageGradePoints, double? AverageAttendance,
        List<object> GradeDistribution, List<object> AttendanceDistribution, List<object> Alerts);

    private record DoctorMaterial(int Id, int CourseId, string Title, string Type, string Description,
        string Content, string? FileUrl, DateTime? DeadlineUtc, DateTime CreatedAtUtc);

    private record DoctorAnnouncement(int Id, int? CourseId, string? CourseName, string Title,
        string Body, string? Category, DateTime CreatedAtUtc);

    private record EnrollmentGradeDto(int Id, int CourseId, string CourseCode, string CourseTitle,
        string Semester, string GradeLetter, double GradePoints, int AttendancePercent, string Status);

    private record AiStatus(bool Available, List<string> Providers, string Message);
}
