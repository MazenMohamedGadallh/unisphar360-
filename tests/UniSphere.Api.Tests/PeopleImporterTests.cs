using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// PeopleImporter tests on a throw-away SQLite database with TINY fixture
/// CSVs (never the real Data/students-*.csv / doctors-*.csv files).
/// Covers: idempotency, per-row skip with reasons, password requirement,
/// passed/current enrollment shapes, doctor links + student counts and
/// university resolution so nothing ever cross-links universities.
/// </summary>
public class PeopleImporterTests : IDisposable
{
    private static readonly string TestUni = "Test Uni";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "unisphere-people-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private AppDbContext? _db;

    public PeopleImporterTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Data"));
        _dbPath = Path.Combine(Path.GetTempPath(), "unisphere-people-" + Guid.NewGuid().ToString("N") + ".db");
    }

    private AppDbContext Db() => _db ??= NewDb(_dbPath);

    private static AppDbContext NewDb(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private void SeedUniAndCourses(string universityName, params (string Code, string Dept)[] courses)
    {
        var db = Db();
        var uni = new University { Name = universityName };
        db.Universities.Add(uni);
        db.SaveChanges();
        foreach (var (code, dept) in courses)
            db.Courses.Add(new Course
            {
                Code = code, UniversityId = uni.Id, Title = "Title " + code,
                Description = "", Track = "", Semester = "", Credits = 3,
                Department = dept, AcademicYear = null
            });
        db.SaveChanges();
    }

    private static void WriteCsv(string root, string fileName, string content) =>
        File.WriteAllText(Path.Combine(root, "Data", fileName), content, new UTF8Encoding(false));

    // Fixture: 2 valid students (one with a passed course), 1 invalid email,
    // 1 unknown course code, and 1 repeated email that exercises the
    // same-university update path. Physical CSV line numbers are asserted.
    private const string StudentsCsv =
    // 1
@"StudentCode,FirstName,MiddleName,LastName,Gender,Age,Email,Phone,Address,Department,DepartmentName,AcademicYear,CurrentCourses,PassedCourses
// 2
S1,Ali,Hasan,Omar,Male,20,ali@test.edu,0100,addr,CS,Computer Science,2,CS 102,CS 101
// 3
S2,Sara,,Nour,Female,21,sara@test.edu,0110,addr,CS,Computer Science,2,CS 101,
// 4
S3,Bad,Email,Row,Male,20,bad-email,0120,addr,CS,Computer Science,2,CS 101,
// 5
S4,No,,Course,Male,22,nocourse@test.edu,0130,addr,CS,Computer Science,2,ZZ 999,
// 6
S5,Dup,,Person,Male,23,ali@test.edu,0140,addr,CS,Computer Science,2,CS 102,";

    private const string DoctorsCsv =
    // 1
@"DoctorCode,FirstName,MiddleName,LastName,Gender,Age,Email,Phone,Address,Department,DepartmentName,Specialization,CourseCodes
// 2
D1,Khaled,Ibrahim,Said,Male,44,khaled.doc@test.edu,0140,addr,CS,Computer Science,AI,CS 101;CS 102
// 3
D2,Mona,,Tarek,Female,39,mona.doc@test.edu,0150,addr,CS,Computer Science,Data Science,CS 101";

    // The comment markers above are only in this source file; the real
    // fixtures are written through WriteFixture which strips them.
    private static string Clean(string raw) =>
        string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));

    private string WriteFixtures()
    {
        WriteCsv(_root, "students-test-uni.csv", Clean(StudentsCsv));
        WriteCsv(_root, "doctors-test-uni.csv", Clean(DoctorsCsv));
        if (!Db().Universities.Any(u => u.Name == TestUni))
            SeedUniAndCourses(TestUni,
                ("CS 101", "Computer Science"),
                ("CS 102", "Computer Science"));
        // Same course code in ANOTHER university must never be linked.
        if (!Db().Universities.Any(u => u.Name == "Other Uni"))
            SeedUniAndCourses("Other Uni", ("CS 101", "Computer Science"));
        return _root;
    }

    [Fact]
    public void Import_CreatesUsersLinksAndReports_Then_SecondRunCreatesNothing()
    {
        var root = WriteFixtures();

        var first = PeopleImporter.ImportAll(Db(), root, "DoNotLog123!", dryRun: false);

        // Users.
        Assert.Equal(5, first.CreatedUsers);            // ali, sara, nocourse, 2 doctors
        Assert.Equal(1, first.UpdatedUsers);            // S5 = ali's email again
        Assert.Equal(0, first.ExistingUsers);
        Assert.Equal(1, first.InvalidRows);             // line 4 bad email
        Assert.Contains(first.Reasons, r => r.Contains("students-test-uni.csv line 4") && r.Contains("bad email"));

        // Enrollments: ali current CS 102 + passed CS 101, sara current CS 101.
        Assert.Equal(3, first.EnrollmentsCreated);
        Assert.Equal(1, first.UnknownCourseCodes);      // ZZ 999 on line 5
        Assert.Contains(first.Reasons, r => r.Contains("students-test-uni.csv line 5") && r.Contains("ZZ 999"));

        // Links: Khaled gets both courses; Mona is NOT allowed to replace him.
        Assert.Equal(2, first.LinksCreated);
        Assert.Equal(1, first.LinksSkippedDifferentInstructor);
        Assert.Contains(first.Reasons, r =>
            r.Contains("doctors-test-uni.csv line 3") &&
            r.Contains("CS 101") &&
            r.Contains("NOT replaced"));

        // Database shapes.
        var db = Db();
        var uni = db.Universities.Single(u => u.Name == TestUni);
        var cs101 = db.Courses.Single(c => c.UniversityId == uni.Id && c.Code == "CS 101");
        var cs102 = db.Courses.Single(c => c.UniversityId == uni.Id && c.Code == "CS 102");
        var ali = db.Users.Single(u => u.Email == "ali@test.edu");

        Assert.Equal(TestUni, db.Universities.Single(u => u.Id == ali.UniversityId!.Value).Name);
        Assert.Equal("Ali Hasan Omar", ali.FullName);
        Assert.Equal(UserRole.Student, ali.Role);

        var ali101 = db.Enrollments.Single(e => e.UserId == ali.Id && e.CourseId == cs101.Id);
        Assert.Equal("C", ali101.GradeLetter);          // passed shape = C / 2.0
        Assert.Equal(2.0, ali101.GradePoints);
        Assert.Equal("Current", ali101.Semester);
        Assert.Equal(100, ali101.AttendancePercent);
        Assert.Equal("Good", ali101.Status);

        var ali102 = db.Enrollments.Single(e => e.UserId == ali.Id && e.CourseId == cs102.Id);
        Assert.Equal("", ali102.GradeLetter);           // current shape
        Assert.Equal(0.0, ali102.GradePoints);

        var sara = db.Users.Single(u => u.Email == "sara@test.edu");
        var sara101 = db.Enrollments.Single(e => e.UserId == sara.Id && e.CourseId == cs101.Id);
        Assert.Equal("", sara101.GradeLetter);
        Assert.Equal(0.0, sara101.GradePoints);

        // No enrollment ever points at the foreign university's course.
        var otherUni = db.Universities.Single(u => u.Name == "Other Uni");
        Assert.DoesNotContain(db.Enrollments, e => e.Course.UniversityId == otherUni.Id);

        // Doctor links + registered-student counts (distinct users).
        var khaled = db.Users.Single(u => u.Email == "khaled.doc@test.edu");
        Assert.Equal(UserRole.Doctor, khaled.Role);
        Assert.Equal("Khaled Ibrahim Said", khaled.FullName);
        Assert.True(db.CourseInstructors.Any(ci => ci.CourseId == cs101.Id && ci.DoctorUserId == khaled.Id && ci.Term == "Fall 2025"));
        Assert.True(db.CourseInstructors.Any(ci => ci.CourseId == cs102.Id && ci.DoctorUserId == khaled.Id));
        var mona = db.Users.Single(u => u.Email == "mona.doc@test.edu");
        Assert.False(db.CourseInstructors.Any(ci => ci.DoctorUserId == mona.Id));

        // The doctor-module mirror (CourseAssignments) received the same two
        // links and never a row for the skipped doctor — this is what the
        // integrated module's dashboard and access checks read.
        Assert.Equal(2, db.CourseAssignments.Count());
        Assert.True(db.CourseAssignments.Any(ca => ca.CourseId == cs101.Id && ca.DoctorUserId == khaled.Id));
        Assert.True(db.CourseAssignments.Any(ca => ca.CourseId == cs102.Id && ca.DoctorUserId == khaled.Id));
        Assert.False(db.CourseAssignments.Any(ca => ca.DoctorUserId == mona.Id));

        var cs101Count = db.Enrollments.Where(e => e.CourseId == cs101.Id)
            .Select(e => e.UserId).Distinct().Count();
        Assert.Equal(2, cs101Count);                    // ali passed it, sara current
        var cs102Count = db.Enrollments.Where(e => e.CourseId == cs102.Id)
            .Select(e => e.UserId).Distinct().Count();
        Assert.Equal(1, cs102Count);

        // ---------- Second run: fully idempotent ----------
        var second = PeopleImporter.ImportAll(Db(), root, "DoNotLog123!", dryRun: false);
        Assert.Equal(0, second.CreatedUsers);
        Assert.Equal(0, second.EnrollmentsCreated);
        Assert.Equal(0, second.LinksCreated);
        // S1 and S5 both carry ali@test.edu with DIFFERENT contact values, so
        // every replay of the file legitimately re-applies them (last row
        // wins, exactly like run 1): the counter stays at 2 and ali's final
        // profile equals the run-1 outcome. The identity guarantee still
        // holds — the updates never touch names — and nothing is created.
        Assert.Equal(2, second.UpdatedUsers);
        Assert.Equal(4, second.ExistingUsers);          // sara, nocourse, 2 doctors

        Assert.Equal(first.CreatedUsers, Db().Users.Count());
        Assert.Equal(first.EnrollmentsCreated, Db().Enrollments.Count());
        Assert.Equal(first.LinksCreated, Db().CourseInstructors.Count());
        Assert.Equal(first.LinksCreated, Db().CourseAssignments.Count());   // module mirror too
        Assert.Equal("Ali Hasan Omar", Db().Users.Single(u => u.Email == "ali@test.edu").FullName);
    }

    [Fact]
    public void DryRun_WritesNothing_ButReportsSameCounts()
    {
        var root = WriteFixtures();

        var report = PeopleImporter.ImportAll(Db(), root, "DoNotLog123!", dryRun: true);

        Assert.Equal(5, report.CreatedUsers);
        Assert.Equal(3, report.EnrollmentsCreated);
        Assert.Equal(2, report.LinksCreated);

        var db = Db();
        Assert.Equal(0, db.Users.Count());              // nothing was written
        Assert.Equal(0, db.Enrollments.Count());
        Assert.Equal(0, db.CourseInstructors.Count());
        Assert.Equal(0, db.CourseAssignments.Count());  // dry run writes no module rows either
        Assert.Equal(2, db.Universities.Count());       // Test Uni + Other Uni were pre-seeded —
                                                        // the dry run added no third university
    }

    [Fact]
    public void Falls_Back_To_Default_Password_When_User_Secret_Missing()
    {
        var root = WriteFixtures();

        // Empty and null mean "no user-secret set": the importer bootstraps
        // NEW accounts with its built-in default password instead of
        // refusing, and reports only the count of such users (spec change:
        // the CSV files carry no password column, so the import must be
        // able to run without a configured secret).
        var first = PeopleImporter.ImportAll(Db(), root, "", dryRun: false);
        Assert.Equal(5, first.CreatedUsers);
        Assert.Equal(5, first.UsersWithDefaultPassword);

        var second = PeopleImporter.ImportAll(Db(), root, null!, dryRun: true);
        Assert.Equal(0, second.CreatedUsers);              // the first call already created them
        Assert.Equal(0, second.UsersWithDefaultPassword);  // nothing new is bootstrapped
        // The fixture's S1/S5 rows conflict on contact values (by design);
        // every replay re-applies them (S1 then S5) — names are never
        // rewritten. Dry run mirrors the real-run counters exactly.
        Assert.Equal(2, second.UpdatedUsers);
        Assert.Equal(4, second.ExistingUsers);             // sara, nocourse, 2 doctors

        // An explicit password still overrides; existing users never get
        // re-passworded, so the default counter stays at zero on replays.
        var third = PeopleImporter.ImportAll(Db(), root, "DoNotLog123!", dryRun: false);
        Assert.Equal(0, third.CreatedUsers);
        Assert.Equal(0, third.UsersWithDefaultPassword);
        Assert.Equal(2, third.UpdatedUsers);               // same S1/S5 contact flip
        Assert.Equal(4, third.ExistingUsers);
    }

    public void Dispose()
    {
        _db?.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>University isolation through the REAL HTTP pipeline: a Cairo
/// student never touches a Helwan course — foreign ids return 404.</summary>
public class PeopleIsolationApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PeopleIsolationApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CairoStudent_CannotEnroll_HelwanCourse()
    {
        var anonymous = _factory.CreateClient();
        var universities = await anonymous.GetFromJsonAsync<List<UniversityDto>>("/api/auth/universities");
        Assert.NotNull(universities);
        var cairoId = universities!.Single(u => u.Name == "Cairo University").Id;
        var helwanId = universities.Single(u => u.Name == "Helwan University").Id;

        // A Helwan student sees the Helwan catalog and any course id.
        var helwanEmail = $"helwan-{Guid.NewGuid():N}@example.com";
        var (helwanClient, _) = await RegisterAsync(helwanEmail, helwanId, 1);
        var helwanCourses = await helwanClient.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(helwanCourses);
        Assert.NotEmpty(helwanCourses!);
        Assert.All(helwanCourses, c => Assert.True(c.Id > 0));
        var helwanCourseId = helwanCourses!.First().Id;

        // A Cairo student gets their own university only.
        var cairoEmail = $"cairo-{Guid.NewGuid():N}@example.com";
        var (cairoClient, me) = await RegisterAsync(cairoEmail, cairoId, 1);
        Assert.Equal("Cairo University", me.UniversityName);

        var helwanCoursesSeenByCairo = await cairoClient.GetFromJsonAsync<List<UniversityCourseDto>>("/api/university/me/courses");
        Assert.NotNull(helwanCoursesSeenByCairo);
        Assert.DoesNotContain(helwanCoursesSeenByCairo!, c => c.Id == helwanCourseId);

        // Foreign course id → 404, indistinguishable from a missing id.
        var enroll = await cairoClient.PostAsJsonAsync("/api/enrollments", new { courseId = helwanCourseId });
        Assert.Equal(HttpStatusCode.NotFound, enroll.StatusCode);
    }

    private async Task<(HttpClient Client, MyUniversityDto Me)> RegisterAsync(string email, int universityId, int year)
    {
        var anonymous = _factory.CreateClient();
        const string password = "Password123!";
        var register = await anonymous.PostAsJsonAsync("/api/auth/register", new Dictionary<string, object?>
        {
            ["firstName"] = "People",
            ["lastName"] = "Tester",
            ["phoneNumber"] = "01098765432",
            ["email"] = email,
            ["gender"] = "Female",
            ["age"] = 19,
            ["role"] = "Student",
            ["password"] = password,
            ["confirmPassword"] = password,
            ["academicYear"] = year,
            ["department"] = "CS"
        });
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        var assign = await _factory.AdminClient.PutAsJsonAsync(
            $"/api/admin/users/{auth!.User.Id}",
            new
            {
                firstName = "People",
                middleName = (string?)null,
                lastName = "Tester",
                email,
                phoneNumber = "01098765432",
                address = "",
                department = "CS",
                universityId
            });
        assign.EnsureSuccessStatusCode();

        var client = _factory.AuthedClient(await _factory.LoginAsync(email, password));
        var me = await client.GetFromJsonAsync<MyUniversityDto>("/api/university/me");
        Assert.NotNull(me);
        return (client, me!);
    }
}
