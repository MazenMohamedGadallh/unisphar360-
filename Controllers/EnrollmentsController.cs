using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EnrollmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;

    public EnrollmentsController(AppDbContext db, IUniversityScope scope)
    {
        _db = db;
        _scope = scope;
    }

    /// <summary>Get the logged-in user's enrollments with course details.
    /// Students see the doctor's display title when one is set: the
    /// doctor-module assignment row wins, legacy instructor rows still
    /// resolve their titles, otherwise the catalog title is shown.</summary>
    [Authorize(Roles = "Student")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EnrollmentDto>>> GetMyEnrollments()
    {
        var userId = User.GetUserId();
        var enrollments = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Include(e => e.Course)
            .OrderBy(e => e.Course!.Code)
            .ToListAsync();

        // Doctor-editable display title, resolved server-side so clients
        // cannot fake it (assignments win over legacy instructor rows).
        var titles = await ResolveDisplayTitlesAsync(
            enrollments.Select(e => e.CourseId).Distinct().ToList());

        return enrollments.Select(e => new EnrollmentDto(
            e.Id, e.CourseId, e.Course!.Code, titles.GetValueOrDefault(e.CourseId, e.Course!.Title),
            e.Semester, e.GradeLetter, e.GradePoints, e.AttendancePercent, e.Status)).ToList();
    }

    /// <summary>Latest non-null display title per course: doctor-module
    /// CourseAssignment rows take precedence, legacy CourseInstructor rows
    /// (and finally the catalog title) fill in.</summary>
    private async Task<Dictionary<int, string>> ResolveDisplayTitlesAsync(List<int> courseIds)
    {
        if (courseIds.Count == 0) return new Dictionary<int, string>();

        var legacy = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, courseIds);
        var titles = new Dictionary<int, string>();
        foreach (var id in courseIds)
            titles[id] = legacy.GetValueOrDefault(id, "");

        var assignments = await _db.CourseAssignments.AsNoTracking()
            .Where(a => courseIds.Contains(a.CourseId) && a.DisplayTitle != null)
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new { a.CourseId, a.DisplayTitle })
            .ToListAsync();
        foreach (var a in assignments)
            titles[a.CourseId] = a.DisplayTitle!;

        return titles;
    }

    /// <summary>Enroll the logged-in user in a course in their university.</summary>
    [Authorize(Roles = "Student")]
    [HttpPost]
    public async Task<ActionResult<EnrollmentDto>> Enroll([FromBody] CreateEnrollmentRequest req)
    {
        var userId = User.GetUserId();
        var scope = await _scope.GetAsync();

        // The course must be visible in the caller's own university; foreign
        // ids are indistinguishable from missing ids.
        var course = await _db.Courses.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .FirstOrDefaultAsync(c => c.Id == req.CourseId);
        if (course is null) return NotFound($"Course {req.CourseId} not found.");

        // Check not already enrolled
        var existing = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == req.CourseId);
        if (existing is not null)
            return Conflict("Already enrolled in this course.");

        var enrollment = new Enrollment
        {
            UserId = userId,
            CourseId = req.CourseId,
            Semester = "Current",
            GradeLetter = "",
            GradePoints = 0,
            AttendancePercent = 100,
            Status = "Good"
        };

        // Optional: a student can add a course together with their own
        // semester / grade / attendance (e.g. courses already taken).
        var recordError = ApplySelfRecord(enrollment, req.Semester, req.GradeLetter, req.AttendancePercent);
        if (recordError is not null) return BadRequest(recordError);

        _db.Enrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        var titles = await ResolveDisplayTitlesAsync(new List<int> { course.Id });
        var displayTitle = titles.TryGetValue(course.Id, out var t) && t.Length > 0
            ? t
            : course.Title;

        // Add activity
        _db.Activities.Add(new ActivityItem
        {
            UserId = userId,
            Kind = "enrollment",
            Message = $"Enrolled in {course.Code} - {displayTitle}"
        });
        await _db.SaveChangesAsync();

        return Ok(new EnrollmentDto(
            enrollment.Id, course.Id, course.Code, displayTitle,
            enrollment.Semester, enrollment.GradeLetter, enrollment.GradePoints,
            enrollment.AttendancePercent, enrollment.Status));
    }

    private static readonly HashSet<string> AllowedLetters =
        new(StringComparer.OrdinalIgnoreCase) { "A", "A-", "B+", "B", "B-", "C+", "C", "C-", "D+", "D", "F" };

    /// <summary>Validates and applies student-entered semester / grade /
    /// attendance onto an enrollment. Null arguments leave a field as is.
    /// Returns an error message, or null on success.</summary>
    private static string? ApplySelfRecord(Enrollment e, string? semester, string? gradeLetter, int? attendance)
    {
        if (semester is not null)
        {
            var sem = semester.Trim();
            if (sem.Length == 0) sem = "Current";
            if (sem.Length > 40) return "Semester is too long (max 40 characters).";
            e.Semester = sem;
        }
        if (gradeLetter is not null)
        {
            var letter = gradeLetter.Trim().ToUpperInvariant();
            if (letter.Length == 0) { e.GradeLetter = ""; e.GradePoints = 0; }
            else if (!AllowedLetters.Contains(letter)) return "Grade must be one of A, A-, B+, B, B-, C+, C, C-, D+, D, F.";
            else { e.GradeLetter = letter; e.GradePoints = AcademicService.LetterToPoints(letter); }
        }
        if (attendance is not null)
        {
            if (attendance < 0 || attendance > 100) return "Attendance must be between 0 and 100.";
            e.AttendancePercent = attendance.Value;
        }
        // Ungraded courses are judged on attendance only.
        e.Status = e.GradeLetter.Length == 0
            ? (e.AttendancePercent < 90 ? "Needs Attention" : "Good")
            : AcademicService.ComputeStatus(e.GradePoints, e.AttendancePercent);
        return null;
    }

    /// <summary>Student: record or correct their own semester, grade letter
    /// and attendance for one of their enrollments. GPA is computed from
    /// these values, so this is how a new account enters its real data.</summary>
    [Authorize(Roles = "Student")]
    [HttpPut("{id:int}/my-record")]
    public async Task<ActionResult<EnrollmentDto>> UpdateMyRecord(int id, [FromBody] UpdateMyRecordRequest req)
    {
        var userId = User.GetUserId();
        var enrollment = await _db.Enrollments.Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
        if (enrollment is null) return NotFound();

        var error = ApplySelfRecord(enrollment, req.Semester, req.GradeLetter, req.AttendancePercent);
        if (error is not null) return BadRequest(error);

        await _db.SaveChangesAsync();
        return Ok(new EnrollmentDto(
            enrollment.Id, enrollment.CourseId, enrollment.Course!.Code, enrollment.Course.Title,
            enrollment.Semester, enrollment.GradeLetter, enrollment.GradePoints,
            enrollment.AttendancePercent, enrollment.Status));
    }

    /// <summary>Drop a course (only if no grade assigned yet).</summary>
    [Authorize(Roles = "Student")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Drop(int id)
    {
        var userId = User.GetUserId();
        var enrollment = await _db.Enrollments
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (enrollment is null) return NotFound();

        if (!string.IsNullOrEmpty(enrollment.GradeLetter))
            return BadRequest("Cannot drop a course that has a grade assigned.");

        _db.Enrollments.Remove(enrollment);

        // Add activity
        _db.Activities.Add(new ActivityItem
        {
            UserId = userId,
            Kind = "enrollment",
            Message = $"Dropped {enrollment.Course?.Code} - {enrollment.Course?.Title}"
        });

        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Admin/Doctor: Update a student's grade, attendance, and status.
    /// Doctors can only touch enrollments in courses assigned to them; that
    /// check runs server-side so foreign ids return 404.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpPut("{id:int}/grade")]
    public async Task<ActionResult<EnrollmentDto>> UpdateGrade(
        int id,
        [FromBody] UpdateGradeRequest req,
        [FromServices] IValidator<UpdateGradeRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var scope = await _scope.GetAsync();
        var enrollment = await _db.Enrollments
            .VisibleToCurrentUser(scope)
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (enrollment is null) return NotFound();

        // Doctors may only grade their own assigned courses. Either the
        // doctor-module assignment or the legacy admin-managed instructor
        // row counts; otherwise the id stays indistinguishable (404).
        if (scope.Role == UserRole.Doctor)
        {
            var assigned = await _db.CourseInstructors.AsNoTracking().AnyAsync(ci =>
                ci.CourseId == enrollment.CourseId && ci.DoctorUserId == scope.UserId)
                || await _db.CourseAssignments.AsNoTracking().AnyAsync(a =>
                    a.CourseId == enrollment.CourseId && a.DoctorUserId == scope.UserId);
            if (!assigned) return NotFound();
        }

        enrollment.GradeLetter = req.GradeLetter ?? enrollment.GradeLetter;
        enrollment.GradePoints = req.GradePoints ?? enrollment.GradePoints;
        enrollment.AttendancePercent = req.AttendancePercent ?? enrollment.AttendancePercent;
        enrollment.Status = req.Status ?? enrollment.Status;

        await _db.SaveChangesAsync();

        // Add activity
        _db.Activities.Add(new ActivityItem
        {
            UserId = enrollment.UserId,
            Kind = "grade",
            Message = $"Grade updated for {enrollment.Course?.Code}: {enrollment.GradeLetter}"
        });
        await _db.SaveChangesAsync();

        return Ok(new EnrollmentDto(
            enrollment.Id, enrollment.CourseId, enrollment.Course!.Code, enrollment.Course.Title,
            enrollment.Semester, enrollment.GradeLetter, enrollment.GradePoints,
            enrollment.AttendancePercent, enrollment.Status));
    }

    /// <summary>Admin/Doctor: Get all enrollments (optionally filter by userId).
    /// Doctors only see enrollments in courses assigned to them.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet("all")]
    public async Task<ActionResult<IReadOnlyList<EnrollmentDto>>> GetAll([FromQuery] int? userId = null)
    {
        var scope = await _scope.GetAsync();
        var query = _db.Enrollments.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .Include(e => e.Course)
            .AsQueryable();

        if (scope.Role == UserRole.Doctor)
        {
            var instructorCourseIds = await _db.CourseInstructors.AsNoTracking()
                .Where(ci => ci.DoctorUserId == scope.UserId)
                .Select(ci => ci.CourseId)
                .ToListAsync();
            var assignedCourseIds = await _db.CourseAssignments.AsNoTracking()
                .Where(a => a.DoctorUserId == scope.UserId)
                .Select(a => a.CourseId)
                .ToListAsync();
            var myCourseIds = instructorCourseIds.Union(assignedCourseIds).ToList();
            query = query.Where(e => myCourseIds.Contains(e.CourseId));
        }

        if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);

        var enrollments = await query
            .OrderBy(e => e.UserId).ThenBy(e => e.Course!.Code)
            .Select(e => new EnrollmentDto(
                e.Id, e.CourseId, e.Course!.Code, e.Course.Title,
                e.Semester, e.GradeLetter, e.GradePoints, e.AttendancePercent, e.Status))
            .ToListAsync();

        return enrollments;
    }
}

public record CreateEnrollmentRequest(int CourseId, string? Semester = null, string? GradeLetter = null, int? AttendancePercent = null);
public record UpdateMyRecordRequest(string? Semester, string? GradeLetter, int? AttendancePercent);
public record UpdateGradeRequest(string? GradeLetter, double? GradePoints, int? AttendancePercent, string? Status);
public record EnrollmentDto(int Id, int CourseId, string CourseCode, string CourseTitle, string Semester, string GradeLetter, double GradePoints, int AttendancePercent, string Status);
