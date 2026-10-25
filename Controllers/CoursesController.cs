using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Student,Doctor,Admin")]
[ApiController]
[Route("api/[controller]")]
public class CoursesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;

    public CoursesController(AppDbContext db, IUniversityScope scope)
    {
        _db = db;
        _scope = scope;
    }

    private static CourseDto ToDto(Course c) =>
        new(c.Id, c.Code, c.Title, c.Description, c.Track, c.Credits);

    /// <summary>Students see the doctor-editable display title when one is
    /// set; the catalog title stays the fallback. Course codes never change.</summary>
    private async Task<List<CourseDto>> WithDisplayTitlesAsync(
        List<CourseDto> courses, UserRole? role)
    {
        if (role != UserRole.Student || courses.Count == 0) return courses;
        var titles = await InstructorDirectory.DisplayTitlesByCourseAsync(
            _db, courses.Select(c => c.Id).ToList());
        return courses
            .Select(c => titles.TryGetValue(c.Id, out var title) ? c with { Title = title } : c)
            .ToList();
    }

    /// <summary>List courses visible in the caller's university. Optional ?track=AI filter.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<CourseDto>> GetAll([FromQuery] string? track = null)
    {
        var scope = await _scope.GetAsync();
        var query = _db.Courses.AsNoTracking().VisibleToCurrentUser(scope);

        if (!string.IsNullOrWhiteSpace(track))
            query = query.Where(c => c.Track == track);

        var courses = await query.OrderBy(c => c.Code)
            .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.Description, c.Track, c.Credits))
            .ToListAsync();
        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, courses.Select(c => c.Id).ToList());
        var result = courses
            .Select(c => c with { Instructors = instructors.GetValueOrDefault(c.Id, "") })
            .ToList();
        return await WithDisplayTitlesAsync(result, scope.Role);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CourseDto>> GetById(int id)
    {
        var scope = await _scope.GetAsync();
        var query = _db.Courses.AsNoTracking().VisibleToCurrentUser(scope);

        var c = await query.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, new[] { c.Id });
        var result = ToDto(c) with { Instructors = instructors.GetValueOrDefault(c.Id, "") };
        if (scope.Role == UserRole.Student)
        {
            var titles = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, new[] { c.Id });
            if (titles.TryGetValue(c.Id, out var title))
                result = result with { Title = title };
        }
        return result;
    }

    /// <summary>Courses recommended for the logged-in student (own university only).</summary>
    [Authorize(Roles = "Student")]
    [HttpGet("recommended")]
    public async Task<ActionResult<IReadOnlyList<CourseDto>>> Recommended()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Unauthorized();

        var scope = await _scope.GetAsync(userId);
        var courses = await _db.Courses.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .EligibleForStudent(user)
            .ToListAsync();

        var result = Recommender.Rank(courses, user).Select(ToDto).ToList();
        var titles = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, result.Select(c => c.Id).ToList());
        return result
            .Select(c => titles.TryGetValue(c.Id, out var title) ? c with { Title = title } : c)
            .ToList();
    }
}
