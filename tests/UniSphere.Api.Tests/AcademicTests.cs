using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;
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

/// <summary>
/// End-to-end tests for the Graduation feature: eligibility tracking,
/// the apply -> withdraw / approve / reject lifecycle, ceremony lookup,
/// certificates with honors, and the admin review flow.
/// Uses its own throw-away database (separate fixture) so the mutable
/// graduation data here never leaks into other test classes.
/// </summary>
public class GraduationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public GraduationTests(ApiFactory factory) => _factory = factory;

    // ---- Shared helpers ----

    private static object NewStudentBody(string email) => new
    {
        firstName = "Layla",
        middleName = (string?)null,
        lastName = "Student",
        phoneNumber = "01012345678",
        email,
        gender = "Female",
        age = 22,
        role = "Student",
        password = "Password123!",
        confirmPassword = "Password123!",
        academicYear = 5,
        department = "Computer Engineering",
        specialization = (string?)null,
        graduatedFrom = (string?)null,
        graduationYear = (int?)null
    };

    /// <summary>Registers a fresh student and returns (userId, bearer client).</summary>
    private async Task<(int Id, HttpClient Client)> RegisterStudentAsync()
    {
        var email = $"layla-{Guid.NewGuid():N}@example.com";
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewStudentBody(email));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthBody>();
        var client = _factory.AuthedClient(await _factory.LoginAsync(email, "Password123!"));
        return (body!.User.Id, client);
    }

    /// <summary>Marks the student's current semester record as a finished,
    /// high-GPA degree so the eligibility audit passes.</summary>
    private async Task MakeEligibleAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = db.SemesterRecords.First(r => r.UserId == userId && r.IsCurrent);
        record.Gpa = 3.4;
        record.AttendanceRate = 91;
        record.CompletedCourses = 26;
        record.TotalCourses = 26;
        await db.SaveChangesAsync();
    }

    // ---- Overview ----

    [Fact]
    public async Task Overview_Student_ShowsRealProgressAndBlockingReason()
    {
        var (userId, client) = await RegisterStudentAsync();

        var response = await client.GetAsync("/api/graduation/overview");

        response.EnsureSuccessStatusCode();
        var o = await response.Content.ReadFromJsonAsync<Overview>();
        Assert.Equal("Student", o!.Role);
        Assert.False(o.IsGraduated);
        Assert.Equal("Computer Engineering", o.Degree);
        Assert.Equal(2.0, o.MinGpaRequired);
        Assert.Equal(0, o.CoursesCompleted);
        Assert.False(o.IsEligible);
        Assert.Contains(o.BlockingReasons, r => r.Contains("GPA"));
        Assert.Contains(o.BlockingReasons, r => r.Contains("coursework"));
        Assert.Equal(5, o.Checklist.Count);
        Assert.Equal("none", o.ApplicationStatus);
        Assert.Null(o.Application);
        Assert.Empty(o.FailedCourses);
        Assert.True(userId > 0);
    }

    [Fact]
    public async Task Overview_Alumni_ReturnsConferralBlockAndHonors()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/graduation/overview");

        response.EnsureSuccessStatusCode();
        var o = await response.Content.ReadFromJsonAsync<Overview>();
        Assert.Equal("Graduate", o!.Role);
        Assert.True(o.IsGraduated);
        Assert.NotNull(o.Alumni);
        Assert.Equal("Cairo University, Faculty of Engineering", o.Alumni!.GraduatedFrom);
        Assert.Equal(2023, o.Alumni.GraduationYear);
        Assert.Equal(3.4, o.Alumni.FinalGpa);
        Assert.Equal("With Distinction", o.Alumni.Honors);
        Assert.True(o.Alumni.HasCertificate);
    }

    // ---- Eligibility + apply ----

    [Fact]
    public async Task Apply_IneligibleStudent_FailsWithReasons()
    {
        var response = await _factory.StudentClient.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)"I think I am done", expectedGraduationYear = (int?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplyError>();
        Assert.NotNull(body!.BlockingReasons);
        Assert.NotEmpty(body.BlockingReasons); // Mazen: coursework 18/26
        // The rejected attempt must not create an application.
        var app = await _factory.StudentClient.GetAsync("/api/graduation/application");
        Assert.Equal(HttpStatusCode.NotFound, app.StatusCode);
    }

    [Fact]
    public async Task Apply_EligibleStudent_Succeeds_AndIsIdempotent()
    {
        var (userId, client) = await RegisterStudentAsync();
        await MakeEligibleAsync(userId);

        var ok = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = "All requirements completed.", expectedGraduationYear = (int?)(DateTime.UtcNow.Year + 1) });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var app = await ok.Content.ReadFromJsonAsync<ApplicationSummary>();
        Assert.Equal("Pending", app!.Status);
        Assert.True(app.Id > 0);
        Assert.True(app.DecisionNote is null); // no decision yet

        // Duplicate: 409, not a second application.
        var dupe = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.Conflict, dupe.StatusCode);

        // Visible via GET too.
        var fetched = await client.GetAsync("/api/graduation/application");
        fetched.EnsureSuccessStatusCode();
        var again = await fetched.Content.ReadFromJsonAsync<ApplicationSummary>();
        Assert.Equal(app.Id, again!.Id);
        Assert.Equal("Pending", again.Status);

        // Checklist reacts: submitted + registrar review are open items.
        var overview = await client.GetFromJsonAsync<Overview>("/api/graduation/overview");
        Assert.Equal("Pending", overview!.ApplicationStatus);
        Assert.True(overview.IsEligible);
        Assert.Empty(overview.BlockingReasons);
        Assert.True(overview.Checklist.First(c => c.Key == "application-submitted").IsMet);
        Assert.False(overview.Checklist.First(c => c.Key == "registrar-review").IsMet);
    }

    [Fact]
    public async Task Apply_GraduateOrAdmin_IsForbidden()
    {
        // A graduate has no active student record to audit; admins / doctors are
        // not students. Both are rejected at the authorization boundary (403),
        // never reaching the business rules.
        var graduate = await _factory.GraduateClient.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.Forbidden, graduate.StatusCode);

        var admin = await _factory.AdminClient.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    // ---- Withdraw + re-apply ----

    [Fact]
    public async Task Withdraw_ThenReapply_RestartsLifecycle()
    {
        var (userId, client) = await RegisterStudentAsync();
        await MakeEligibleAsync(userId);
        var ok = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        Assert.True(ok.IsSuccessStatusCode);

        var withdraw = await client.PostAsync("/api/graduation/application/withdraw", null);

        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
        var app = await withdraw.Content.ReadFromJsonAsync<ApplicationSummary>();
        Assert.Equal("Withdrawn", app!.Status);

        // No active application left to withdraw again.
        var second = await client.PostAsync("/api/graduation/application/withdraw", null);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        // And the student can file a new one.
        var reapply = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.OK, reapply.StatusCode);
        var fresh = await reapply.Content.ReadFromJsonAsync<ApplicationSummary>();
        Assert.Equal("Pending", fresh!.Status);
    }

    // ---- Ceremony ----

    [Fact]
    public async Task Ceremony_ReturnsSeededUpcomingEvent()
    {
        var response = await _factory.StudentClient.GetAsync("/api/graduation/ceremony");

        response.EnsureSuccessStatusCode();
        var c = await response.Content.ReadFromJsonAsync<Ceremony>();
        Assert.Equal("Graduation Ceremony 2026", c!.Title);
        Assert.True(c.StartsAtUtc >= DateTime.UtcNow);
        Assert.Contains("Campus", c.Location);
    }

    // ---- Certificate ----

    [Fact]
    public async Task Certificate_NotYetConferred_IsNotFound()
    {
        var response = await _factory.StudentClient.GetAsync("/api/graduation/certificate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Certificate_Alumni_ReturnsHonors()
    {
        var response = await _factory.GraduateClient.GetAsync("/api/graduation/certificate");

        response.EnsureSuccessStatusCode();
        var c = await response.Content.ReadFromJsonAsync<Certificate>();
        Assert.Equal("Omar Hassan", c!.Holder);
        Assert.Contains("Cairo University", c.University);
        Assert.Equal(2023, c.GraduationYear);
        Assert.Equal(3.4, c.FinalGpa);
        Assert.Equal("With Distinction", c.Honors);
        Assert.StartsWith("UNI-GRAD-", c.CertificateId);
    }

    // ---- Admin review flow (approve confers, reject offers a re-apply) ----

    [Fact]
    public async Task Admin_Approve_ConfersDegreeAndBlocksReReview()
    {
        var (userId, client) = await RegisterStudentAsync();
        await MakeEligibleAsync(userId);
        var ok = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        var app = await ok.Content.ReadFromJsonAsync<ApplicationSummary>();

        // Admin sees it in the pending queue.
        var queue = await _factory.AdminClient.GetFromJsonAsync<Paged>("/api/graduation/applications?status=Pending");
        Assert.Contains(queue!.Items, i => i.Id == app!.Id);

        var response = await _factory.AdminClient.PostAsJsonAsync(
            $"/api/graduation/applications/{app!.Id}/review",
            new { approve = true, note = "Degree audit passed.", graduationYear = (int?)DateTime.UtcNow.Year });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reviewed = await response.Content.ReadFromJsonAsync<ApplicationSummary>();
        Assert.Equal("Approved", reviewed!.Status);
        Assert.Equal(DateTime.UtcNow.Year, reviewed.ConferredGraduationYear);

        // Conferral happened: certificate + alumni-style conferral data unlock.
        var cert = await client.GetAsync("/api/graduation/certificate");
        Assert.Equal(HttpStatusCode.OK, cert.StatusCode);
        var body = await cert.Content.ReadFromJsonAsync<Certificate>();
        Assert.Equal(DateTime.UtcNow.Year, body!.GraduationYear);
        Assert.Equal("With Distinction", body.Honors);
        var overview = await client.GetFromJsonAsync<Overview>("/api/graduation/overview");
        Assert.True(overview!.IsGraduated);

        // The welcome bot emitted a notification for the student.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(db.Notifications.Any(n => n.UserId == userId && n.Title == "Graduation approved"));
        Assert.True(db.Users.First(u => u.Id == userId).GraduationYear == DateTime.UtcNow.Year);

        // Already finalized: re-reviewing is a conflict.
        var again = await _factory.AdminClient.PostAsJsonAsync(
            $"/api/graduation/applications/{app.Id}/review",
            new { approve = false, note = (string?)null, graduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // Student filter sees nothing pending anymore.
        var after = await _factory.AdminClient.GetFromJsonAsync<Paged>("/api/graduation/applications?status=Pending");
        Assert.DoesNotContain(after!.Items, i => i.Id == app.Id);
    }

    [Fact]
    public async Task Admin_Reject_AllowsReapply()
    {
        var (userId, client) = await RegisterStudentAsync();
        await MakeEligibleAsync(userId);
        var ok = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = (string?)null, expectedGraduationYear = (int?)null });
        var app = await ok.Content.ReadFromJsonAsync<ApplicationSummary>();

        var response = await _factory.AdminClient.PostAsJsonAsync(
            $"/api/graduation/applications/{app!.Id}/review",
            new { approve = false, note = "One course still pending retake.", graduationYear = (int?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reviewed = await response.Content.ReadFromJsonAsync<ApplicationSummary>();
        Assert.Equal("Rejected", reviewed!.Status);
        Assert.Null(reviewed.ConferredGraduationYear);

        // Certificate stays locked and no conferral was stamped.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/graduation/certificate")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(db.Users.First(u => u.Id == userId).GraduationYear);

        // The checklist reflects the rejection and re-applying is allowed.
        var overview = await client.GetFromJsonAsync<Overview>("/api/graduation/overview");
        Assert.False(overview!.IsGraduated);
        Assert.False(overview.Checklist.First(c => c.Key == "application-submitted").IsMet);
        var reapply = await client.PostAsJsonAsync("/api/graduation/apply",
            new { note = "Retake finished this term.", expectedGraduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.OK, reapply.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_RequireAdminRole()
    {
        var list = await _factory.StudentClient.GetAsync("/api/graduation/applications");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var review = await _factory.StudentClient.PostAsJsonAsync(
            "/api/graduation/applications/1/review", new { approve = true, note = (string?)null, graduationYear = (int?)null });
        Assert.Equal(HttpStatusCode.Forbidden, review.StatusCode);
    }

    [Fact]
    public async Task Admin_ReviewUnknownApplication_IsNotFound()
    {
        var response = await _factory.AdminClient.PostAsJsonAsync(
            "/api/graduation/applications/99999/review",
            new { approve = true, note = (string?)null, graduationYear = (int?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Response shapes ----
    private record AuthBody(string Token, DateTime ExpiresAtUtc, AuthUser User);
    private record AuthUser(int Id, string FullName, string Email, int RoleId, string Role, string Major, string? AvatarUrl);
    private record Overview(string Role, bool IsGraduated, string Degree, string Department,
        string UniversityName, int? AcademicYear,
        double Gpa, double MinGpaRequired, int CoursesCompleted, int CoursesRequired, int CompletionPercent,
        int CreditsCompleted, int CreditsInProgress, List<string> FailedCourses,
        bool IsEligible, List<string> BlockingReasons, List<ChecklistItem> Checklist,
        string ApplicationStatus, ApplicationSummary? Application, Alumni? Alumni);
    private record ChecklistItem(string Key, string Title, string Detail, bool IsMet);
    private record ApplicationSummary(int Id, string Status, DateTime SubmittedAtUtc,
        DateTime? ReviewedAtUtc, string? DecisionNote, int? ConferredGraduationYear);
    private record Alumni(string GraduatedFrom, int? GraduationYear, double FinalGpa, string Honors, bool HasCertificate);
    private record Ceremony(int Id, string Title, string Location, DateTime StartsAtUtc, string? Description);
    private record Certificate(string CertificateId, string Holder, string Degree, string University,
        int GraduationYear, double FinalGpa, string Honors, DateTime? ConferredOnUtc);
    private record ApplyError(string Title, List<string> BlockingReasons);
    private record AppRow(int Id, int UserId, string StudentName, string Email, string Degree,
        double GpaAtApplication, int CoursesCompleted, int CoursesRequired, string Status,
        DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc, string? DecisionNote, int? ConferredGraduationYear);
    private record Paged(IReadOnlyList<AppRow> Items, int Total, int Page, int PageSize);
}
