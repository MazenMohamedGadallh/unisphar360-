using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Acceptance tests for published-exam attempts: publish (quiz infra) →
/// student takes → the SERVER grades and persists the attempt (one per
/// student per exam) and each answer's correctness. Covers: score
/// persistence, no correct-answer leakage in any student payload (before
/// submit and after submit without permission), rejection of a second
/// attempt, open/close window enforcement, doctor results (names, scores,
/// percentages, non-takers), and authorization (foreign doctor 404 /
/// foreign student 404 / student on a doctor route 403).
/// Everything here runs against the factory's throw-away SQLite database —
/// the developer's unisphere-v6.db is never touched.
/// </summary>
public class ExamAttemptTests : IClassFixture<ApiFactory>
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
    private static int[] _enrolledIds = new int[2];

    private readonly ApiFactory _factory;
    private HttpClient? _doctor;
    private HttpClient? _otherDoctor;

    public ExamAttemptTests(ApiFactory factory) => _factory = factory;

    // ---------- seed (same eligibility shape as QuizPublishTests) ----------

    private async Task SeedAsync()
    {
        if (_seeded) return;
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var uni = new University { Name = $"Exam Attempt University {Guid.NewGuid():N}" };
            db.Universities.Add(uni);
            var course = new Course
            {
                Code = $"EAC{Random.Shared.Next(100, 999)}",
                Title = "Exam Attempts Test Course",
                Description = "Course used by the exam attempts test suite.",
                Track = "Testing",
                Semester = "Current",
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            _uniId = uni.Id;
            course.UniversityId = _uniId;
            _courseId = course.Id;

            _doctorEmail = $"attempt.doctor.{Guid.NewGuid():N}@unisphere.edu";
            var doctor = NewUser(_doctorEmail, "Attempt", "Doctor", UserRole.Doctor);
            db.Users.Add(doctor);

            _otherDoctorEmail = $"attempt.other.{Guid.NewGuid():N}@unisphere.edu";
            db.Users.Add(NewUser(_otherDoctorEmail, "Other", "Doctor", UserRole.Doctor));

            _enrolled1Email = $"attempt.stud1.{Guid.NewGuid():N}@unisphere.edu";
            _enrolled2Email = $"attempt.stud2.{Guid.NewGuid():N}@unisphere.edu";
            var enrolled1 = NewUser(_enrolled1Email, "Enrolled", "One", UserRole.Student);
            var enrolled2 = NewUser(_enrolled2Email, "Enrolled", "Two", UserRole.Student);
            db.Users.Add(enrolled1);
            db.Users.Add(enrolled2);

            _gradedEmail = $"attempt.grad.{Guid.NewGuid():N}@unisphere.edu";
            var graded = NewUser(_gradedEmail, "Graded", "Student", UserRole.Student);
            db.Users.Add(graded);

            _notEnrolledEmail = $"attempt.lonely.{Guid.NewGuid():N}@unisphere.edu";
            db.Users.Add(NewUser(_notEnrolledEmail, "Not", "Enrolled", UserRole.Student));

            await db.SaveChangesAsync();

            db.CourseAssignments.Add(new CourseAssignment
            {
                DoctorUserId = doctor.Id,
                CourseId = course.Id,
            });
            db.Enrollments.Add(new Enrollment { UserId = enrolled1.Id, CourseId = course.Id, Semester = "Current" });
            db.Enrollments.Add(new Enrollment { UserId = enrolled2.Id, CourseId = course.Id, Semester = "Current" });
            db.Enrollments.Add(new Enrollment
            {
                UserId = graded.Id, CourseId = course.Id, Semester = "Fall 2025", // finished last semester
                GradeLetter = "B", GradePoints = 3,
            });
            await db.SaveChangesAsync();

            _enrolledIds[0] = enrolled1.Id;
            _enrolledIds[1] = enrolled2.Id;
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

    // ---------- publish helper (quiz infra; 3 MCQs, correct = "Option B") ----------

    private const string ExplanationSecret = "EXPLANATIONSECRET";

    private static List<GeneratedQuestionDto> ValidQuestions(int count = 3)
    {
        var list = new List<GeneratedQuestionDto>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new GeneratedQuestionDto(
                Question: $"Attempt question {i + 1}: which option is B?",
                Options: new List<string> { "Option A", "Option B", "Option C", "Option D" },
                CorrectIndex: 1,
                Explanation: ExplanationSecret,
                Reference: null));
        }
        return list;
    }

    private static string UniqueTitle(string label) => $"EAT {label} {Guid.NewGuid():N}";

    private static async Task<int> PublishAsync(
        HttpClient doctor,
        string title,
        DateTime? opensAtUtc = null,
        DateTime? closesAtUtc = null,
        bool showAnswers = false)
    {
        var req = new QuizPublishRequest(
            CourseId: _courseId,
            MaterialId: null,
            Title: title,
            Language: "en",
            Marks: 10,
            Questions: ValidQuestions(),
            OpensAtUtc: opensAtUtc,
            ClosesAtUtc: closesAtUtc,
            DurationMinutes: null,
            ShowAnswersAfterSubmit: showAnswers);
        var resp = await doctor.PostAsJsonAsync("/api/doctor/ai/quiz/publish", req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<QuizPublishResponse>())!.ExamId;
    }

    private static List<SubmitExamAnswerDto> Choices(params (int QuestionId, int option)[] picks) =>
        picks.Select(p => new SubmitExamAnswerDto(p.QuestionId, p.option)).ToList();

    private static async Task<(int AttemptId, List<int> QuestionIds, StudentExamAttemptStateDto State)>
        StartExamAsync(HttpClient student, int examId)
    {
        var resp = await student.PostAsync($"/api/student/exams/{examId}/start", null);
        resp.EnsureSuccessStatusCode();
        var state = (await resp.Content.ReadFromJsonAsync<StudentExamAttemptStateDto>())!;
        return (state.Attempt!.AttemptId, state.TakeQuestions!.Select(q => q.QuestionId).ToList(), state);
    }

    // ---------- 1-3: publish → take → server grading persists ----------

    [Fact]
    public async Task Publish_Then_Take_Then_Submit_Stores_Score_And_Answers()
    {
        await SeedAsync();
        var title = UniqueTitle("Grade");
        var examId = await PublishAsync(await DoctorAsync(), title);

        var student = await StudentAsync(_enrolled1Email);
        var (_, questionIds, state) = await StartExamAsync(student, examId);

        // Take payload: options only — no correct-answer info leaks.
        var takeRaw = await student.GetStringAsync($"/api/student/exams/{examId}/attempt");
        Assert.DoesNotContain(ExplanationSecret, takeRaw);
        Assert.DoesNotContain("correctanswer", takeRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iscorrect", takeRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctoption", takeRaw, StringComparison.OrdinalIgnoreCase);

        // q1=correct, q2=wrong, q3 unanswered → 1/3, graded server-side.
        var answer = new SubmitExamAttemptRequest(Choices(
            (questionIds[0], 1),
            (questionIds[1], 0)));
        var resp = await student.PostAsJsonAsync($"/api/student/exams/{examId}/submit", answer);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var submitted = (await resp.Content.ReadFromJsonAsync<StudentExamAttemptStateDto>())!;
        Assert.NotNull(submitted.Attempt);
        Assert.NotNull(submitted.Attempt.SubmittedAtUtc);
        Assert.Equal(1, submitted.Attempt.Score);
        Assert.Equal(3, submitted.Attempt.MaxScore);
        // Without ShowAnswersAfterSubmit the review must stay answer-blind.
        Assert.NotNull(submitted.Review);
        Assert.All(submitted.Review, r =>
        {
            Assert.Null(r.IsCorrect);
            Assert.Null(r.CorrectOption);
            Assert.DoesNotContain(ExplanationSecret, JsonSerializer.Serialize(r));
        });
        // The chosen options still come back for the review UI.
        Assert.Contains(submitted.Review, r => r.QuestionId == questionIds[0] && r.SelectedOption == 1);
        Assert.Contains(submitted.Review, r => r.QuestionId == questionIds[1] && r.SelectedOption == 0);
        Assert.Contains(submitted.Review, r => r.QuestionId == questionIds[2] && r.SelectedOption is null);

        // Everything persisted via EF Core: attempt + per-answer correctness.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = await db.ExamAttempts.AsNoTracking()
            .Include(a => a.Answers)
            .SingleAsync(a => a.ExamId == examId && a.StudentUserId == _enrolledIds[0]);
        Assert.NotNull(attempt.SubmittedAtUtc);
        Assert.Equal(1, attempt.Score);
        Assert.Equal(3, attempt.MaxScore);
        Assert.Equal(3, attempt.Answers.Count);
        Assert.Equal(1, attempt.Answers.Single(x => x.QuestionId == questionIds[0]).SelectedOption);
        Assert.True(attempt.Answers.Single(x => x.QuestionId == questionIds[0]).IsCorrect);
        Assert.False(attempt.Answers.Single(x => x.QuestionId == questionIds[1]).IsCorrect);
        Assert.True(attempt.Answers.Single(x => x.QuestionId == questionIds[2]).SelectedOption is null
                    && !attempt.Answers.Single(x => x.QuestionId == questionIds[2]).IsCorrect);
    }

    [Fact]
    public async Task All_Correct_Submission_Scores_Full_Marks()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Perfect"));

        var student = await StudentAsync(_enrolled2Email);
        var (_, questionIds, _) = await StartExamAsync(student, examId);
        var resp = await student.PostAsJsonAsync($"/api/student/exams/{examId}/submit",
            new SubmitExamAttemptRequest(questionIds.Select(id => new SubmitExamAnswerDto(id, 1)).ToList()));
        resp.EnsureSuccessStatusCode();
        var state = (await resp.Content.ReadFromJsonAsync<StudentExamAttemptStateDto>())!;
        Assert.Equal(3, state.Attempt!.Score);
        Assert.Equal(3, state.Attempt.MaxScore);
    }

    [Fact]
    public async Task Submitted_Attempt_Shows_In_Own_Attempt_List()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Chip"));

        // Student 2 takes and finishes it.
        var s2 = await StudentAsync(_enrolled2Email);
        var (attemptId, questionIds, _) = await StartExamAsync(s2, examId);
        (await s2.PostAsJsonAsync($"/api/student/exams/{examId}/submit",
            new SubmitExamAttemptRequest(Choices((questionIds[0], 1))))
            ).EnsureSuccessStatusCode();

        // Own list shows exactly that row (score + submission date)...
        var rows = await s2.GetFromJsonAsync<List<StudentAttemptListRowDto>>("/api/student/exams/attempts");
        var row = rows!.Single(r => r.ExamId == examId);
        Assert.Equal(attemptId, row.AttemptId);
        Assert.Equal(1, row.Score);
        Assert.Equal(3, row.MaxScore);
        Assert.NotNull(row.SubmittedAtUtc);

        // ...while student 1 has nothing there (own attempts only).
        var s1 = await StudentAsync(_enrolled1Email);
        var own = await s1.GetFromJsonAsync<List<StudentAttemptListRowDto>>("/api/student/exams/attempts");
        Assert.NotNull(own);
        Assert.DoesNotContain(own, r => r.ExamId == examId);
    }

    // ---------- 4: show-answers permission gates the review ----------

    [Fact]
    public async Task ShowAnswersAfterSubmit_Reveals_Correctness_Only_When_Allowed()
    {
        await SeedAsync();
        var doctor = await DoctorAsync();

        // Default exam: doctor did NOT allow showing answers.
        var plainId = await PublishAsync(doctor, UniqueTitle("NoReveal"));
        var s1 = await StudentAsync(_enrolled1Email);
        var (_, q1, _) = await StartExamAsync(s1, plainId);
        var plainResp = await s1.PostAsJsonAsync($"/api/student/exams/{plainId}/submit",
            new SubmitExamAttemptRequest(Choices((q1[0], 1))));
        var plain = (await plainResp.Content.ReadFromJsonAsync<StudentExamAttemptStateDto>())!;
        Assert.False(plain.ShowAnswersAfterSubmit);
        Assert.All(plain.Review!, r => { Assert.Null(r.IsCorrect); Assert.Null(r.CorrectOption); });
        var plainRaw = JsonSerializer.Serialize(plain);
        Assert.DoesNotContain(ExplanationSecret, plainRaw);

        // Second exam: doctor allows showing answers after submit (the flag
        // travels through the real publish request → exam column).
        var revealId = await PublishAsync(doctor, UniqueTitle("Reveal"), showAnswers: true);
        var s2 = await StudentAsync(_enrolled2Email);
        var (_, q2, _) = await StartExamAsync(s2, revealId);
        var reveal = (await (await s2.PostAsJsonAsync($"/api/student/exams/{revealId}/submit",
            new SubmitExamAttemptRequest(Choices((q2[0], 1), (q2[1], 0)))))
            .Content.ReadFromJsonAsync<StudentExamAttemptStateDto>())!;
        Assert.True(reveal.ShowAnswersAfterSubmit);
        Assert.True(reveal.Review!.Single(r => r.QuestionId == q2[0]).IsCorrect);
        Assert.Equal(1, reveal.Review!.Single(r => r.QuestionId == q2[0]).CorrectOption);
        Assert.False(reveal.Review!.Single(r => r.QuestionId == q2[1]).IsCorrect);
        // Even with permission, explanations are never sent to students.
        Assert.DoesNotContain(ExplanationSecret, JsonSerializer.Serialize(reveal));
    }

    // ---------- 5: second attempt rejected ----------

    [Fact]
    public async Task Second_Attempt_Is_Rejected_With_409()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Once"));

        var student = await StudentAsync(_enrolled1Email);
        var (_, questionIds, _) = await StartExamAsync(student, examId);
        var submit = new SubmitExamAttemptRequest(
            questionIds.Select(id => new SubmitExamAnswerDto(id, 1)).ToList());
        (await student.PostAsJsonAsync($"/api/student/exams/{examId}/submit", submit))
            .EnsureSuccessStatusCode();

        // Restarting after submission → 409 (unique one-attempt-per-student).
        var secondStart = await student.PostAsync($"/api/student/exams/{examId}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, secondStart.StatusCode);
        var conflictBody = await secondStart.Content.ReadAsStringAsync();
        Assert.Contains("already submitted", conflictBody, StringComparison.OrdinalIgnoreCase);

        // Re-submitting the same attempt → 409 too.
        var secondSubmit = await student.PostAsJsonAsync($"/api/student/exams/{examId}/submit", submit);
        Assert.Equal(HttpStatusCode.Conflict, secondSubmit.StatusCode);
    }

    // ---------- 6: exam window enforcement ----------

    [Fact]
    public async Task Exam_Window_Is_Enforced_Server_Side()
    {
        await SeedAsync();
        var doctor = await DoctorAsync();
        var title = UniqueTitle("Window");
        var examId = await PublishAsync(doctor, title,
            opensAtUtc: DateTime.UtcNow.AddHours(1),
            closesAtUtc: DateTime.UtcNow.AddDays(7));

        // The publish must persist the window.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == examId);
            Assert.NotNull(exam.OpensAtUtc);
            Assert.NotNull(exam.ClosesAtUtc);
        }

        var student = await StudentAsync(_enrolled1Email);

        // Not open yet → 409.
        var early = await student.PostAsync($"/api/student/exams/{examId}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("not open yet", await early.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // Open the window server-side (test clock), then start works and the
        // attempt row exists with StartedAtUtc.
        ShiftWindow(examId, opensAt: DateTime.UtcNow.AddMinutes(-5));
        var (_, questionIds, state) = await StartExamAsync(student, examId);
        Assert.True(state.Attempt!.StartedAtUtc <= DateTime.UtcNow.ToUniversalTime());

        // Close the window server-side; the deadline is enforced at submit.
        ShiftWindow(examId, closesAt: DateTime.UtcNow.AddMinutes(-1));
        var late = await student.PostAsJsonAsync($"/api/student/exams/{examId}/submit",
            new SubmitExamAttemptRequest(Choices((questionIds[0], 1))));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        var closedBody = await late.Content.ReadAsStringAsync();
        Assert.Contains("closed", closedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Published_Window_Is_Persisted_And_Served_To_Students()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("WindowMeta"),
            opensAtUtc: DateTime.UtcNow.AddDays(-1),
            closesAtUtc: DateTime.UtcNow.AddDays(2));

        var student = await StudentAsync(_enrolled1Email);
        var state = await student.GetFromJsonAsync<StudentExamAttemptStateDto>(
            $"/api/student/exams/{examId}/attempt");
        Assert.NotNull(state);
        Assert.Equal(examId, state.ExamId);
        Assert.Equal("Published", state.Status);
        Assert.NotNull(state.OpensAtUtc);
        Assert.NotNull(state.ClosesAtUtc);
        // Duration was not set → absent, not fabricated.
        Assert.Null(state.DurationMinutes);
        Assert.False(state.ShowAnswersAfterSubmit);
        // No attempt rows exist yet for anyone.
        Assert.Null(state.Attempt);
        Assert.Null(state.TakeQuestions);
        Assert.Null(state.Review);
    }

    // ---------- 7: doctor results ----------

    [Fact]
    public async Task Doctor_Results_List_Takers_With_Names_Scores_And_NonTakers()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Results"));

        // Student 1: 2/3. Student 2: 3/3.
        var s1 = await StudentAsync(_enrolled1Email);
        var (_, q1, _) = await StartExamAsync(s1, examId);
        (await s1.PostAsJsonAsync($"/api/student/exams/{examId}/submit",
            new SubmitExamAttemptRequest(Choices((q1[0], 1), (q1[1], 1), (q1[2], 0))))).EnsureSuccessStatusCode();

        var s2 = await StudentAsync(_enrolled2Email);
        var (_, q2, _) = await StartExamAsync(s2, examId);
        (await s2.PostAsJsonAsync($"/api/student/exams/{examId}/submit",
            new SubmitExamAttemptRequest(Choices((q2[0], 1), (q2[1], 1), (q2[2], 1))))).EnsureSuccessStatusCode();

        var doctor = await DoctorAsync();
        var results = await doctor.GetFromJsonAsync<DoctorExamResultsDto>(
            $"/api/doctor/exam-assistant/exams/{examId}/results");
        Assert.NotNull(results);
        Assert.Equal(2, results.EligibleCount);
        Assert.Equal(2, results.Takers.Count);
        Assert.Empty(results.NonTakers);

        var first = results.Takers.Single(t => t.FullName == "Enrolled Two");
        Assert.Equal(3, first.Score);
        Assert.Equal(3, first.MaxScore);
        Assert.Equal(100.0, first.Percent!.Value);

        var second = results.Takers.Single(t => t.FullName == "Enrolled One");
        Assert.Equal(2, second.Score);
        Assert.Equal(3, second.MaxScore);
        Assert.Equal(66.7, second.Percent!.Value);
        Assert.True(second.SubmittedAtUtc > DateTime.MinValue);

        // A submitted attempt means the student is no longer in non-takers.
        Assert.DoesNotContain(results.NonTakers, n => n.FullName is "Enrolled One" or "Enrolled Two");

        // Ineligible students (finished the course / never enrolled) never appear at all.
        Assert.DoesNotContain(results.Takers, t => t.FullName == "Graded Student");
        Assert.DoesNotContain(results.NonTakers, n => n.FullName == "Graded Student");
        Assert.DoesNotContain(results.NonTakers, n => n.FullName == "Not Enrolled");
    }

    [Fact]
    public async Task Doctor_Results_Separates_NonTakers_Before_Submission()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Split"));

        // Only student 1 takes it.
        var s1 = await StudentAsync(_enrolled1Email);
        var (_, _, _) = await StartExamAsync(s1, examId);

        var results = await (await DoctorAsync()).GetFromJsonAsync<DoctorExamResultsDto>(
            $"/api/doctor/exam-assistant/exams/{examId}/results");
        Assert.NotNull(results);
        // Started-but-not-submitted is NOT "took the exam".
        Assert.Empty(results.Takers);
        var nonTaker = results.NonTakers.Single(n => n.StudentUserId == _enrolledIds[1]);
        Assert.Equal("Enrolled Two", nonTaker.FullName);
    }

    [Fact]
    public async Task Foreign_Doctor_Reads_Results_As_404_And_Student_Gets_403()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Guard"));

        var otherDoctor = await OtherDoctorAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherDoctor.GetAsync($"/api/doctor/exam-assistant/exams/{examId}/results")).StatusCode);

        // Students cannot pass the Doctor role gate at all.
        var student = await StudentAsync(_enrolled1Email);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await student.GetAsync($"/api/doctor/exam-assistant/exams/{examId}/results")).StatusCode);

        // The owning doctor reads them fine.
        Assert.Equal(HttpStatusCode.OK,
            (await (await DoctorAsync()).GetAsync($"/api/doctor/exam-assistant/exams/{examId}/results")).StatusCode);
    }

    // ---------- 8: other students cannot see or take the exam ----------

    [Fact]
    public async Task Foreign_Students_Get_404_On_Attempt_Endpoints()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("NoView"));

        // Never-enrolled student: state / start / submit all 404.
        var lonely = await StudentAsync(_notEnrolledEmail);
        Assert.Equal(HttpStatusCode.NotFound,
            (await lonely.GetAsync($"/api/student/exams/{examId}/attempt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await lonely.PostAsync($"/api/student/exams/{examId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await lonely.PostAsJsonAsync($"/api/student/exams/{examId}/submit",
                new SubmitExamAttemptRequest(Array.Empty<SubmitExamAnswerDto>()))).StatusCode);

        // Graded student (course finished): also hidden.
        var graded = await StudentAsync(_gradedEmail);
        Assert.Equal(HttpStatusCode.NotFound,
            (await graded.GetAsync($"/api/student/exams/{examId}/attempt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await graded.PostAsync($"/api/student/exams/{examId}/start", null)).StatusCode);

        // A foreign attempt list stays empty for ineligible students.
        var rows = await graded.GetFromJsonAsync<List<StudentAttemptListRowDto>>("/api/student/exams/attempts");
        Assert.NotNull(rows);
        Assert.DoesNotContain(rows, r => r.ExamId == examId);
    }

    [Fact]
    public async Task Submit_Without_Start_Is_409_Not_A_New_Attempt()
    {
        await SeedAsync();
        var examId = await PublishAsync(await DoctorAsync(), UniqueTitle("Order"));

        var student = await StudentAsync(_enrolled1Email);
        var resp = await student.PostAsJsonAsync(
            $"/api/student/exams/{examId}/submit",
            new SubmitExamAttemptRequest(Array.Empty<SubmitExamAnswerDto>()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        // No attempt row was created behind the scenes.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ExamAttempts.AnyAsync(a => a.ExamId == examId));
    }

    // ---------- helper: move the exam window in the test database ----------

    private void ShiftWindow(int examId, DateTime? opensAt = null, DateTime? closesAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exam = db.Exams.Single(e => e.Id == examId);
        if (opensAt is not null) exam.OpensAtUtc = opensAt;
        if (closesAt is not null) exam.ClosesAtUtc = closesAt;
        db.SaveChanges();
    }
}
