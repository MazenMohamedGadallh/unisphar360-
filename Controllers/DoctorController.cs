using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>
/// Doctor-only endpoints. Every course-specific action re-verifies that the
/// authenticated doctor is assigned to the requested course (via
/// <see cref="DoctorAccessService"/>) and that the target row belongs to that
/// course. Frontend route guards are never trusted on their own.
/// </summary>
[Authorize(Roles = "Doctor")]
[ApiController]
[Route("api/doctor")]
public class DoctorController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".ppt", ".pptx", ".doc", ".docx", ".txt", ".md", ".csv", ".png", ".jpg", ".jpeg", ".zip"
    };

    private readonly AppDbContext _db;
    private readonly DoctorAccessService _access;
    private readonly DoctorService _doctor;
    private readonly IWebHostEnvironment _env;

    public DoctorController(AppDbContext db, DoctorAccessService access, DoctorService doctor, IWebHostEnvironment env)
    {
        _db = db;
        _access = access;
        _doctor = doctor;
        _env = env;
    }

    private int MeId => User.GetUserId();

    /// <summary>Resolves the caller and authorizes a course. Returns a failure
    /// result (or null when allowed) so each action stays a one-liner.</summary>
    private async Task<(User? User, ActionResult? Failure)> AuthorizeAsync(int courseId)
    {
        var result = await _access.AuthorizeCourseAsync(MeId, courseId);
        if (result.IsUnauthenticated) return (null, Unauthorized());
        if (result.IsNotFound) return (null, NotFound());
        if (!result.IsAllowed) return (null, Forbid());
        return (result.User, null);
    }

    // ---- Dashboard ----

    [HttpGet("dashboard")]
    public async Task<ActionResult<DoctorDashboardDto>> Dashboard()
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();
        return await _doctor.GetDashboardAsync(me);
    }

    // ---- Courses ----

    [HttpGet("courses")]
    public async Task<ActionResult<IReadOnlyList<DoctorCourseSummaryDto>>> Courses()
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var courseIds = await _access.GetAssignedCourseIdsAsync(me.Id);
        var courses = await _db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .OrderBy(c => c.Code)
            .ToListAsync();

        var enrollments = await _db.Enrollments.AsNoTracking()
            .Where(e => courseIds.Contains(e.CourseId))
            .ToListAsync();

        var list = courses.Select(c =>
        {
            var rows = enrollments.Where(e => e.CourseId == c.Id).ToList();
            var graded = rows.Where(e => !string.IsNullOrWhiteSpace(e.GradeLetter)).ToList();
            return new DoctorCourseSummaryDto(
                c.Id, c.Code, c.Title, c.Track, c.Credits, c.Semester,
                rows.Select(e => e.UserId).Distinct().Count(),
                graded.Count == 0 ? null : Math.Round(graded.Average(e => e.GradePoints), 2),
                rows.Count == 0 ? null : Math.Round(rows.Average(e => (double)e.AttendancePercent), 1));
        }).ToList();

        return list;
    }

    [HttpGet("courses/{courseId:int}")]
    public async Task<ActionResult<DoctorCourseDetailDto>> Course(int courseId)
    {
        var (_, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return NotFound();

        return new DoctorCourseDetailDto(
            course.Id, course.Code, course.Title, course.Description,
            course.Track, course.Credits, course.Semester, course.Department, course.AcademicYear);
    }

    [HttpGet("courses/{courseId:int}/students")]
    public async Task<ActionResult<IReadOnlyList<DoctorStudentDto>>> Students(int courseId)
    {
        var (_, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;
        return Ok(await _doctor.GetStudentsAsync(courseId));
    }

    [HttpGet("courses/{courseId:int}/analytics")]
    public async Task<ActionResult<DoctorCourseAnalyticsDto>> Analytics(int courseId)
    {
        var (_, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return NotFound();
        return Ok(await _doctor.GetCourseAnalyticsAsync(course));
    }

    // ---- Materials ----

    [HttpGet("courses/{courseId:int}/materials")]
    public async Task<ActionResult<IReadOnlyList<DoctorMaterialDto>>> Materials(int courseId)
    {
        var (_, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        var items = await _db.CourseMaterials.AsNoTracking()
            .Where(m => m.CourseId == courseId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync();
        return items.Select(ToDto).ToList();
    }

    [HttpPost("courses/{courseId:int}/materials")]
    public async Task<ActionResult<DoctorMaterialDto>> CreateMaterial(
        int courseId, [FromBody] CreateDoctorMaterialRequest req,
        [FromServices] FluentValidation.IValidator<CreateDoctorMaterialRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var (me, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        var material = new CourseMaterial
        {
            CourseId = courseId,
            Title = req.Title.Trim(),
            Type = req.Type.Trim(),
            Description = req.Description?.Trim() ?? "",
            Content = req.Content?.Trim() ?? "",
            FileUrl = string.IsNullOrWhiteSpace(req.FileUrl) ? null : req.FileUrl.Trim(),
            DeadlineUtc = req.DeadlineUtc,
            UploadedByUserId = me!.Id
        };
        _db.CourseMaterials.Add(material);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Materials), new { courseId }, ToDto(material));
    }

    [HttpPut("materials/{materialId:int}")]
    public async Task<ActionResult<DoctorMaterialDto>> UpdateMaterial(
        int materialId, [FromBody] UpdateDoctorMaterialRequest req,
        [FromServices] FluentValidation.IValidator<UpdateDoctorMaterialRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var material = await _db.CourseMaterials.FirstOrDefaultAsync(m => m.Id == materialId);
        if (material is null) return NotFound();

        // Ownership: the material must belong to a course the caller teaches.
        var (me, failure) = await AuthorizeAsync(material.CourseId);
        if (failure is not null) return failure;

        material.Title = req.Title.Trim();
        material.Type = req.Type.Trim();
        material.Description = req.Description?.Trim() ?? "";
        material.Content = req.Content?.Trim() ?? "";
        material.FileUrl = string.IsNullOrWhiteSpace(req.FileUrl) ? null : req.FileUrl.Trim();
        material.DeadlineUtc = req.DeadlineUtc;
        await _db.SaveChangesAsync();
        return ToDto(material);
    }

    [HttpDelete("materials/{materialId:int}")]
    public async Task<IActionResult> DeleteMaterial(int materialId)
    {
        var material = await _db.CourseMaterials.FirstOrDefaultAsync(m => m.Id == materialId);
        if (material is null) return NotFound();

        var (_, failure) = await AuthorizeAsync(material.CourseId);
        if (failure is not null) return failure;

        _db.CourseMaterials.Remove(material);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Uploads a lecture file for a course the doctor teaches.
    /// Extension and size are validated, the stored name is a server-generated
    /// GUID (no path traversal, never executed) and only a relative URL is
    /// returned — the physical path is never exposed.</summary>
    [HttpPost("courses/{courseId:int}/materials/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<MaterialUploadResultDto>> Upload(int courseId, IFormFile file)
    {
        var (_, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        if (file is null || file.Length == 0) return BadRequest("A non-empty file is required.");
        if (file.Length > MaxUploadBytes) return BadRequest("File exceeds the 10 MB limit.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
            return BadRequest("Unsupported file type. Allowed: pdf, ppt/pptx, doc/docx, txt, md, csv, png, jpg, jpeg, zip.");

        var safeExt = ext.ToLowerInvariant();
        var name = Guid.NewGuid().ToString("N") + safeExt;
        var relativeDir = Path.Combine("uploads", "doctor", $"course-{courseId}");
        var physicalDir = Path.Combine(_env.WebRootPath, relativeDir);
        Directory.CreateDirectory(physicalDir);

        var physicalPath = Path.Combine(physicalDir, name);
        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await file.CopyToAsync(stream);
        }

        var url = "/" + relativeDir.Replace('\\', '/') + "/" + name;
        return Ok(new MaterialUploadResultDto(url, file.FileName, file.Length));
    }

    // ---- Announcements ----

    [HttpGet("courses/{courseId:int}/announcements")]
    public async Task<ActionResult<IReadOnlyList<DoctorAnnouncementDto>>> Announcements(int courseId)
    {
        var (_, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        return await _db.Announcements.AsNoTracking()
            .Include(a => a.Course)
            .Where(a => a.CourseId == courseId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new DoctorAnnouncementDto(
                a.Id, a.CourseId, a.Course != null ? a.Course.Title : null,
                a.Title, a.Body, a.Category, a.CreatedAtUtc))
            .ToListAsync();
    }

    [HttpPost("courses/{courseId:int}/announcements")]
    public async Task<ActionResult<DoctorAnnouncementDto>> CreateAnnouncement(
        int courseId, [FromBody] CreateCourseAnnouncementRequest req,
        [FromServices] FluentValidation.IValidator<CreateCourseAnnouncementRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var (me, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId);
        var announcement = new Announcement
        {
            CourseId = courseId,
            Title = req.Title.Trim(),
            Body = req.Body.Trim(),
            Category = string.IsNullOrWhiteSpace(req.Category) ? "general" : req.Category.Trim(),
            CreatedByUserId = me!.Id
        };
        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Announcements), new { courseId },
            new DoctorAnnouncementDto(announcement.Id, courseId, course?.Title,
                announcement.Title, announcement.Body, announcement.Category, announcement.CreatedAtUtc));
    }

    [HttpPut("announcements/{announcementId:int}")]
    public async Task<ActionResult<DoctorAnnouncementDto>> UpdateAnnouncement(
        int announcementId, [FromBody] CreateCourseAnnouncementRequest req,
        [FromServices] FluentValidation.IValidator<CreateCourseAnnouncementRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var announcement = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId);
        if (announcement is null) return NotFound();
        if (announcement.CourseId is null)
            return Forbid(); // university-wide announcements are admin-managed

        var (_, failure) = await AuthorizeAsync(announcement.CourseId.Value);
        if (failure is not null) return failure;

        announcement.Title = req.Title.Trim();
        announcement.Body = req.Body.Trim();
        announcement.Category = string.IsNullOrWhiteSpace(req.Category) ? "general" : req.Category.Trim();
        await _db.SaveChangesAsync();

        var courseName = await _db.Courses.AsNoTracking()
            .Where(c => c.Id == announcement.CourseId)
            .Select(c => c.Title).FirstOrDefaultAsync();
        return new DoctorAnnouncementDto(announcement.Id, announcement.CourseId, courseName,
            announcement.Title, announcement.Body, announcement.Category, announcement.CreatedAtUtc);
    }

    [HttpDelete("announcements/{announcementId:int}")]
    public async Task<IActionResult> DeleteAnnouncement(int announcementId)
    {
        var announcement = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId);
        if (announcement is null) return NotFound();
        if (announcement.CourseId is null)
            return Forbid();

        var (_, failure) = await AuthorizeAsync(announcement.CourseId.Value);
        if (failure is not null) return failure;

        _db.Announcements.Remove(announcement);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static DoctorMaterialDto ToDto(CourseMaterial m) =>
        new(m.Id, m.CourseId, m.Title, m.Type, m.Description, m.Content, m.FileUrl, m.DeadlineUtc, m.CreatedAtUtc);
}
