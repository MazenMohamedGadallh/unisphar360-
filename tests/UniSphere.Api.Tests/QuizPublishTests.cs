using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Acceptance tests for publishing AI-generated doctor quizzes to enrolled
/// students, reusing the existing exam infrastructure end to end (no separate
/// quiz tables). Covers: publish success + status, question mapping
/// (CorrectAnswer resolved server-side to the option TEXT, OptionsJson),
/// eligible-student visibility, no answer leakage to students, exclusion of
/// ineligible students, authorization failures, payload validation,
/// notification counts, rollback of orphan drafts when publication fails, and
/// the best-effort duplicate guard (409).
/// Everything here runs against the factory's throw-away SQLite database —
/// the developer's unisphere-v6.db is never touched.
/// </summary>
public class QuizPublishTests : IClassFixture<ApiFactory>
{
    // ---- one-time seed shared by every test in this class ----
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private static int _courseId;
    private static int _uniId;
    private static string _doctorEmail = "";
    private static string _otherDoctorEmail = "";
    private static string _enrolled1Email = "";
    private static string _enrolled2Email = "";
    private static string _gradedEmail = "";
    private static string _notEnrolledEmail = "";
    private static readonly int[] EligibleStudentIds = new int[2];

    private readonly ApiFactory _factory;
    private HttpClient? _doctor;
    private HttpClient? _otherDoctor;

    public QuizPublishTests(ApiFactory factory) => _factory = factory;

    // ---------- seed ----------

