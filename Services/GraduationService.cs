using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Graduation tracking: eligibility evaluation, degree audit applications,
/// ceremony lookup, certificates and the admin review flow. An approved
/// application is the conferral — the holder's GraduationYear/GraduatedFrom
/// are stamped from it and notification + activity records are emitted.
/// </summary>
public class GraduationService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string AppIdPrefix = "UNI-GRAD-";
    private const string DefaultInstitutionName = "UniSphere 360+";
    private static readonly string[] CeremonyKeywords = { "graduation", "ceremony" };

    // ---- Degree policy (single source of truth for every endpoint) ----
    public const double MinGpa = 2.0;
    public const double HonorsSummaCumLaude = 3.9;
    public const double HonorsMagnaCumLaude = 3.7;
    public const double HonorsCumLaude = 3.5;
    public const double HonorsDistinction = 3.0;
    public const int MaxExpectedYearsAhead = 5;

    private readonly AppDbContext _db;
    public GraduationService(AppDbContext db) => _db = db;

    /// <summary>Latin honors label for the conferred GPA.</summary>
    public static string HonorsFor(double gpa) =>
        gpa >= HonorsSummaCumLaude ? "Summa Cum Laude"
        : gpa >= HonorsMagnaCumLaude ? "Magna Cum Laude"
        : gpa >= HonorsCumLaude ? "Cum Laude"
        : gpa >= HonorsDistinction ? "With Distinction"
        : "Pass";

    /// <summary>Everything the Graduation screen needs in one call.</summary>
    public async Task<GraduationOverviewResponse?> GetOverviewAsync(int userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var snapshot = await EvaluateEligibilityAsync(user);
        var application = await GetApplicationAsync(userId);
        var universityName = await GetUniversityNameAsync(user.UniversityId);

        GraduationAlumniDto? alumni = null;
        if (user.Role == UserRole.Graduate)
        {
            var final = await _db.SemesterRecords.AsNoTracking()
                .Where(r => r.UserId == userId && (r.IsCurrent || r.Label == "Final"))
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync();
            var finalGpa = final?.Gpa ?? snapshot.Gpa;
            alumni = new GraduationAlumniDto(
                user.GraduatedFrom ?? universityName ?? DefaultInstitutionName,
                user.GraduationYear, finalGpa, HonorsFor(finalGpa),
                HasCertificate: user.GraduationYear is not null);
        }

        return new GraduationOverviewResponse(
            user.Role.ToString(),
            IsGraduated: user.Role == UserRole.Graduate || application?.Status == GraduationStatus.Approved,
            Degree: string.IsNullOrWhiteSpace(user.Major) ? "Undeclared" : user.Major,
            Department: user.Department,
            UniversityName: universityName ?? "",
            AcademicYear: user.AcademicYear,
            Gpa: snapshot.Gpa,
            MinGpaRequired: MinGpa,
            CoursesCompleted: snapshot.CoursesCompleted,
            CoursesRequired: snapshot.CoursesRequired,
            CompletionPercent: snapshot.CompletionPercent,
            CreditsCompleted: snapshot.CreditsCompleted,
            CreditsInProgress: snapshot.CreditsInProgress,
            FailedCourses: snapshot.FailedCourses,
            IsEligible: snapshot.BlockingReasons.Count == 0,
            BlockingReasons: snapshot.BlockingReasons,
            Checklist: BuildChecklist(user, snapshot, application),
            ApplicationStatus: application?.Status ?? "none",
            Application: application,
            Alumni: alumni);
    }

    private static IReadOnlyList<GraduationChecklistItemDto> BuildChecklist(
        User user, EligibilitySnapshot snapshot, GraduationApplicationSummaryDto? application)
    {
        var items = new List<GraduationChecklistItemDto>
        {
            new("gpa", "Grade point average",
                $"Your current GPA is {snapshot.Gpa.ToString("0.00", Inv)} (minimum required {MinGpa.ToString("0.00", Inv)}).",
                snapshot.Gpa >= MinGpa),
            new("coursework", "All coursework completed",
                $"You completed {snapshot.CoursesCompleted} of {snapshot.CoursesRequired} planned courses ({snapshot.CompletionPercent}%).",
                snapshot.CoursesRequired > 0 && snapshot.CoursesCompleted >= snapshot.CoursesRequired),
            new("passed-courses", "No failed courses",
                snapshot.FailedCourses.Count == 0
                    ? "Every course you took has a passing grade."
                    : $"Retake and pass: {string.Join(", ", snapshot.FailedCourses.Take(3))}",
                snapshot.FailedCourses.Count == 0),
        };
        if (user.Role == UserRole.Student)
        {
            items.Add(new("application-submitted", "Application submitted",
                application is null
                    ? "You have not submitted a graduation application yet."
                    : $"Application #{application.Id} is {application.Status}.",
                application is not null && application.Status != GraduationStatus.Withdrawn
                                        && application.Status != GraduationStatus.Rejected));
            items.Add(new("registrar-review", "Registrar review",
                application is null
                    ? "The registrar reviews completed applications."
                    : $"Application #{application.Id} is {application.Status}.",
                application?.Status is GraduationStatus.InReview or GraduationStatus.Approved));
        }
        return items;
    }

    // ---- Eligibility engine ----

    private sealed record EligibilitySnapshot(
        double Gpa,
        int CoursesCompleted, int CoursesRequired, int CompletionPercent,
        int CreditsCompleted, int CreditsInProgress,
        List<string> FailedCourses,
        List<string> BlockingReasons);

    private async Task<EligibilitySnapshot> EvaluateEligibilityAsync(User user)
    {
        var records = await _db.SemesterRecords.AsNoTracking()
            .Where(r => r.UserId == user.Id).ToListAsync();
        var current = records.FirstOrDefault(r => r.IsCurrent);

        var enrollments = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Course)
            .Where(e => e.UserId == user.Id).ToListAsync();

        var gpa = current?.Gpa ?? 0;
        var coursesRequired = current?.TotalCourses ?? 0;
        var coursesCompleted = current?.CompletedCourses ?? 0;
        var completionPercent = coursesRequired == 0
            ? 0
            : (int)Math.Round(100.0 * coursesCompleted / coursesRequired);

        var failed = enrollments
            .Where(e => e.GradePoints < MinGpa)
            .Select(e => e.Course?.Title ?? $"Course #{e.CourseId}")
            .Distinct()
            .Order().ToList();
        var creditsCompleted = enrollments
            .Where(e => e.GradePoints >= MinGpa)
            .Sum(e => e.Course?.Credits ?? 0);
        var creditsInProgress = enrollments
            .Where(e => e.GradePoints < MinGpa)
            .Sum(e => e.Course?.Credits ?? 0);

        var reasons = new List<string>();
        if (gpa < MinGpa)
            reasons.Add($"Your GPA ({gpa.ToString("0.00", Inv)}) is below the required minimum of {MinGpa.ToString("0.00", Inv)}.");
        if (coursesRequired == 0 || coursesCompleted < coursesRequired)
            reasons.Add($"You completed {coursesCompleted} of {coursesRequired} planned courses ({completionPercent}%); your entire coursework must be finished before applying.");
        if (failed.Count > 0)
            reasons.Add($"You must retake and pass {failed.Count} failed course(s): {string.Join(", ", failed)}.");

        return new EligibilitySnapshot(
            gpa, coursesCompleted, coursesRequired, completionPercent,
            creditsCompleted, creditsInProgress, failed, reasons);
    }

    // ---- Own application ----

    private async Task<GraduationApplicationSummaryDto?> GetApplicationAsync(int userId)
    {
        var apps = await _db.GraduationApplications.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.Id).ToListAsync();

        // Active ones win (they drive the UI); otherwise show the latest decision.
        var app = apps.FirstOrDefault(g => GraduationStatus.ActiveStatuses.Contains(g.Status)) ?? apps.FirstOrDefault();
        return app is null ? null : ToSummary(app);
    }

    private static GraduationApplicationSummaryDto ToSummary(GraduationApplication g) =>
        new(g.Id, g.Status, g.SubmittedAtUtc, g.ReviewedAtUtc, g.DecisionNote, g.ConferredGraduationYear);

    // ---- Application lifecycle ----

    public enum GraduationApplyOutcome
    {
        Applied, UserNotFound, NotStudent, AlreadyGraduated, ActiveApplicationExists, NotEligible
    }

    public sealed record GraduationApplyResult(
        GraduationApplyOutcome Outcome,
        GraduationApplicationSummaryDto? Application,
        IReadOnlyList<string>? BlockingReasons);

    public async Task<GraduationApplyResult> ApplyAsync(int userId, GraduationApplyRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.IsDeleted) return new(GraduationApplyOutcome.UserNotFound, null, null);
        if (!user.IsActive) return new(GraduationApplyOutcome.UserNotFound, null, null);
        if (user.Role == UserRole.Graduate || user.GraduationYear is not null)
            return new(GraduationApplyOutcome.AlreadyGraduated, null, null);
        if (user.Role != UserRole.Student)
            return new(GraduationApplyOutcome.NotStudent, null, null);

        var existing = await FindActiveApplicationAsync(userId);
        if (existing is not null)
            return new(GraduationApplyOutcome.ActiveApplicationExists, ToSummary(existing), null);

        var snapshot = await EvaluateEligibilityAsync(user);
        if (snapshot.BlockingReasons.Count > 0)
            return new(GraduationApplyOutcome.NotEligible, null, snapshot.BlockingReasons);

        var app = new GraduationApplication
        {
            UserId = userId,
            Status = GraduationStatus.Pending,
            Degree = string.IsNullOrWhiteSpace(user.Major) ? "Undeclared" : user.Major,
            Department = user.Department,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ExpectedGraduationYear = request.ExpectedGraduationYear,
            GpaAtApplication = Math.Round(snapshot.Gpa, 2),
            CoursesCompleted = snapshot.CoursesCompleted,
            CoursesRequired = snapshot.CoursesRequired,
            SubmittedAtUtc = DateTime.UtcNow
        };
        _db.GraduationApplications.Add(app);
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Graduation application received",
            Body = $"Your graduation application ({app.Degree}) was submitted and is now pending registrar review."
        });
        _db.Activities.Add(new ActivityItem
        {
            UserId = userId,
            Kind = "graduation",
            Message = "You submitted your graduation application"
        });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race (e.g. parallel request): the partial unique index on
            // (UserId, Status) prevented a second active application. Surface it.
            _db.ChangeTracker.Clear();
            var dupe = await FindActiveApplicationAsync(userId);
            return new(GraduationApplyOutcome.ActiveApplicationExists,
                dupe is null ? null : ToSummary(dupe), null);
        }

        return new(GraduationApplyOutcome.Applied, ToSummary(app), null);
    }

    public enum GraduationWithdrawOutcome { Withdrawn, NotFound }

    public sealed record GraduationWithdrawResult(
        GraduationWithdrawOutcome Outcome, GraduationApplicationSummaryDto? Application);

    /// <summary>Closes a pending / in-review application; finalized ones
    /// (approved, rejected, already withdrawn) cannot be withdrawn.</summary>
    public async Task<GraduationWithdrawResult> WithdrawAsync(int userId)
    {
        var app = await FindActiveApplicationAsync(userId);
        if (app is null)
            return new(GraduationWithdrawOutcome.NotFound, null);

        app.Status = GraduationStatus.Withdrawn;
        app.DecisionNote = "Withdrawn by the student.";
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Graduation application withdrawn",
            Body = "Your graduation application was withdrawn. You can re-apply at any time from the Graduation screen."
        });
        _db.Activities.Add(new ActivityItem
        {
            UserId = userId,
            Kind = "graduation",
            Message = "You withdrew your graduation application"
        });
        await _db.SaveChangesAsync();
        return new(GraduationWithdrawOutcome.Withdrawn, ToSummary(app));
    }

    private async Task<GraduationApplication?> FindActiveApplicationAsync(int userId) =>
        await _db.GraduationApplications.FirstOrDefaultAsync(g =>
            g.UserId == userId && GraduationStatus.ActiveStatuses.Contains(g.Status));

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException se
        && se.SqliteErrorCode == 19
        && se.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);

    // ---- Ceremony ----

    /// <summary>The next upcoming graduation event from the events calendar.</summary>
    public async Task<GraduationCeremonyDto?> GetNextCeremonyAsync()
    {
        var now = DateTime.UtcNow;
        var upcoming = await _db.Events.AsNoTracking()
            .Where(e => e.StartsAtUtc >= now)
            .OrderBy(e => e.StartsAtUtc)
            .ToListAsync();

        var found = upcoming.FirstOrDefault(e => CeremonyKeywords.Any(k =>
            (e.Title ?? "").ToLowerInvariant().Contains(k)
            || (e.Description ?? "").ToLowerInvariant().Contains(k)
            || (e.Location ?? "").ToLowerInvariant().Contains(k)));
        return found is null
            ? null
            : new(found.Id, found.Title, found.Location, found.StartsAtUtc, found.Description);
    }

    // ---- Certificate / diploma ----

    public enum GraduationCertificateOutcome { Issued, NotFound }

    public sealed record GraduationCertificateResult(
        GraduationCertificateOutcome Outcome, GraduationCertificateDto? Certificate);

    public async Task<GraduationCertificateResult> GetCertificateAsync(int userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(GraduationCertificateOutcome.NotFound, null);

        if (user.Role == UserRole.Graduate || user.GraduationYear is not null)
        {
            if (user.GraduationYear is null)
                return new(GraduationCertificateOutcome.NotFound, null);

            var record = await _db.SemesterRecords.AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.IsCurrent).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync();
            var gpa = record?.Gpa ?? 0;
            var university = user.GraduatedFrom
                ?? await GetUniversityNameAsync(user.UniversityId)
                ?? DefaultInstitutionName;

            return new(GraduationCertificateOutcome.Issued, new(
                $"{AppIdPrefix}A{user.Id:D5}",  // "A" = alumni record without an application file
                user.FullName,
                string.IsNullOrWhiteSpace(user.Major) ? "Undeclared" : user.Major,
                university,
                user.GraduationYear.Value, gpa, HonorsFor(gpa), null));
        }

        var app = await _db.GraduationApplications.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId && g.Status == GraduationStatus.Approved);
        if (app is null) return new(GraduationCertificateOutcome.NotFound, null);

        var uni = user.GraduatedFrom
            ?? await GetUniversityNameAsync(user.UniversityId)
            ?? DefaultInstitutionName;

        return new(GraduationCertificateOutcome.Issued, new(
            $"{AppIdPrefix}{app.Id:D5}",
            user.FullName,
            string.IsNullOrWhiteSpace(app.Degree) ? "Undeclared" : app.Degree,
            uni,
            app.ConferredGraduationYear ?? app.ExpectedGraduationYear ?? 0,
            app.GpaAtApplication, HonorsFor(app.GpaAtApplication), app.ReviewedAtUtc));
    }

    private async Task<string?> GetUniversityNameAsync(int? universityId)
    {
        if (universityId is null) return null;
        var name = await _db.Universities.AsNoTracking()
            .Where(u => u.Id == universityId.Value)
            .Select(u => u.Name).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    // ---- Admin flow ----

    public async Task<PagedResultDto<GraduationApplicationAdminDto>> GetApplicationsAsync(
        string? status, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.GraduationApplications.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim();
            query = query.Where(g => g.Status.ToLower() == wanted.ToLower());
        }

        var total = await query.CountAsync();
        var items = await query
            .Include(g => g.User)
            .OrderByDescending(g => g.SubmittedAtUtc).ThenByDescending(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GraduationApplicationAdminDto(
                g.Id, g.UserId, g.User!.FullName, g.User!.Email,
                g.Degree, g.GpaAtApplication, g.CoursesCompleted, g.CoursesRequired,
                g.Status, g.SubmittedAtUtc, g.ReviewedAtUtc, g.DecisionNote, g.ConferredGraduationYear))
            .ToListAsync();

        return new(items, total, page, pageSize);
    }

    public enum GraduationReviewOutcome { Approved, Rejected, NotFound, AlreadyFinalized }

    public sealed record GraduationReviewResult(
        GraduationReviewOutcome Outcome, GraduationApplicationSummaryDto? Application);

    public async Task<GraduationReviewResult> ReviewAsync(
        int applicationId, bool approve, string? note, int? graduationYear, int reviewerId)
    {
        var app = await _db.GraduationApplications
            .Include(g => g.User)
            .FirstOrDefaultAsync(g => g.Id == applicationId);
        if (app is null) return new(GraduationReviewOutcome.NotFound, null);
        if (GraduationStatus.IsFinal(app.Status))
            return new(GraduationReviewOutcome.AlreadyFinalized, ToSummary(app));

        var year = graduationYear ?? app.ExpectedGraduationYear ?? DateTime.UtcNow.Year;

        var decided = DateTime.UtcNow;
        app.ReviewedAtUtc = decided;
        app.ReviewedByUserId = reviewerId;
        app.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        app.ConferredGraduationYear = approve ? year : null;

        var student = app.User!;
        if (approve)
        {
            app.Status = GraduationStatus.Approved;
            // Conferral: stamp the diploma holder — this unlocks the certificate
            // and alumni screens. Role changes (Student -> Graduate) stay in the
            // hands of an admin via the user management screen.
            student.GraduationYear = year;
            if (string.IsNullOrWhiteSpace(student.GraduatedFrom))
                student.GraduatedFrom = await GetUniversityNameAsync(student.UniversityId) ?? DefaultInstitutionName;
            _db.Notifications.Add(new Notification
            {
                UserId = student.Id,
                Title = "Graduation approved",
                Body = $"Congratulations! Your graduation application was approved. Your degree ({app.Degree}) will be conferred in {year}."
            });
            _db.Activities.Add(new ActivityItem
            {
                UserId = student.Id,
                Kind = "graduation",
                Message = $"Your graduation application was approved - degree conferral {year}"
            });
        }
        else
        {
            app.Status = GraduationStatus.Rejected;
            _db.Notifications.Add(new Notification
            {
                UserId = student.Id,
                Title = "Graduation application update",
                Body = string.IsNullOrWhiteSpace(note)
                    ? "Your graduation application was not approved. Contact the registrar for details, then re-apply."
                    : $"Your graduation application was not approved: {note.Trim()}"
            });
            _db.Activities.Add(new ActivityItem
            {
                UserId = student.Id,
                Kind = "graduation",
                Message = "Your graduation application was rejected"
            });
        }

        await _db.SaveChangesAsync();
        return new(approve ? GraduationReviewOutcome.Approved : GraduationReviewOutcome.Rejected, ToSummary(app));
    }
}
