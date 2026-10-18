using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoursesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CoursesController(AppDbContext db) => _db = db;

    private static CourseDto ToDto(Course c) => new(c.Id, c.Code, c.Title, c.Description, c.Track, c.Credits);

    /// <summary>List courses. Optional ?track=AI filter.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<CourseDto>> GetAll([FromQuery] string? track = null)
    {
        var query = _db.Courses.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(track))
            query = query.Where(c => c.Track == track);

        return await query.OrderBy(c => c.Code)
            .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.Description, c.Track, c.Credits))
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CourseDto>> GetById(int id)
    {
        var c = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return c is null ? NotFound() : ToDto(c);
    }

    /// <summary>Courses recommended for a user (based on their major).</summary>
    [Authorize]
    [HttpGet("recommended")]
    public async Task<ActionResult<IReadOnlyList<CourseDto>>> Recommended()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        var courses = await _db.Courses.AsNoTracking().ToListAsync();
        return Recommender.Rank(courses, user).Select(ToDto).ToList();
    }
}