    private async Task SeedAsync()
    {
        if (_seeded) return;
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var uni = new University { Name = $"AI Quiz University {Guid.NewGuid():N}" };
            db.Universities.Add(uni);
            var course = new Course
            {
                Code = $"TQC{Random.Shared.Next(100, 999)}",
                Title = "AI Quiz Publishing Test Course",
                Description = "Course used by the quiz publish test suite.",
                Track = "Testing",
                Semester = "Current",
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            _uniId = uni.Id;
            course.UniversityId = _uniId;
            _courseId = course.Id;

            // Doctor assigned to the course (via the doctor-module join row).
            _doctorEmail = $"quiz.doctor.{Guid.NewGuid():N}@unisphere.edu";
            var doctor = NewUser(_doctorEmail, "Quiz", "Doctor", UserRole.Doctor);
            db.Users.Add(doctor);

            // Second doctor: same university, NOT assigned (expects 400).
            _otherDoctorEmail = $"quiz.other.{Guid.NewGuid():N}@unisphere.edu";
            db.Users.Add(NewUser(_otherDoctorEmail, "Other", "Doctor", UserRole.Doctor));

            // Two eligible students: enrolled CURRENT semester, ungraded.
            _enrolled1Email = $"quiz.stud1.{Guid.NewGuid():N}@unisphere.edu";
            _enrolled2Email = $"quiz.stud2.{Guid.NewGuid():N}@unisphere.edu";
            var enrolled1 = NewUser(_enrolled1Email, "Enrolled", "One", UserRole.Student);
            var enrolled2 = NewUser(_enrolled2Email, "Enrolled", "Two", UserRole.Student);
            db.Users.Add(enrolled1);
            db.Users.Add(enrolled2);

            // Finished student: took the course in a past semester -> excluded.
            _gradedEmail = $"quiz.grad.{Guid.NewGuid():N}@unisphere.edu";
            var graded = NewUser(_gradedEmail, "Graded", "Student", UserRole.Student);
            db.Users.Add(graded);

            // Not-enrolled student: same university, no enrollment -> excluded.
            _notEnrolledEmail = $"quiz.lonely.{Guid.NewGuid():N}@unisphere.edu";
            db.Users.Add(NewUser(_notEnrolledEmail, "Not", "Enrolled", UserRole.Student));

            await db.SaveChangesAsync();

            db.CourseAssignments.Add(new CourseAssignment
            {
                DoctorUserId = doctor.Id,
                CourseId = course.Id,
            });
            db.Enrollments.Add(new Enrollment { UserId = enrolled1.Id, CourseId = course.Id, Semester = "Current" });
            // In-progress grade this semester: must still receive the quiz.
            db.Enrollments.Add(new Enrollment { UserId = enrolled2.Id, CourseId = course.Id, Semester = "Current", GradeLetter = "B+", GradePoints = 3.3 });
            db.Enrollments.Add(new Enrollment
            {
                UserId = graded.Id, CourseId = course.Id, Semester = "Fall 2025",
                GradeLetter = "A", GradePoints = 4,
            });
            await db.SaveChangesAsync();

            EligibleStudentIds[0] = enrolled1.Id;
            EligibleStudentIds[1] = enrolled2.Id;
            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private User NewUser(string email, string first, string last, UserRole role) =>
        new()
        {
            FirstName = first,
            LastName = last,
            FullName = $"{first} {last}",
            Email = email,
            PhoneNumber = "0100000000",
            Address = "Test City",
            Gender = "Male",
            Age = 24,
            Major = "Computer Science",
            Department = "Computer Science",
            UniversityId = _uniId,
            Role = role,
            PasswordHash = PasswordService.Hash(role == UserRole.Doctor ? "Doctor123!" : "Student123!"),
        };

    // ---------- clients ----------

    private async Task<HttpClient> DoctorAsync()
    {
        await SeedAsync();
        return _doctor ??= _factory.AuthedClient(
            await _factory.LoginAsync(_doctorEmail, "Doctor123!"));
    }

    private async Task<HttpClient> OtherDoctorAsync()
    {
        await SeedAsync();
        return _otherDoctor ??= _factory.AuthedClient(
            await _factory.LoginAsync(_otherDoctorEmail, "Doctor123!"));
    }

    private async Task<HttpClient> StudentAsync(string email)
    {
        await SeedAsync();
        return _factory.AuthedClient(await _factory.LoginAsync(email, "Student123!"));
    }

    // ---------- request helpers ----------

    private const string ExplanationSecret = "EXPLANATIONSECRET";

    private const string PublishEndpoint = "/api/doctor/ai/quiz/publish";

    private static List<GeneratedQuestionDto> ValidQuestions(int count = 3)
    {
        var list = new List<GeneratedQuestionDto>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new GeneratedQuestionDto(
                Question: $"Test question {i + 1}: which option is B?",
                Options: new List<string> { "Option A", "Option B", "Option C", "Option D" },
                CorrectIndex: 1,
                Explanation: ExplanationSecret,
                Reference: null));
        }
        return list;
    }

