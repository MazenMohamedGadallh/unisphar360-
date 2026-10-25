using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AnnouncementsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;

    public AnnouncementsController(AppDbContext db, IUniversityScope scope)
    {
        _db = db;
        _scope = scope;
    }

    /// <summary>Announcements for the caller's university. Admin sees all.
    /// Students additionally see course announcements only for courses they
    /// are enrolled in; doctors only for courses they teach; graduates never
    /// see course-specific announcements.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> GetAll()
    {
        var scope = await _scope.GetAsync();
        var query = _db.Announcements.AsNoTracking().VisibleToCurrentUser(scope);

        if (scope.Role == UserRole.Student)
        {
            var enrolledIds = await _db.Enrollments.AsNoTracking()
                .Where(e => e.UserId == scope.UserId)
                .Select(e => e.CourseId)
                .ToListAsync();
            query = query.Where(a => a.CourseId == null || enrolledIds.Contains(a.CourseId.Value));
        }
        else if (scope.Role == UserRole.Graduate)
        {
            query = query.Where(a => a.CourseId == null);
        }
        else if (scope.Role == UserRole.Doctor)
        {
            var myCourseIds = await _db.CourseInstructors.AsNoTracking()
                .Where(ci => ci.DoctorUserId == scope.UserId)
                .Select(ci => ci.CourseId)
                .ToListAsync();
            query = query.Where(a => a.CourseId == null
                || myCourseIds.Contains(a.CourseId.Value)
                || a.CreatedByUserId == scope.UserId);
        }

        var announcements = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.Body,
                a.Category,
                a.CreatedAtUtc,
                a.UniversityId,
                a.CourseId
            })
            .ToListAsync();

        var courseIds = announcements
            .Where(a => a.CourseId.HasValue)
            .Select(a => a.CourseId!.Value)
            .Distinct()
            .ToList();
        var courseNames = courseIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Courses.AsNoTracking()
                .Where(c => courseIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Title);

        return Ok(announcements.Select(a => new
        {
            a.Id,
            a.Title,
            a.Body,
            a.Category,
            a.CreatedAtUtc,
            a.UniversityId,
            a.CourseId,
            CourseName = a.CourseId.HasValue
                ? courseNames.GetValueOrDefault(a.CourseId.Value, "") : ""
        }).ToList());
    }
}
