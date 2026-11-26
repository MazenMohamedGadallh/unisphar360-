using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using UniSphere.Api.Validators;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/university")]
public class UniversityController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;
    private readonly CourseMaterialFileStorage _materialFiles;

    public UniversityController(AppDbContext db, IUniversityScope scope,
        CourseMaterialFileStorage materialFiles)
    {
        _db = db;
        _scope = scope;
        _materialFiles = materialFiles;
    }

    private Task<User?> MeAsync() =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());

    private async Task<IQueryable<Course>> MyCourseQueryAsync()
    {
        var scope = await _scope.GetAsync();
        return _db.Courses.AsNoTracking().VisibleToCurrentUser(scope);
    }

    private async Task<IQueryable<Course>> MyMaterialCourseQueryAsync(User user)
    {
        var scope = await _scope.GetAsync();
        var enrolledIds = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == user.Id)
            .Select(e => e.CourseId)
            .ToListAsync();

        return _db.Courses.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .Where(c => enrolledIds.Contains(c.Id)
                || ((string.IsNullOrEmpty(user.Department)
                        || c.Department == ""
                        || c.Department == user.Department)
                    && (!user.AcademicYear.HasValue
                        || !c.AcademicYear.HasValue
                        || c.AcademicYear == user.AcademicYear)));
    }

    [Authorize(Roles = "Student")]
    [HttpGet("me")]
    public async Task<ActionResult<MyUniversityDto>> Me()
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();

        if (!user.UniversityId.HasValue)
        {
            if (user.Role == UserRole.Graduate)
                return new MyUniversityDto(null, user.Department, null, 0, 0);
            return NotFound("No university assigned to this account yet.");
        }

        var university = await _db.Universities.FindAsync(user.UniversityId.Value);
        if (university is null) return NotFound("University not found.");

        var courseIds = await (await MyCourseQueryAsync()).Select(c => c.Id).ToListAsync();
        var materialCourseIds = await (await MyMaterialCourseQueryAsync(user)).Select(c => c.Id).ToListAsync();
        var materials = materialCourseIds.Count == 0 ? 0
            : await _db.CourseMaterials.CountAsync(m => materialCourseIds.Contains(m.CourseId));

        return new MyUniversityDto(university.Name, user.Department, user.AcademicYear,
            courseIds.Count, materials);
    }

    [Authorize(Roles = "Student")]
    [HttpGet("me/courses")]
    public async Task<ActionResult<IReadOnlyList<UniversityCourseDto>>> MyCourses(
        [FromQuery] string? department = null,
        [FromQuery] int? year = null)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return new List<UniversityCourseDto>();

        var allCourses = await (await MyCourseQueryAsync()).OrderBy(c => c.Code).ToListAsync();

        var enrollmentRows = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == user.Id)
            .ToListAsync();
        var enrollments = enrollmentRows
            .GroupBy(e => e.CourseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.GradePoints).First());
        var passedCodes = enrollmentRows
            .Where(e => e.GradePoints >= 2.0)
            .Join(allCourses, e => e.CourseId, c => c.Id, (_, c) => c.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var courses = allCourses.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(department))
        {
            var requested = department.Trim();
            var departmentName = requested.ToUpperInvariant() switch
            {
                "CS" => "Computer Science",
                "IS" => "Information Systems",
                "IT" => "Information Technology",
                "AI" => "Artificial Intelligence",
                "HU" => "Humanities",
                "MA" => "Mathematics",
                "PH" => "Physics",
                "ST" => "Statistics",
                _ => requested
            };
            courses = courses.Where(c =>
                string.Equals(c.Department, departmentName, StringComparison.OrdinalIgnoreCase));
        }
        if (year.HasValue)
            courses = courses.Where(c => c.AcademicYear == year.Value);

        var selected = courses.ToList();
        var ids = selected.Select(c => c.Id).ToList();
        var materialIds = await (await MyMaterialCourseQueryAsync(user)).Select(c => c.Id).ToListAsync();
        var materials = await _db.CourseMaterials.AsNoTracking()
            .Where(m => materialIds.Contains(m.CourseId))
            .ToListAsync();
        // Instructor names come only from admin-managed assignments.
        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, ids);
        // Students see the doctor-editable display title when one is set.
        var displayTitles = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, ids);

        return selected.Select(c =>
        {
            var mine = materials.Where(m => m.CourseId == c.Id).ToList();
            DateTime? latest = mine.Count == 0 ? null : mine.Max(m => m.CreatedAtUtc);
            return new UniversityCourseDto(c.Id, c.Code, displayTitles.GetValueOrDefault(c.Id, c.Title),
                c.Department, c.AcademicYear, c.Semester, mine.Count, latest,
                instructors.GetValueOrDefault(c.Id, ""),
                CourseStatus(c, enrollments.GetValueOrDefault(c.Id), passedCodes),
                MatchesStudentProfile(c, user));
        }).ToList();
    }

    private static string CourseStatus(
        Course course, Enrollment? enrollment, HashSet<string> passedCodes)
    {
        if (enrollment is not null)
            return enrollment.GradePoints >= 2.0 ? "Passed" : "InProgress";

        var prerequisites = (course.Prerequisites ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return prerequisites.All(passedCodes.Contains) ? "Available" : "Locked";
    }

    private static bool MatchesStudentProfile(Course course, User user)
    {
        var departmentMatches = string.IsNullOrWhiteSpace(user.Department)
            || string.IsNullOrWhiteSpace(course.Department)
            || string.Equals(course.Department, user.Department, StringComparison.OrdinalIgnoreCase);
        var yearMatches = !user.AcademicYear.HasValue
            || !course.AcademicYear.HasValue
            || course.AcademicYear == user.AcademicYear;
        return departmentMatches && yearMatches;
    }

    [Authorize(Roles = "Student")]
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

        var scopedIds = await (await MyMaterialCourseQueryAsync(user)).Select(c => c.Id).ToListAsync();

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

        var displayTitles = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, scopedIds);
        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, scopedIds);
        return new PagedResultDto<MaterialDto>(
            items.Select(m => ToDto(m,
                displayTitles.GetValueOrDefault(m.CourseId, m.Course?.Title ?? ""),
                instructors.GetValueOrDefault(m.CourseId, ""))).ToList(),
            total, page, pageSize);
    }

    [Authorize(Roles = "Student")]
    [HttpGet("me/materials/{id:int}")]
    public async Task<ActionResult<MaterialDto>> MaterialById(int id)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return NotFound();

        var scopedIds = await (await MyMaterialCourseQueryAsync(user)).Select(c => c.Id).ToListAsync();
        var material = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .FirstOrDefaultAsync(m => m.Id == id && scopedIds.Contains(m.CourseId));
        if (material is null) return NotFound();
        var titles = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, new[] { material.CourseId });
        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, new[] { material.CourseId });
        return ToDto(material,
            titles.GetValueOrDefault(material.CourseId, material.Course?.Title ?? ""),
            instructors.GetValueOrDefault(material.CourseId, ""));
    }

    [Authorize(Roles = "Student")]
    [HttpGet("me/upcoming")]
    public async Task<ActionResult<IReadOnlyList<UpcomingItemDto>>> Upcoming([FromQuery] int take = 5)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();
        if (!user.UniversityId.HasValue) return new List<UpcomingItemDto>();

        var scopedIds = await (await MyMaterialCourseQueryAsync(user)).Select(c => c.Id).ToListAsync();
        var items = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .Where(m => scopedIds.Contains(m.CourseId)
                && (m.Type == "Exam" || m.Type == "Assignment" || m.Type == "Announcement"))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 20))
            .ToListAsync();

        var titles = await InstructorDirectory.DisplayTitlesByCourseAsync(
            _db, items.Select(m => m.CourseId).Distinct().ToList());
        return items.Select(m => new UpcomingItemDto(m.Id, m.Title,
            titles.GetValueOrDefault(m.CourseId, m.Course?.Title ?? ""),
            m.Type, m.CreatedAtUtc, string.IsNullOrWhiteSpace(m.Description) ? null : m.Description)).ToList();
    }

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

        var scope = await _scope.GetAsync();
        var courseQuery = _db.Courses.VisibleToCurrentUser(scope);
        if (scope.Role == UserRole.Doctor)
            courseQuery = courseQuery.AssignedToDoctor(_db, user.Id);
        var course = await courseQuery.FirstOrDefaultAsync(c => c.Id == req.CourseId);
        if (course is null) return NotFound("Course not found.");

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

        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, new[] { course.Id });
        return StatusCode(StatusCodes.Status201Created,
            ToDto(material, course.Title, instructors.GetValueOrDefault(course.Id, "")));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("materials/{id:int}")]
    public async Task<IActionResult> DeleteMaterial(int id)
    {
        var user = await MeAsync();
        if (user is null || user.IsDeleted) return Unauthorized();

        var material = await _db.CourseMaterials.FindAsync(id);
        if (material is null) return NotFound();

        var storedFileName = material.StoredFileName;
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
        _materialFiles.Delete(storedFileName);
        return NoContent();
    }

    private static MaterialDto ToDto(CourseMaterial m, string? courseName = null, string? instructorName = null) =>
        new(m.Id, m.CourseId, courseName ?? m.Course?.Title ?? "",
            m.Title, m.Type, m.Description, m.Content, m.FileUrl, m.CreatedAtUtc,
            instructorName ?? "",
            !string.IsNullOrWhiteSpace(m.StoredFileName),
            m.OriginalFileName,
            m.FileSizeBytes,
            m.DeadlineUtc);
}