    private static string UniqueTitle(string label) => $"QT {label} {Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> PublishAsync(
        HttpClient client, string title,
        List<GeneratedQuestionDto>? questions = null)
    {
        var req = new QuizPublishRequest(
            CourseId: _courseId,
            MaterialId: null,
            Title: title,
            Language: "en",
            Marks: 10,
            Questions: questions ?? ValidQuestions());
        return client.PostAsJsonAsync(PublishEndpoint, req);
    }

    // ---------- 1-8: happy paths ----------

    [Fact]
    public async Task Publish_Succeeds_And_Returns_Counts()
    {
        await SeedAsync();
        var title = UniqueTitle("Success");
        var resp = await PublishAsync(await DoctorAsync(), title);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<QuizPublishResponse>();
        Assert.NotNull(body);
        Assert.True(body.ExamId > 0);
        Assert.Equal(title, body.Title);
        Assert.Equal(3, body.QuestionCount);
        // Two eligible (enrolled, current, ungraded) students exist.
        Assert.Equal(2, body.NotifiedStudents);
        Assert.Equal("Published", body.Status);
    }

    [Fact]
    public async Task Publish_Persists_Exam_With_Published_Status()
    {
        await SeedAsync();
        var title = UniqueTitle("Status");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == examId);
        Assert.Equal("Published", exam.Status);
        Assert.NotNull(exam.PublishedAtUtc);
        Assert.Equal("en", exam.Language);
        Assert.Equal(10, exam.Marks);
    }

    [Fact]
    public async Task Publish_Maps_All_Questions_With_Order()
    {
        await SeedAsync();
        var title = UniqueTitle("Count");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.ExamQuestions.AsNoTracking()
            .Where(q => q.ExamId == examId)
            .OrderBy(q => q.OrderIndex)
            .ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows[0].OrderIndex);
        Assert.Equal(2, rows[2].OrderIndex);
        Assert.All(rows, q => Assert.Equal(examId, q.ExamId));
    }

    [Fact]
    public async Task CorrectAnswer_Is_Option_Text_Resolved_On_Server()
    {
        await SeedAsync();
        var title = UniqueTitle("Answer");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.ExamQuestions.AsNoTracking()
            .Where(q => q.ExamId == examId)
            .ToListAsync();
        Assert.NotEmpty(rows);
        // CorrectIndex=1 => the option text "Option B", stored as text; a
        // client-supplied answer field could never inject anything else.
        Assert.All(rows, q => Assert.Equal("Option B", q.CorrectAnswer));
        Assert.All(rows, q => Assert.Equal("MCQ", q.QuestionType));
    }

    [Fact]
    public async Task OptionsJson_Stores_Serialized_Option_Array()
    {
        await SeedAsync();
        var title = UniqueTitle("Options");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ExamQuestions.AsNoTracking()
            .FirstAsync(q => q.ExamId == examId);
        var options = JsonSerializer.Deserialize<List<string>>(row.OptionsJson ?? "[]");
        Assert.NotNull(options);
        Assert.Equal(new[] { "Option A", "Option B", "Option C", "Option D" }, options);
    }

    [Fact]
    public async Task Eligible_Student_Sees_The_Quiz()
    {
        await SeedAsync();
        var title = UniqueTitle("Visible");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        var student = await StudentAsync(_enrolled1Email);

        var list = await student.GetFromJsonAsync<List<StudentExamDto>>("/api/student/exams");
        Assert.NotNull(list);
        var summary = list.Single(e => e.Id == examId);
        Assert.Equal(title, summary.Title);
        Assert.Equal(3, summary.QuestionCount);

        var detail = await student.GetFromJsonAsync<StudentExamDetailDto>(
            $"/api/student/exams/{examId}");
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Questions.Count);
        Assert.All(detail.Questions, q =>
        {
            var opts = JsonSerializer.Deserialize<List<string>>(q.OptionsJson ?? "[]");
            Assert.NotNull(opts);
            Assert.Equal(4, opts!.Count);
        });
    }

    [Fact]
    public async Task Student_Detail_Hides_Answers_And_Explanations()
    {
        await SeedAsync();
        var title = UniqueTitle("LeakSafe");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        var student = await StudentAsync(_enrolled2Email);
        var raw = await student.GetStringAsync($"/api/student/exams/{examId}");

        // The explanation text never reaches the student payload...
        Assert.DoesNotContain(ExplanationSecret, raw);
        // ...and no correct-answer field is serialized at all...
        Assert.DoesNotContain("correctanswer", raw, StringComparison.OrdinalIgnoreCase);
        // ...while the option texts the student SHOULD see are present.
        Assert.Contains("Option A", raw);
    }

    [Fact]
    public async Task Ineligible_Students_Cannot_See_The_Quiz()
    {
        await SeedAsync();
        var title = UniqueTitle("Hidden");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var examId = (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;

        // Student who finished the course last semester: excluded from the list...
        var graded = await StudentAsync(_gradedEmail);
        var gradedList = await graded.GetFromJsonAsync<List<StudentExamDto>>("/api/student/exams");
        Assert.DoesNotContain(gradedList, e => e.Id == examId);
        // ...and detail returns 404 (foreign ids stay indistinguishable).
        Assert.Equal(HttpStatusCode.NotFound,
            (await graded.GetAsync($"/api/student/exams/{examId}")).StatusCode);

        // Never-enrolled student: also excluded.
        var lonely = await StudentAsync(_notEnrolledEmail);
        var lonelyList = await lonely.GetFromJsonAsync<List<StudentExamDto>>("/api/student/exams");
        Assert.NotNull(lonelyList);
        Assert.DoesNotContain(lonelyList, e => e.Id == examId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await lonely.GetAsync($"/api/student/exams/{examId}")).StatusCode);
    }

    // ---------- 9-14: authorization + validation ----------

    [Fact]
    public async Task Unassigned_Doctor_Gets_400()
    {
        await SeedAsync();
        var resp = await PublishAsync(await OtherDoctorAsync(), UniqueTitle("NotAssigned"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();
        Assert.Contains("assigned", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Student_Cannot_Publish_Quiz()
    {
        await SeedAsync();
        var student = await StudentAsync(_enrolled1Email);
        var resp = await PublishAsync(student, UniqueTitle("Forbidden"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Out_Of_Range_CorrectIndex_Is_400()
    {
        await SeedAsync();
        var questions = new List<GeneratedQuestionDto>
        {
            new("Q with a bad index", new List<string> { "A", "B" },
                CorrectIndex: 5, Explanation: "", Reference: null),
        };
        var resp = await PublishAsync(await DoctorAsync(), UniqueTitle("BadIndex"), questions);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Single_Option_Question_Is_400()
    {
        await SeedAsync();
        var questions = new List<GeneratedQuestionDto>
        {
            new("Q with one option", new List<string> { "Only A" },
                CorrectIndex: 0, Explanation: "", Reference: null),
        };
        var resp = await PublishAsync(await DoctorAsync(), UniqueTitle("OneOption"), questions);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Empty_Title_Is_400()
    {
        await SeedAsync();
        var resp = await PublishAsync(await DoctorAsync(), "   ");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Invalid_MaterialId_Is_400()
    {
        await SeedAsync();
        var doctor = await DoctorAsync();
        var resp = await doctor.PostAsJsonAsync(PublishEndpoint,
            new QuizPublishRequest(
                CourseId: _courseId,
                MaterialId: 999999,
                Title: UniqueTitle("BadMaterial"),
                Language: "en",
                Marks: 10,
                Questions: ValidQuestions(1)));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();
        Assert.Contains("material", raw, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 15-17: notifications, rollback, duplicates ----------

    [Fact]
    public async Task Notification_Count_Equals_Eligible_Recipients()
    {
        await SeedAsync();
        var title = UniqueTitle("Notify");
        var resp = await PublishAsync(await DoctorAsync(), title);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<QuizPublishResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body.NotifiedStudents);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.Title == $"New exam: {title}")
            .ToListAsync();
        Assert.Equal(2, notifications.Count);
        // Exactly the eligible students received one notification each.
        Assert.Equal(
            EligibleStudentIds.OrderBy(x => x).ToList(),
            notifications.ConvertAll(n => n.UserId).OrderBy(x => x).ToList());
        Assert.All(notifications, n => Assert.False(n.IsRead));
    }

    [Fact]
    public async Task Publish_Failure_Rolls_Back_Orphan_Draft()
    {
        await SeedAsync();
        var title = UniqueTitle("Rollback");

        // Swap in a failing publish step: the fake persists a real draft exam
        // through the scoped AppDbContext (inside the quiz service's ambient
        // transaction) and then throws during publication.
        var host = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IExamAssistantService>();
            services.AddScoped<IExamAssistantService>(sp =>
                new PublishFailingExamAssistant(sp.GetRequiredService<AppDbContext>()));
        }));

        var client = host.CreateClient();
        // The derived host shares the base factory's database + signing key,
        // so a token minted there is valid here. The publish endpooint is
        // doctor-only; Authentication must be attached explicitly.
        var token = await _factory.LoginAsync(_doctorEmail, "Doctor123!");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var req = new QuizPublishRequest(
            CourseId: _courseId, MaterialId: null, Title: title, Language: "en",
            Marks: 10, Questions: ValidQuestions(2));
        var resp = await client.PostAsJsonAsync(PublishEndpoint, req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        // The draft staged by the fake must be gone — publication failing
        // inside the transaction rolls the whole draft back.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Exams.AsNoTracking().AnyAsync(e => e.Title == title));
    }

    [Fact]
    public async Task Duplicate_Publish_Within_Window_Is_409()
    {
        await SeedAsync();
        var title = UniqueTitle("Duplicate");
        var doctor = await DoctorAsync();

        var first = await PublishAsync(doctor, title);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same doctor + course + title within the 2-minute window.
        var second = await PublishAsync(doctor, title);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---------- fake used only by the orphan-draft rollback test ----------

    private sealed class PublishFailingExamAssistant(AppDbContext db)
        : IExamAssistantService
    {
        public Task<ExamGenerationResponse> GenerateFromMaterialsAsync(
            int doctorUserId, ExamAssistantGenerationRequest req, CancellationToken ct)
            => throw new NotSupportedException();

        public async Task<ExamDto> CreateDraftExamAsync(
            int doctorUserId, UpdateExamRequest req, CancellationToken ct)
        {
            // Persist a real draft row inside the caller's transaction.
            var exam = new Exam
            {
                DoctorUserId = doctorUserId,
                CourseId = req.CourseId,
                Title = req.Title,
                QuestionCount = req.QuestionCount,
                QuestionTypes = req.QuestionTypes,
                Difficulty = req.Difficulty,
                Language = req.Language,
                MaterialsUsed = req.MaterialsUsed,
                Status = "Draft",
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync(ct);
            return new ExamDto(
                exam.Id, exam.DoctorUserId, exam.CourseId, "", "",
                exam.Title, exam.QuestionCount, exam.QuestionTypes,
                exam.Difficulty, exam.Language, exam.MaterialsUsed,
                exam.CreatedAtUtc, exam.Status, exam.PublishedAtUtc, exam.Marks,
                new List<ExamQuestionDto>());
        }

        public Task<(ExamDto Exam, int NotifiedCount)> PublishExamWithNotifiedCountAsync(
            int doctorUserId, ExamAssistantPublishRequest req, CancellationToken ct)
            => throw new InvalidOperationException("simulated publish failure");

        public Task<ExamDto> PublishExamAsync(
            int doctorUserId, ExamAssistantPublishRequest req, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ExamListDto>> ListExamsAsync(
            int doctorUserId, CancellationToken ct) => throw new NotSupportedException();

        public Task<ExamDto?> GetExamAsync(
            int doctorUserId, int examId, CancellationToken ct) => throw new NotSupportedException();

        public Task<ExamDto?> UpdateExamAsync(
            int doctorUserId, int examId, UpdateExamRequest req, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> DeleteExamAsync(
            int doctorUserId, int examId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<StudentExamDto>> ListStudentExamsAsync(
            int studentUserId, CancellationToken ct) => throw new NotSupportedException();

        public Task<StudentExamDto?> GetStudentExamAsync(
            int studentUserId, int examId, CancellationToken ct) => throw new NotSupportedException();

        public Task<StudentExamDetailDto?> GetStudentExamDetailAsync(
            int studentUserId, int examId, CancellationToken ct) => throw new NotSupportedException();

        public Task<UserNotificationSettingDto> GetNotificationSettingAsync(
            int userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<UserNotificationSettingDto> UpdateNotificationSettingAsync(
            int userId, UpdateUserNotificationSettingRequest req, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
