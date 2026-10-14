using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using UniSphere.Api.Validators;

namespace UniSphere.Api.Controllers;

/// <summary>University-scoped courses and materials. Students only ever see
/// their own university + department + year; ids from the client are always
/// re-validated server-side against the JWT identity.</summary>
[Authorize]
[ApiController]
[Route("api/university")]
public class UniversityController : ControllerBase
{
    private readonly AppDbContext _db;
    public UniversityController(AppDbContext db) => _db = db;

    private Task<User?> MeAsync() =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());

    public static IQueryable<Course> InScope(IQueryable<Course> query, User user) =>
        query.Where(c => user.UniversityId != null
                && c.UniversityId == user.UniversityId
                && (string.IsNullOrEmpty(user.Department) || c.Department == "" || c.Department == user.Department)
                && (!user.AcademicYear.HasValue || !c.AcademicYear.HasValue || c.AcademicYear == user.AcademicYear));

    [HttpGet("me")]
    public async Task<ActionResult<MyUniversityDto>> Me()
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return NotFound("No university assigned to this account yet.");

        var university = await _db.Universities.FindAsync(user.UniversityId.Value);
        if (university is null) return NotFound("University not found.");

        var courseIds = await InScope(_db.Courses.AsNoTracking(), user).Select(c => c.Id).ToListAsync();
        var materials = courseIds.Count == 0 ? 0
            : await _db.CourseMaterials.CountAsync(m => courseIds.Contains(m.CourseId));

        return new MyUniversityDto(university.Name, user.Department, user.AcademicYear,
            courseIds.Count, materials);
    }

    [HttpGet("me/courses")]
    public async Task<ActionResult<IReadOnlyList<UniversityCourseDto>>> MyCourses()
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return new List<UniversityCourseDto>();

        var courses = await InScope(_db.Courses.AsNoTracking(), user).OrderBy(c => c.Code).ToListAsync();
        var ids = courses.Select(c => c.Id).ToList();
        var materials = await _db.CourseMaterials.AsNoTracking()
            .Where(m => ids.Contains(m.CourseId))
            .ToListAsync();

        return courses.Select(c =>
        {
            var mine = materials.Where(m => m.CourseId == c.Id).ToList();
            DateTime? latest = mine.Count == 0 ? null : mine.Max(m => m.CreatedAtUtc);
            return new UniversityCourseDto(c.Id, c.Code, c.Title, c.Department,
                c.AcademicYear, c.Semester, mine.Count, latest);
        }).ToList();
    }

    [HttpGet("me/materials")]
    public async Task<ActionResult<PagedResultDto<MaterialDto>>> MyMaterials(
        [FromQuery] int? courseId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue)
            return new PagedResultDto<MaterialDto>(new List<MaterialDto>(), 0, 1, pageSize);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var scopedIds = await InScope(_db.Courses.AsNoTracking(), user).Select(c => c.Id).ToListAsync();

        if (courseId.HasValue && !scopedIds.Contains(courseId.Value))
            return NotFound("Course not found in your university.");

        if (!string.IsNullOrWhiteSpace(type) && !CreateMaterialRequestValidator.AllowedTypes.Contains(type.Trim(), StringComparer.OrdinalIgnoreCase))
            return BadRequest("Type must be Summary, Lecture, Assignment, Exam or Announcement.");

        var query = _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .Where(m => scopedIds.Contains(m.CourseId));

        if (courseId.HasValue)
            query = query.Where(m => m.CourseId == courseId.Value);
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(m => m.Type == type.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(m => m.Title.ToLower().Contains(s) || m.Description.ToLower().Contains(s));
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<MaterialDto>(
            items.Select(m => ToDto(m)).ToList(), total, page, pageSize);
    }

    /// <summary>One material for the /university?materialId= deep link.
    /// Out-of-scope ids return 404, never another student's data.</summary>
    [HttpGet("me/materials/{id:int}")]
    public async Task<ActionResult<MaterialDto>> MaterialById(int id)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return NotFound();

        var scopedIds = await InScope(_db.Courses.AsNoTracking(), user).Select(c => c.Id).ToListAsync();
        var material = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .FirstOrDefaultAsync(m => m.Id == id && scopedIds.Contains(m.CourseId));
        return material is null ? NotFound() : ToDto(material);
    }

    [HttpGet("me/upcoming")]    public async Task<ActionResult<IReadOnlyList<UpcomingItemDto>>> Upcoming([FromQuery] int take = 5)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return new List<UpcomingItemDto>();

        var scopedIds = await InScope(_db.Courses.AsNoTracking(), user).Select(c => c.Id).ToListAsync();
        var items = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .Where(m => scopedIds.Contains(m.CourseId)
                && (m.Type == "Exam" || m.Type == "Assignment" || m.Type == "Announcement"))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 20))
            .ToListAsync();

        return items.Select(m => new UpcomingItemDto(m.Id, m.Title, m.Course?.Title ?? "",
            m.Type, m.CreatedAtUtc, string.IsNullOrWhiteSpace(m.Description) ? null : m.Description)).ToList();
    }

    /// <summary>Doctors add materials for courses of their own university; admins for any course.</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPost("materials")]
    public async Task<ActionResult<MaterialDto>> CreateMaterial(
        CreateMaterialRequest req,
        [FromServices] FluentValidation.IValidator<CreateMaterialRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();

        var course = await _db.Courses.FindAsync(req.CourseId);
        if (course is null) return NotFound("Course not found.");

        if (user.Role == UserRole.Doctor
            && (!user.UniversityId.HasValue || course.UniversityId != user.UniversityId))
            return StatusCode(StatusCodes.Status403Forbidden,
                "You can only add materials for courses of your own university.");

        var material = new CourseMaterial
        {
            CourseId = course.Id,
            Title = req.Title.Trim(),
            Type = req.Type.Trim(),
            Description = req.Description?.Trim() ?? "",
            Content = req.Content?.Trim() ?? "",
            FileUrl = string.IsNullOrWhiteSpace(req.FileUrl) ? null : req.FileUrl.Trim(),
            UploadedByUserId = user.Id
        };
        _db.CourseMaterials.Add(material);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Method = HttpContext.Request.Method,
            Path = HttpContext.Request.Path.Value ?? "/api/university/materials",
            StatusCode = StatusCodes.Status201Created,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(material, course.Title));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("materials/{id:int}")]
    public async Task<IActionResult> DeleteMaterial(int id)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();

        var material = await _db.CourseMaterials.FindAsync(id);
        if (material is null) return NotFound();

        _db.CourseMaterials.Remove(material);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Method = HttpContext.Request.Method,
            Path = HttpContext.Request.Path.Value ?? $"/api/university/materials/{id}",
            StatusCode = StatusCodes.Status204NoContent,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static MaterialDto ToDto(CourseMaterial m, string? courseName = null) =>
        new(m.Id, m.CourseId, courseName ?? m.Course?.Title ?? "",
            m.Title, m.Type, m.Description, m.Content, m.FileUrl, m.CreatedAtUtc);
}
