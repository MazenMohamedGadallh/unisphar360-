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
    private readonly DoctorAccessService _access;
    public EnrollmentsController(AppDbContext db, DoctorAccessService access)
    {
        _db = db;
        _access = access;
    }

    /// <summary>Get the logged-in user's enrollments with course details.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EnrollmentDto>>> GetMyEnrollments()
    {
        var userId = User.GetUserId();
        var enrollments = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Include(e => e.Course)
            .OrderBy(e => e.Course!.Code)
            .Select(e => new EnrollmentDto(
                e.Id, e.CourseId, e.Course!.Code, e.Course.Title,
                e.Semester, e.GradeLetter, e.GradePoints, e.AttendancePercent, e.Status))
            .ToListAsync();

        return enrollments;
    }

    /// <summary>Enroll the logged-in user in a course.</summary>
    [HttpPost]
    public async Task<ActionResult<EnrollmentDto>> Enroll([FromBody] CreateEnrollmentRequest req)
    {
        var userId = User.GetUserId();

        // Validate course exists
        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CourseId);
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

        _db.Enrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        // Add activity
        _db.Activities.Add(new ActivityItem
        {
            UserId = userId,
            Kind = "enrollment",
            Message = $"Enrolled in {course.Code} - {course.Title}"
        });
        await _db.SaveChangesAsync();

        return Ok(new EnrollmentDto(
            enrollment.Id, course.Id, course.Code, course.Title,
            enrollment.Semester, enrollment.GradeLetter, enrollment.GradePoints,
            enrollment.AttendancePercent, enrollment.Status));
    }

    /// <summary>Drop a course (only if no grade assigned yet).</summary>
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
    /// Doctors may only grade courses they are assigned to (Admins bypass).</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpPut("{id:int}/grade")]
    public async Task<ActionResult<EnrollmentDto>> UpdateGrade(int id, [FromBody] UpdateGradeRequest req)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (enrollment is null) return NotFound();

        if (User.IsInRole(UserRole.Doctor.ToString()) && !User.IsInRole(UserRole.Admin.ToString()))
        {
            var allowed = await _access.IsAssignedToCourseAsync(User.GetUserId(), enrollment.CourseId);
            if (!allowed) return Forbid();
        }

        // Validate attendance
        if (req.AttendancePercent is < 0 or > 100)
            return BadRequest("Attendance must be between 0 and 100.");

        // Validate grade points
        if (req.GradePoints is < 0 or > 4.0)
            return BadRequest("Grade points must be between 0 and 4.0.");

        enrollment.GradeLetter = req.GradeLetter ?? enrollment.GradeLetter;
        // When a doctor/admin assigns a letter without points (the UI quiz flow),
        // derive the points from the institutional 4.0 scale so letter and GPA
        // can never disagree.
        enrollment.GradePoints = req.GradePoints
            ?? (string.IsNullOrWhiteSpace(req.GradeLetter)
                ? enrollment.GradePoints
                : AcademicService.LetterToPoints(req.GradeLetter));
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

    /// <summary>Admin/Doctor: Get all enrollments (optionally filtered by userId).
    /// Doctors see only courses they are assigned to; Admins see all.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet("all")]
    public async Task<ActionResult<IReadOnlyList<EnrollmentDto>>> GetAll([FromQuery] int? userId = null)
    {
        var query = _db.Enrollments.AsNoTracking().Include(e => e.Course).AsQueryable();

        if (User.IsInRole(UserRole.Doctor.ToString()) && !User.IsInRole(UserRole.Admin.ToString()))
        {
            var courseIds = await _access.GetAssignedCourseIdsAsync(User.GetUserId());
            query = query.Where(e => courseIds.Contains(e.CourseId));
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

public record CreateEnrollmentRequest(int CourseId);
public record UpdateGradeRequest(string? GradeLetter, double? GradePoints, int? AttendancePercent, string? Status);
public record EnrollmentDto(int Id, int CourseId, string CourseCode, string CourseTitle, string Semester, string GradeLetter, double GradePoints, int AttendancePercent, string Status);
