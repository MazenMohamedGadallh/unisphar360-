using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Phase 1 acceptance tests for the doctor workspace:
/// locked profile identity fields, editable avatar/password, and the
/// per-doctor course display title with its student-visible effect.
/// </summary>
public class DoctorPhase1Tests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _doctor;
    private readonly HttpClient _student;
    private readonly HttpClient _admin;

    public DoctorPhase1Tests(ApiFactory factory)
    {
        _factory = factory;
        _doctor = factory.DoctorClient;
        _student = factory.StudentClient;
        _admin = factory.AdminClient;
    }

    // ---- Profile: locked identity fields ----

    [Fact]
    public async Task Profile_ReturnsLockedIdentityFields()
    {
        var profile = await GetProfileAsync();

        Assert.True(profile.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(profile.FirstName));
        Assert.False(string.IsNullOrWhiteSpace(profile.LastName));
        Assert.False(string.IsNullOrWhiteSpace(profile.FullName));
        Assert.False(string.IsNullOrWhiteSpace(profile.Email));
        Assert.False(string.IsNullOrWhiteSpace(profile.PhoneNumber));
        Assert.False(string.IsNullOrWhiteSpace(profile.Address));
        Assert.False(string.IsNullOrWhiteSpace(profile.Department));
        Assert.NotNull(profile.UniversityId);
        Assert.False(string.IsNullOrWhiteSpace(profile.UniversityName));
    }

    [Fact]
    public async Task Profile_Update_RejectsEveryLockedField_AndChangesNothing()
    {
        var before = await GetProfileAsync();

        var attempts = new (Dictionary<string, object?> Payload, string[] Rejected)[]
        {
            (new() { ["firstName"] = "Hacked" }, new[] { "firstName" }),
            (new() { ["middleName"] = "Hacked" }, new[] { "middleName" }),
            (new() { ["lastName"] = "Hacked" }, new[] { "lastName" }),
            (new() { ["email"] = "hacked@evil.test" }, new[] { "email" }),
            (new() { ["phoneNumber"] = "01099999999" }, new[] { "phoneNumber" }),
            (new() { ["address"] = "Somewhere else" }, new[] { "address" }),
            (new() { ["department"] = "Hacked Department" }, new[] { "department" }),
            (new() { ["universityId"] = 9999 }, new[] { "universityId" }),
            // Mixed request: even a valid avatar must not be applied when a
            // locked field is present (all-or-nothing). The avatar itself is
            // editable, so only the locked key is reported as rejected.
            (new()
            {
                ["avatarUrl"] = "https://evil.test/avatar.png",
                ["firstName"] = "Hacked"
            }, new[] { "firstName" })
        };

        foreach (var (payload, rejected) in attempts)
        {
            var response = await _doctor.PutAsJsonAsync("/api/doctor/profile", payload);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            // The friend module's allowlist answer: "Profile contains locked
            // fields." with a Fields list naming each unexpected key.
            Assert.Contains("Profile contains locked fields", body);
            foreach (var key in rejected)
                Assert.Contains(key, body);
        }

        var after = await GetProfileAsync();
        Assert.Equal(before.FirstName, after.FirstName);
        Assert.Equal(before.MiddleName, after.MiddleName);
        Assert.Equal(before.LastName, after.LastName);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PhoneNumber, after.PhoneNumber);
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.Department, after.Department);
        Assert.Equal(before.UniversityId, after.UniversityId);
        Assert.Equal(before.AvatarUrl, after.AvatarUrl);
    }

    [Fact]
    public async Task Profile_Update_Avatar_IsAccepted_AndPersisted()
    {
        var url = $"https://cdn.example.com/avatars/doctor-{Guid.NewGuid():N}.png";

        var response = await _doctor.PutAsJsonAsync("/api/doctor/profile", new { avatarUrl = url });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<Profile>();
        Assert.Equal(url, body!.AvatarUrl);

        var persisted = await GetProfileAsync();
        Assert.Equal(url, persisted.AvatarUrl);
    }

    [Fact]
    public async Task Profile_Update_Avatar_WithNonUrl_IsRejected()
    {
        // Adopted module contract: the avatar must be an absolute URL or an
        // uploaded /uploads/... path. (Absolute URIs like "javascript:..."
        // pass validation and stay inert when rendered as an <img src>.)
        var response = await _doctor.PutAsJsonAsync("/api/doctor/profile",
            new { avatarUrl = "not a url with spaces" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- My courses: server-computed enrolled counts ----

    [Fact]
    public async Task MyCourses_EnrolledCounts_MatchServerStudentLists()
    {
        var courses = await GetDoctorCoursesAsync();
        Assert.NotEmpty(courses);
        Assert.Contains(courses, c => c.StudentCount > 0);

        foreach (var course in courses)
        {
            var response = await _doctor.GetAsync($"/api/doctor/courses/{course.CourseId}/students");
            response.EnsureSuccessStatusCode();
            var students = await response.Content.ReadFromJsonAsync<List<StudentRow>>();

            Assert.NotNull(students);
            Assert.Equal(course.StudentCount, students!.Count);
        }
    }

    // ---- Course display title ----

    [Fact]
    public async Task Course_DisplayTitle_IsVisibleToStudents_WhileCatalogTitleStays()
    {
        var courses = await GetDoctorCoursesAsync();
        var target = courses.First(c => c.StudentCount > 0);
        var targetId = target.CourseId;
        var newTitle = $"Phase One Renamed {Guid.NewGuid():N}"[..28];

        var response = await _doctor.PutAsJsonAsync($"/api/doctor/courses/{targetId}/display-title",
            new { displayTitle = newTitle });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The doctor's own profile keeps the catalog title and exposes the display title.
        var profile = await GetProfileAsync();
        var mine = profile.Courses!.Single(c => c.CourseId == targetId);
        Assert.Equal(target.Title, mine.CatalogTitle);
        Assert.Equal(newTitle, mine.DisplayTitle);

        // Students see the effective title in the catalog list...
        var studentCourses = await _student.GetFromJsonAsync<List<StudentCourse>>("/api/courses");
        Assert.NotNull(studentCourses);
        Assert.Equal(newTitle, studentCourses!.First(c => c.Id == targetId).Title);

        // ...in the course detail...
        var detail = await _student.GetFromJsonAsync<StudentCourse>($"/api/courses/{targetId}");
        Assert.Equal(newTitle, detail!.Title);

        // ...and in their enrolled courses page.
        var enrolled = await _student.GetFromJsonAsync<List<StudentCourse>>("/api/university/me/courses");
        Assert.NotNull(enrolled);
        Assert.Equal(newTitle, enrolled!.First(c => c.Id == targetId).Title);

        // The catalog itself is never renamed: admin still sees the original title.
        var adminCourses = await _admin.GetFromJsonAsync<List<StudentCourse>>("/api/courses");
        Assert.Equal(target.Title, adminCourses!.First(c => c.Id == targetId).Title);
        Assert.Equal(target.Code, adminCourses!.First(c => c.Id == targetId).Code);
    }

    [Fact]
    public async Task Course_DisplayTitle_RejectsInvalidInput()
    {
        var course = (await GetDoctorCoursesAsync()).First();

        var invalidTitles = new[]
        {
            "ab",
            new string('x', 121),
            "<b>HTML</b>",
            "Title <script>alert(1)</script>"
        };

        foreach (var title in invalidTitles)
        {
            var response = await _doctor.PutAsJsonAsync($"/api/doctor/courses/{course.CourseId}/display-title",
                new { displayTitle = title });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // The display-title route binds a narrowly-typed request; unknown JSON
        // fields (here the catalog code) are ignored and the catalog is never
        // touched by this endpoint.
        var extraField = await _doctor.PutAsJsonAsync($"/api/doctor/courses/{course.CourseId}/display-title",
            new { displayTitle = "Valid New Title", code = "CS999" });
        Assert.Equal(HttpStatusCode.OK, extraField.StatusCode);
        var adminCourses = await _admin.GetFromJsonAsync<List<StudentCourse>>("/api/courses");
        Assert.Equal(course.Code, adminCourses!.First(c => c.Id == course.CourseId).Code);

        // Exactly 120 characters is the upper boundary and must pass.
        var boundary = await _doctor.PutAsJsonAsync($"/api/doctor/courses/{course.CourseId}/display-title",
            new { displayTitle = new string('a', 120) });
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
    }

    [Fact]
    public async Task Course_DisplayTitle_AnotherDoctorsCourse_Returns404()
    {
        var profile = await GetProfileAsync();
        var mineIds = (await GetDoctorCoursesAsync()).Select(c => c.CourseId).ToHashSet();

        // A published course of the same university that is not assigned to us.
        var adminCourses = await _admin.GetFromJsonAsync<List<AdminCourse>>("/api/admin/courses");
        Assert.NotNull(adminCourses);
        var foreign = adminCourses!.FirstOrDefault(c =>
            c.UniversityId == profile.UniversityId && !mineIds.Contains(c.Id));
        Assert.NotNull(foreign);

        // Create a second doctor, move them into the same university (locked
        // fields are admin-only) and assign the course via the doctor-module
        // assignment API (the display-title endpoint is assignment-scoped).
        var email = $"doctor2-{Guid.NewGuid():N}@example.com";
        var register = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            DoctorPayload(email));
        register.EnsureSuccessStatusCode();
        var registered = await register.Content.ReadFromJsonAsync<AuthResponse>();
        var doctor2Id = registered!.User.Id;

        var detail = await _admin.GetFromJsonAsync<AdminUserDetail>($"/api/admin/users/{doctor2Id}");
        var setUniversity = await _admin.PutAsJsonAsync($"/api/admin/users/{doctor2Id}", new
        {
            firstName = detail!.FirstName,
            middleName = detail.MiddleName,
            lastName = detail.LastName,
            email = detail.Email,
            phoneNumber = detail.PhoneNumber,
            address = detail.Address,
            department = detail.Department,
            universityId = profile.UniversityId
        });
        Assert.Equal(HttpStatusCode.OK, setUniversity.StatusCode);

        var assign = await _admin.PostAsJsonAsync("/api/course-assignments", new
        {
            doctorUserId = doctor2Id,
            courseId = foreign!.Id
        });
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);

        // The demo doctor must not be able to rename someone else's course...
        var denied = await _doctor.PutAsJsonAsync($"/api/doctor/courses/{foreign.Id}/display-title",
            new { displayTitle = "Hijacked Title" });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // ...while the assigned doctor can (control: the course id is valid).
        var doctor2 = _factory.AuthedClient(
            await _factory.LoginAsync(email, "DoctorPass123!"));
        var allowed = await doctor2.PutAsJsonAsync($"/api/doctor/courses/{foreign.Id}/display-title",
            new { displayTitle = "Second Doctor's Title" });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ---- Generic profile endpoint must not bypass the lock ----

    [Fact]
    public async Task Users_Put_CannotChangeDoctorNameOrMajor()
    {
        // The route id must match the JWT; resolve our own id from the profile.
        var profile = await GetProfileAsync();
        var me = await _doctor.GetFromJsonAsync<UserProfile>($"/api/users/{profile.Id}");
        Assert.NotNull(me);

        var changedName = await _doctor.PutAsJsonAsync($"/api/users/{profile.Id}", new
        {
            fullName = "Hacked Doctor",
            major = me!.Major,
            avatarUrl = me.AvatarUrl
        });
        Assert.Equal(HttpStatusCode.BadRequest, changedName.StatusCode);

        var changedMajor = await _doctor.PutAsJsonAsync($"/api/users/{profile.Id}", new
        {
            fullName = me.FullName,
            major = "Hacked Major",
            avatarUrl = me.AvatarUrl
        });
        Assert.Equal(HttpStatusCode.BadRequest, changedMajor.StatusCode);

        // Identity unchanged + a new avatar is still allowed.
        var avatar = $"https://cdn.example.com/avatars/self-{Guid.NewGuid():N}.png";
        var allowed = await _doctor.PutAsJsonAsync($"/api/users/{profile.Id}", new
        {
            fullName = me.FullName,
            major = me.Major,
            avatarUrl = avatar
        });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var updated = await allowed.Content.ReadFromJsonAsync<UserProfile>();
        Assert.Equal(me.FullName, updated!.FullName);
        Assert.Equal(avatar, updated.AvatarUrl);
    }

    // ---- Admin edit of locked fields + audit trail ----

    [Fact]
    public async Task Admin_CanEditLockedDoctorFields_AndActionIsAudited()
    {
        var profile = await GetProfileAsync();
        var detail = await _admin.GetFromJsonAsync<AdminUserDetail>($"/api/admin/users/{profile.Id}");
        Assert.NotNull(detail);

        var phone = "01234567890";
        var address = "99 Admin Edited Street";
        var department = "Computer Science";

        var put = await _admin.PutAsJsonAsync($"/api/admin/users/{profile.Id}", new
        {
            firstName = detail!.FirstName,
            middleName = detail.MiddleName,
            lastName = detail.LastName,
            email = detail.Email,
            phoneNumber = phone,
            address,
            department,
            universityId = detail.UniversityId
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var updated = await put.Content.ReadFromJsonAsync<AdminUserDetail>();
        Assert.Equal(phone, updated!.PhoneNumber);
        Assert.Equal(address, updated.Address);
        Assert.Equal(department, updated.Department);

        // The doctor sees the administrator's change (and still cannot edit it).
        var after = await GetProfileAsync();
        Assert.Equal(phone, after.PhoneNumber);
        Assert.Equal(address, after.Address);
        Assert.Equal(department, after.Department);

        var adminMe = await _admin.GetFromJsonAsync<AuthUser>("/api/auth/me");
        var logs = await _admin.GetFromJsonAsync<Paged<AuditEntry>>(
            "/api/admin/audit-log?pageSize=200");
        Assert.NotNull(logs);
        Assert.Contains(logs!.Items, l =>
            l.Path == $"PUT /api/admin/users/{profile.Id}" &&
            l.UserId == adminMe!.Id);
    }

    // ---- Password change (doctor profile page) ----

    [Fact]
    public async Task DoctorPassword_ChangeRequiresCurrentPassword()
    {
        var email = $"doctor-pw-{Guid.NewGuid():N}@example.com";
        var anonymous = _factory.CreateClient();
        var register = await anonymous.PostAsJsonAsync("/api/auth/register", DoctorPayload(email));
        register.EnsureSuccessStatusCode();

        var doctor = _factory.AuthedClient(
            await _factory.LoginAsync(email, "DoctorPass123!"));

        var wrong = await doctor.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "WrongPass123!", newPassword = "NewDoctorPass123!" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var ok = await doctor.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "DoctorPass123!", newPassword = "NewDoctorPass123!" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var oldLogin = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email, password = "DoctorPass123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email, password = "NewDoctorPass123!" });
        Assert.True(newLogin.IsSuccessStatusCode);
    }

    // ---- Helpers ----

    private static object DoctorPayload(string email) => new
    {
        firstName = "Phase",
        middleName = (string?)null,
        lastName = "One Doctor",
        phoneNumber = "01012345678",
        email,
        gender = "Male",
        age = 40,
        role = "Doctor",
        password = "DoctorPass123!",
        confirmPassword = "DoctorPass123!",
        academicYear = (int?)null,
        department = "Computer Science",
        specialization = "Software Engineering",
        graduatedFrom = (string?)null,
        graduationYear = (int?)null,
        universityId = (int?)null,
        address = "1 Test Street"
    };

    private async Task<Profile> GetProfileAsync()
    {
        var response = await _doctor.GetAsync("/api/doctor/profile");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Profile>())!;
    }

    private async Task<List<DoctorCourse>> GetDoctorCoursesAsync()
    {
        var response = await _doctor.GetAsync("/api/doctor/courses");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DoctorCourse>>())!;
    }

    private record Profile(
        int Id, string FirstName, string? MiddleName, string LastName, string FullName,
        string Email, string Address, string PhoneNumber,
        int? UniversityId, string UniversityName, string Department,
        string? Specialization, string? AvatarUrl, List<ProfileCourseRow>? Courses = null);

    private record ProfileCourseRow(
        int CourseId, string Code, string CatalogTitle, string? DisplayTitle,
        string Semester, int EnrolledCount);

    private record DoctorCourse(
        int CourseId, string Code, string Title, string Track, int Credits,
        string Semester, int StudentCount, double? AverageGradePoints, double? AverageAttendance);

    private record StudentRow(
        int EnrollmentId, int UserId, string FullName, int? AcademicYear, string Major,
        string Status, string GradeLetter, double GradePoints, int AttendancePercent,
        string Semester);

    private record StudentCourse(int Id, string Code, string Title);

    private record AdminCourse(int Id, string Code, string Title, int? UniversityId);

    private record AdminUserDetail(
        int Id, string FirstName, string? MiddleName, string LastName, string FullName,
        string Email, string Address, string PhoneNumber,
        int? UniversityId, string UniversityName, string Department,
        string Role, int RoleId, bool IsActive, bool IsDeleted);

    private record UserProfile(int Id, string FullName, string FirstName, string? AvatarUrl, string Major);

    private record AuthResponse(string Token, DateTime ExpiresAtUtc, AuthUser User);

    private record AuthUser(int Id);

    private record AuditEntry(
        int Id, int UserId, string Method, string Path, int StatusCode,
        int DurationMs, DateTime Timestamp, string IpAddress);

    private record Paged<T>(List<T> Items, int Total, int Page, int PageSize);
}
