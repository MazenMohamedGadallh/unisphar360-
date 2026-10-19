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
    private const long MaxAvatarBytes = 5 * 1024 * 1024;  // 5 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".ppt", ".pptx", ".doc", ".docx", ".txt", ".md", ".csv", ".png", ".jpg", ".jpeg", ".zip"
    };

    private readonly AppDbContext _db;
    private readonly DoctorAccessService _access;
    private readonly DoctorService _doctor;
    private readonly IWebHostEnvironment _env;
    private readonly FluentValidation.IValidator<UpdateDoctorProfileRequest> _profileValidator;

    public DoctorController(AppDbContext db, DoctorAccessService access, DoctorService doctor, IWebHostEnvironment env,
        FluentValidation.IValidator<UpdateDoctorProfileRequest> profileValidator)
    {
        _db = db;
        _access = access;
        _doctor = doctor;
        _env = env;
        _profileValidator = profileValidator;
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

    // ---- Profile ----

    /// <summary>GET /api/doctor/profile — the doctor's own profile resolved
    /// from the JWT claims only. Locked fields (name/email/phone/address/
    /// university/department) are returned read-only; only AvatarUrl and the
    /// per-course display titles are editable.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<DoctorProfileDto>> Profile()
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var courses = await GetMyCourseRowsAsync(me);
        string? universityName = null;
        if (me.UniversityId is not null)
        {
            universityName = await _db.Universities.AsNoTracking()
                .Where(u => u.Id == me.UniversityId.Value)
                .Select(u => u.Name)
                .FirstOrDefaultAsync();
        }

        return new DoctorProfileDto(
            me.Id, me.FirstName, me.MiddleName, me.LastName,
            me.Email, me.Address, me.PhoneNumber,
            universityName, me.Department,
            me.AvatarUrl,
            courses);
    }

    /// <summary>PUT /api/doctor/profile — server-side enforcement that the
    /// doctor can only change what is editable. The body is parsed as raw
    /// JSON (the User entity is NEVER model-bound) and every key other than
    /// the editable ones is rejected with 400 BEFORE anything is applied.
    /// UpdateDoctorProfileRequest contains only editable fields by design;
    /// all changes are audit-logged by the audit middleware.</summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] System.Text.Json.JsonElement body)
    {
        if (body.ValueKind != System.Text.Json.JsonValueKind.Object)
            return BadRequest("A JSON object body is required.");

        // Allowlist: everything except the editable fields is a locked field.
        const string editableKey = "avatarurl";
        var unexpected = body.EnumerateObject()
            .Where(p => !string.Equals(p.Name, editableKey, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();
        if (unexpected.Count > 0)
        {
            return BadRequest(new
            {
                Title = "Profile contains locked fields.",
                Status = StatusCodes.Status400BadRequest,
                Fields = unexpected,
                Error = "First name, middle name, last name, email, address, phone number, university and department are locked. Contact an administrator to change them."
            });
        }

        var req = System.Text.Json.JsonSerializer.Deserialize<UpdateDoctorProfileRequest>(body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var validation = await _profileValidator.ValidateAsync(req ?? new UpdateDoctorProfileRequest(null));
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        // Trackable load (not AsNoTracking) for the single editable column.
        var me = await _db.Users.FirstOrDefaultAsync(u => u.Id == MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        me.AvatarUrl = string.IsNullOrWhiteSpace(req!.AvatarUrl) ? null : req.AvatarUrl.Trim();
        await _db.SaveChangesAsync();

        var courses = await GetMyCourseRowsAsync(me);
        string? universityName = null;
        if (me.UniversityId is not null)
        {
            universityName = await _db.Universities.AsNoTracking()
                .Where(u => u.Id == me.UniversityId.Value)
                .Select(u => u.Name)
                .FirstOrDefaultAsync();
        }
        return Ok(new DoctorProfileDto(
            me.Id, me.FirstName, me.MiddleName, me.LastName,
            me.Email, me.Address, me.PhoneNumber,
            universityName, me.Department,
            me.AvatarUrl,
            courses));
    }

    /// <summary>POST /api/doctor/profile/avatar — uploads an avatar image.
    /// The magic bytes (not the client MIME type) decide the format, the
    /// stored name is a server-generated GUID and only a relative URL is
    /// returned; the previous avatar file is deleted on replace.</summary>
    [HttpPost("profile/avatar")]
    [RequestSizeLimit(MaxAvatarBytes)]
    public async Task<ActionResult<MaterialUploadResultDto>> UploadAvatar(IFormFile file)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        if (file is null || file.Length == 0) return BadRequest("A non-empty file is required.");
        if (file.Length > MaxAvatarBytes) return BadRequest("Avatar exceeds the 5 MB limit.");

        var ext = DetectImageExtension(file);
        if (ext is null) return BadRequest("Unsupported avatar format. Only PNG, JPEG or WebP images are allowed.");
        var submittedExt = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        if (!new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(submittedExt))
            return BadRequest("File extension must be .png, .jpg, .jpeg or .webp.");

        var name = Guid.NewGuid().ToString("N") + ext;
        var relativeDir = Path.Combine("uploads", "doctor", "avatar");
        var physicalDir = Path.Combine(_env.WebRootPath, relativeDir);
        Directory.CreateDirectory(physicalDir);

        // Verify the magic bytes from the stream, not the client headers.
        await using (var open = file.OpenReadStream())
        {
            var header = new byte[4];
            var read = await open.ReadAsync(header.AsMemory(0, 4));
            if (read < 4 || !IsAllowedImageHeader(header, ext))
                return BadRequest("The uploaded file is not a valid PNG, JPEG or WebP image.");
        }

        var physicalPath = Path.Combine(physicalDir, name);
        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await file.CopyToAsync(stream);
        }

        // Best-effort cleanup of the previous avatar file (never exposes paths).
        DeleteUploadedFile(me.AvatarUrl, subdir: "avatar");

        var url = "/" + relativeDir.Replace('\\', '/') + "/" + name;

        var tracked = await _db.Users.FirstAsync(u => u.Id == me.Id);
        tracked.AvatarUrl = url;
        await _db.SaveChangesAsync();

        return Ok(new MaterialUploadResultDto(url, file.FileName ?? name, file.Length));
    }

    /// <summary>My Course rows for the doctor: course code, catalog title,
    /// term, the doctor's editable display title and the number of enrolled
    /// students — computed on the server (AsNoTracking) from the Enrollments
    /// table joined with students of the doctor's university (never trusted
    /// from the client).</summary>
    [HttpGet("my-courses")]
    public async Task<ActionResult<IReadOnlyList<DoctorProfileCourseDto>>> MyCourses()
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();
        return Ok(await GetMyCourseRowsAsync(me));
    }

    /// <summary>PUT /api/doctor/courses/{courseId}/display-title — renames the
    /// course only for this doctor's assignment row. Ownership is enforced by
    /// looking up the (doctor, course) assignment directly: another doctor's
    /// course or an unknown course returns 404, never a leak. Catalog code
    /// and title are never touched.</summary>
    [HttpPut("courses/{courseId:int}/display-title")]
    public async Task<IActionResult> SetCourseDisplayTitle(
        int courseId, [FromBody] SetCourseDisplayTitleRequest req,
        [FromServices] FluentValidation.IValidator<SetCourseDisplayTitleRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var assignment = await _db.CourseAssignments
            .FirstOrDefaultAsync(a => a.DoctorUserId == me.Id && a.CourseId == courseId);
        if (assignment is null) return NotFound(); // not his course (or unknown course)

        assignment.DisplayTitle = string.IsNullOrWhiteSpace(req!.DisplayTitle)
            ? null // cleared: students fall back to the catalog title
            : req.DisplayTitle.Trim();
        await _db.SaveChangesAsync();

        var course = await _db.Courses.AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => new { c.Code, Title = c.Title, c.Semester })
            .FirstAsync();

        var enrolledCount = await CountEnrolledAsync(me, courseId);
        return Ok(new DoctorProfileCourseDto(
            courseId, course.Code, course.Title, assignment.DisplayTitle, course.Semester, enrolledCount));
    }

    /// <summary>Course rows used by both the profile and my-courses endpoints.
    /// Enrolled counts are computed on the server with AsNoTracking.</summary>
    private async Task<List<DoctorProfileCourseDto>> GetMyCourseRowsAsync(User me)
    {
        var assignments = await _db.CourseAssignments.AsNoTracking()
            .Where(a => a.DoctorUserId == me.Id)
            .Join(_db.Courses.AsNoTracking(), a => a.CourseId, c => c.Id,
                (a, c) => new { a.CourseId, a.DisplayTitle, c.Code, CatalogTitle = c.Title, c.Semester })
            .OrderBy(x => x.Code)
            .ToListAsync();

        var courseIds = assignments.Select(x => x.CourseId).ToList();
        var counts = await CountEnrolledAsync(me, courseIds);

        return assignments.Select(x => new DoctorProfileCourseDto(
            x.CourseId, x.Code, x.CatalogTitle, x.DisplayTitle, x.Semester,
            counts.TryGetValue(x.CourseId, out var n) ? n : 0)).ToList();
    }

    /// <summary>Counts enrollments per course among non-deleted students of
    /// the doctor's university. The university id always comes from the
    /// claims-resolved user, never from client input. When the doctor has no
    /// university assigned, all students of his courses count.</summary>
    private async Task<Dictionary<int, int>> CountEnrolledAsync(User me, IEnumerable<int> courseIds)
    {
        var ids = courseIds.ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        var rows = _db.Enrollments.AsNoTracking()
            .Where(e => ids.Contains(e.CourseId))
            .Join(_db.Users.AsNoTracking(), e => e.UserId, u => u.Id,
                (e, u) => new { e.CourseId, u.UniversityId, u.IsDeleted });
        if (me.UniversityId is not null)
            rows = rows.Where(x => x.UniversityId == me.UniversityId);

        return await rows
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count);
    }

    private async Task<int> CountEnrolledAsync(User me, int courseId) =>
        (await CountEnrolledAsync(me, new[] { courseId })).TryGetValue(courseId, out var n) ? n : 0;

    /// <summary>Best-effort delete of a previously uploaded file by its
    /// relative store URL (avatar). Files outside the expected subfolder are
    /// never touched and the physical path is never exposed.</summary>
    private void DeleteUploadedFile(string? storeUrl, string subdir)
    {
        if (string.IsNullOrWhiteSpace(storeUrl)) return;
        var prefix = "/" + ("uploads/doctor/" + subdir).Replace('\\', '/') + "/";
        if (!storeUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        var name = storeUrl[prefix.Length..];
        // GUID file names are generated by this server; anything else is ignored.
        if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _)
            && Path.GetExtension(name).StartsWith(".", StringComparison.Ordinal))
        {
            try
            {
                var path = Path.Combine(_env.WebRootPath, "uploads", "doctor", subdir, Path.GetFileName(name));
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch
            {
                // Cleanup is best-effort only.
            }
        }
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

    /// <summary>Detects the image format from the first bytes of the stream,
    /// ignoring the client-declared content type and extension entirely.
    /// Returns the normalized extension, or null for unsupported data.</summary>
    private static string? DetectImageExtension(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        Span<byte> header = stackalloc byte[12];
        var read = 0;
        while (read < header.Length)
        {
            var n = stream.Read(header[read..]);
            if (n == 0) break;
            read += n;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                       && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return ".png";
        // JPEG: FF D8 FF
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ".jpeg";
        // WebP: "RIFF" .... "WEBP"
        if (read >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                      && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            return ".webp";
        return null;
    }

    /// <summary>Defence in depth: after DetectImageExtension picked the
    /// extension, re-verify the first bytes from a fresh stream still match
    /// that extension (guards against a truncated/raced file).</summary>
    private static bool IsAllowedImageHeader(byte[] header, string ext) => ext switch
    {
        ".png" => header.Length >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
        ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".webp" => header.Length >= 4 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F',
        _ => false
    };

    private static DoctorMaterialDto ToDto(CourseMaterial m) =>
        new(m.Id, m.CourseId, m.Title, m.Type, m.Description, m.Content, m.FileUrl, m.DeadlineUtc, m.CreatedAtUtc);
}
