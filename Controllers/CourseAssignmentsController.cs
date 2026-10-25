using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>
/// Admin-only management of doctor ↔ course assignments for the doctor
/// module. Doctors can never assign themselves to a course: only this
/// controller (Admin role) can create or remove assignments.
/// University isolation follows this project's rules: an admin whose account
/// has a university sees and manages only that university's assignments;
/// foreign or unmatched ids resolve to 404 as though they did not exist.
/// A global admin (no university) keeps the wider view.
/// </summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/course-assignments")]
public class CourseAssignmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;
    public CourseAssignmentsController(AppDbContext db, IUniversityScope scope)
    {
        _db = db;
        _scope = scope;
    }

    private static CourseAssignmentDto ToDto(CourseAssignment a, Course course, User doctor) =>
        new(a.Id, course.Id, course.Code, course.Title, doctor.Id, doctor.FullName, a.AssignedAtUtc);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CourseAssignmentDto>>> List(
        [FromQuery] int? courseId = null, [FromQuery] int? doctorUserId = null)
    {
        var scope = await _scope.GetAsync();

        var query = _db.CourseAssignments.AsNoTracking()
            .Join(_db.Courses, a => a.CourseId, c => c.Id, (a, c) => new { a, c })
            .Join(_db.Users, x => x.a.DoctorUserId, u => u.Id, (x, u) => new { x.a, x.c, u });

        // University isolation: an admin bound to a university only ever
        // manages the assignments of that university's courses.
        if (scope.UniversityId is not null)
        {
            var universityId = scope.UniversityId.Value;
            query = query.Where(x => x.c.UniversityId == universityId);
        }

        if (courseId.HasValue) query = query.Where(x => x.c.Id == courseId.Value);
        if (doctorUserId.HasValue) query = query.Where(x => x.u.Id == doctorUserId.Value);

        var list = await query
            .OrderBy(x => x.c.Code).ThenBy(x => x.u.FullName)
            .Select(x => new CourseAssignmentDto(
                x.a.Id, x.c.Id, x.c.Code, x.c.Title, x.u.Id, x.u.FullName, x.a.AssignedAtUtc))
            .ToListAsync();
        return list;
    }

    [HttpPost]
    public async Task<ActionResult<CourseAssignmentDto>> Create([FromBody] CreateCourseAssignmentRequest req)
    {
        var scope = await _scope.GetAsync();

        var doctor = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.DoctorUserId);
        if (doctor is null || doctor.IsDeleted) return NotFound("Doctor not found.");
        if (doctor.Role != UserRole.Doctor)
            return BadRequest("The selected user is not a doctor.");

        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == req.CourseId);
        if (course is null) return NotFound("Course not found.");

        // University isolation: foreign/unmatched combinations are
        // indistinguishable from missing rows. Admins bound to a university
        // may only touch their own university's courses and doctors.
        if (scope.UniversityId is not null)
        {
            var universityId = scope.UniversityId.Value;
            if (course.UniversityId != universityId || doctor.UniversityId != universityId)
                return NotFound();
        }
        else if (course.UniversityId is not null
                 && doctor.UniversityId is not null
                 && course.UniversityId != doctor.UniversityId)
        {
            return NotFound();
        }

        var exists = await _db.CourseAssignments
            .AnyAsync(a => a.DoctorUserId == req.DoctorUserId && a.CourseId == req.CourseId);
        if (exists) return Conflict("This doctor is already assigned to the course.");

        var assignment = new CourseAssignment
        {
            DoctorUserId = doctor.Id,
            CourseId = course.Id,
            AssignedByUserId = User.GetUserId(),
            AssignedAtUtc = DateTime.UtcNow
        };
        _db.CourseAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { courseId = course.Id }, ToDto(assignment, course, doctor));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var scope = await _scope.GetAsync();

        var assignment = await _db.CourseAssignments
            .Join(_db.Courses, a => a.CourseId, c => c.Id, (a, c) => new { a, c })
            .FirstOrDefaultAsync(x => x.a.Id == id);
        if (assignment is null) return NotFound();

        if (scope.UniversityId is not null && assignment.c.UniversityId != scope.UniversityId)
            return NotFound();

        _db.CourseAssignments.Remove(assignment.a);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
