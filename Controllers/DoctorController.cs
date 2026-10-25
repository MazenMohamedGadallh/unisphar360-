using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using UniSphere.Api.Validators;

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
    private readonly CourseMaterialFileStorage _materialFiles;
    private readonly FluentValidation.IValidator<UpdateDoctorProfileRequest> _profileValidator;

    public DoctorController(AppDbContext db, DoctorAccessService access, DoctorService doctor, IWebHostEnvironment env,
        CourseMaterialFileStorage materialFiles,
        FluentValidation.IValidator<UpdateDoctorProfileRequest> profileValidator)
    {
        _db = db;
        _access = access;
        _doctor = doctor;
        _env = env;
        _materialFiles = materialFiles;
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

    /// <summary>Material writes are intentionally stricter than reads: a
    /// course the caller is not assigned to is reported as 404 (not 403) so
    /// foreign and unassigned course ids are indistinguishable.</summary>
    private async Task<(User? User, ActionResult? Failure)> AuthorizeMaterialWriteAsync(int courseId)
    {
        var result = await _access.AuthorizeCourseAsync(MeId, courseId);
        if (result.IsUnauthenticated) return (null, Unauthorized());
        if (!result.IsAllowed) return (null, NotFound());
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
            me.FullName, me.UniversityId,
            me.Email, me.Address, me.PhoneNumber,
            universityName, me.Department,
            me.Specialization,
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
            me.FullName, me.UniversityId,
            me.Email, me.Address, me.PhoneNumber,
            universityName, me.Department,
            me.Specialization,
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

        // The doctor may be linked through either assignment table. Update
        // every existing row for this doctor+course so both the legacy and
        // doctor-module views stay in sync.
        var assignment = await _db.CourseAssignments
            .FirstOrDefaultAsync(a => a.DoctorUserId == me.Id && a.CourseId == courseId);
        var legacyRows = await _db.CourseInstructors
            .Where(ci => ci.DoctorUserId == me.Id && ci.CourseId == courseId)
            .ToListAsync();
        if (assignment is null && legacyRows.Count == 0) return NotFound(); // not his course (or unknown course)

        var displayTitle = string.IsNullOrWhiteSpace(req!.DisplayTitle)
            ? null // cleared: students fall back to the catalog title
            : req.DisplayTitle.Trim();
        if (assignment is not null) assignment.DisplayTitle = displayTitle;
        foreach (var row in legacyRows) row.DisplayTitle = displayTitle;
        await _db.SaveChangesAsync();

        var course = await _db.Courses.AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => new { c.Code, Title = c.Title, c.Semester })
            .FirstAsync();

        var enrolledCount = await CountEnrolledAsync(me, courseId);
        return Ok(new DoctorProfileCourseDto(
            courseId, course.Code, course.Title, displayTitle, course.Semester, enrolledCount));
    }

    /// <summary>Course rows used by both the profile and my-courses endpoints.
    /// Assignments are read from both the legacy CourseInstructors rows and
    /// the doctor-module CourseAssignments rows; enrolled counts are computed
    /// on the server with AsNoTracking.</summary>
    private async Task<List<DoctorProfileCourseDto>> GetMyCourseRowsAsync(User me)
    {
        var courseIds = await _access.GetAssignedCourseIdsAsync(me.Id);
        if (courseIds.Count == 0) return new List<DoctorProfileCourseDto>();

        var courses = await _db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.Title, c.Semester })
            .ToListAsync();

        var moduleRows = await _db.CourseAssignments.AsNoTracking()
            .Where(a => a.DoctorUserId == me.Id && courseIds.Contains(a.CourseId))
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new { a.CourseId, a.DisplayTitle })
            .ToListAsync();
        var legacyRows = await _db.CourseInstructors.AsNoTracking()
            .Where(ci => ci.DoctorUserId == me.Id && courseIds.Contains(ci.CourseId))
            .OrderBy(ci => ci.Id)
            .Select(ci => new { ci.CourseId, ci.DisplayTitle })
            .ToListAsync();

        // Doctor-module titles take precedence over legacy per-term ones.
        var titles = new Dictionary<int, string?>();
        foreach (var row in legacyRows)
            if (!string.IsNullOrWhiteSpace(row.DisplayTitle)) titles.TryAdd(row.CourseId, row.DisplayTitle.Trim());
        foreach (var row in moduleRows)
            if (!string.IsNullOrWhiteSpace(row.DisplayTitle)) titles[row.CourseId] = row.DisplayTitle.Trim();

        var counts = await CountEnrolledAsync(me, courseIds);

        return courses.Select(x => new DoctorProfileCourseDto(
            x.Id, x.Code, x.Title, titles.GetValueOrDefault(x.Id), x.Semester,
            counts.TryGetValue(x.Id, out var n) ? n : 0)).ToList();
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

    /// <summary>GET /api/doctor/summary — compact workspace counters used by
    /// the legacy Doctor Home page. Course ids come from both assignment
    /// tables; student counts and content are always real database rows.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DoctorSummaryDto>> Summary()
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var courseIds = await _access.GetAssignedCourseIdsAsync(me.Id);

        var materials = courseIds.Count == 0
            ? new List<CourseMaterial>()
            : await _db.CourseMaterials.AsNoTracking()
                .Where(m => courseIds.Contains(m.CourseId))
                .OrderByDescending(m => m.CreatedAtUtc)
                .ToListAsync();

        var announcements = courseIds.Count == 0
            ? new List<DoctorAnnouncementDto>()
            : await _db.Announcements.AsNoTracking()
                .Where(a => a.CourseId != null && courseIds.Contains(a.CourseId.Value))
                .OrderByDescending(a => a.CreatedAtUtc)
                .Select(a => new DoctorAnnouncementDto(
                    a.Id, a.CourseId, a.Course != null ? a.Course.Title : null,
                    a.Title, a.Body, a.Category, a.CreatedAtUtc,
                    a.CreatedByUserId == me.Id))
                .ToListAsync();

        var studentsCount = courseIds.Count == 0
            ? 0
            : await _db.Enrollments.AsNoTracking()
                .Where(e => courseIds.Contains(e.CourseId))
                .Select(e => e.UserId)
                .Distinct()
                .CountAsync();

        var now = DateTime.UtcNow;
        var latest = materials.Take(5).ToList();
        var upcoming = materials
            .Where(m => (m.Type == "Exam" || m.Type == "Assignment")
                && (!m.DeadlineUtc.HasValue || m.DeadlineUtc.Value >= now))
            .OrderBy(m => m.DeadlineUtc ?? m.CreatedAtUtc)
            .Take(5)
            .ToList();

        var combined = latest.Concat(upcoming).DistinctBy(m => m.Id).ToList();
        var materialDtos = (await ToMaterialDtosAsync(combined)).ToDictionary(m => m.Id);

        return new DoctorSummaryDto(
            courseIds.Count,
            studentsCount,
            materials.Count,
            announcements.Count,
            latest.Select(m => materialDtos[m.Id]).ToList(),
            announcements.Take(5).ToList(),
            announcements.Where(a => a.IsMine).Take(5).ToList(),
            upcoming.Select(m => materialDtos[m.Id]).ToList());
    }

    // ---- Courses ----

    [HttpGet("courses")]
    public async Task<ActionResult<IReadOnlyList<DoctorCourseDto>>> Courses()
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
        var materialCounts = await _db.CourseMaterials.AsNoTracking()
            .Where(m => courseIds.Contains(m.CourseId))
            .GroupBy(m => m.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count);

        var moduleRows = await _db.CourseAssignments.AsNoTracking()
            .Where(a => a.DoctorUserId == me.Id && courseIds.Contains(a.CourseId))
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new { a.CourseId, a.DisplayTitle })
            .ToListAsync();
        var legacyRows = await _db.CourseInstructors.AsNoTracking()
            .Where(ci => ci.DoctorUserId == me.Id && courseIds.Contains(ci.CourseId))
            .OrderBy(ci => ci.Id)
            .Select(ci => new { ci.CourseId, ci.DisplayTitle, ci.Term })
            .ToListAsync();

        var titles = new Dictionary<int, string?>();
        foreach (var row in legacyRows)
            if (!string.IsNullOrWhiteSpace(row.DisplayTitle)) titles.TryAdd(row.CourseId, row.DisplayTitle.Trim());
        foreach (var row in moduleRows)
            if (!string.IsNullOrWhiteSpace(row.DisplayTitle)) titles[row.CourseId] = row.DisplayTitle.Trim();

        var terms = legacyRows
            .GroupBy(r => r.CourseId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(r => r.Term)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)));

        var list = courses.Select(c => new DoctorCourseDto(
            c.Id, c.Code, c.Title, c.Department, c.AcademicYear, c.Semester, c.Credits,
            c.Prerequisites,
            enrollments.Where(e => e.CourseId == c.Id).Select(e => e.UserId).Distinct().Count(),
            materialCounts.GetValueOrDefault(c.Id),
            terms.GetValueOrDefault(c.Id, ""),
            titles.GetValueOrDefault(c.Id))).ToList();

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

    /// <summary>Legacy list used by /doctor/materials: all materials of the
    /// caller's assigned courses, with the same filters and paging as the
    /// student view. A courseId that is not assigned returns 404.</summary>
    [HttpGet("materials")]
    public async Task<ActionResult<PagedResultDto<MaterialDto>>> MyMaterials(
        [FromQuery] int? courseId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var courseIds = await _access.GetAssignedCourseIdsAsync(me.Id);
        if (courseId.HasValue && !courseIds.Contains(courseId.Value))
            return NotFound();

        if (!string.IsNullOrWhiteSpace(type)
            && !DoctorMaterialValidator.AllowedTypes.Contains(type.Trim(), StringComparer.OrdinalIgnoreCase))
            return BadRequest("Type must be Summary, Lecture, Assignment, Exam or Announcement.");

        var query = _db.CourseMaterials.AsNoTracking()
            .Where(m => courseIds.Contains(m.CourseId));

        if (courseId.HasValue) query = query.Where(m => m.CourseId == courseId.Value);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.Type == type.Trim());
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

        var dtos = await ToMaterialDtosAsync(items);
        return new PagedResultDto<MaterialDto>(dtos, total, page, pageSize);
    }

    /// <summary>Legacy create used by /doctor/materials. The course id is in
    /// the body; ownership is re-checked server-side and unassigned courses
    /// return 404.</summary>
    [HttpPost("materials")]
    public async Task<ActionResult<MaterialDto>> CreateMaterialForCourse(
        [FromBody] CreateDoctorMaterialRequest req,
        [FromServices] FluentValidation.IValidator<CreateDoctorMaterialRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        if (req.CourseId <= 0) return BadRequest("CourseId is required.");

        var (me, failure) = await AuthorizeMaterialWriteAsync(req.CourseId);
        if (failure is not null) return failure;

        var material = new CourseMaterial
        {
            CourseId = req.CourseId,
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

        var dto = (await ToMaterialDtosAsync(new[] { material })).Single();
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpGet("courses/{courseId:int}/materials")]
    public async Task<ActionResult<IReadOnlyList<DoctorMaterialDto>>> CourseMaterials(int courseId)
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

        var (me, failure) = await AuthorizeMaterialWriteAsync(courseId);
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
        return CreatedAtAction(nameof(CourseMaterials), new { courseId }, ToDto(material));
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
        var (me, failure) = await AuthorizeMaterialWriteAsync(material.CourseId);
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

        var (_, failure) = await AuthorizeMaterialWriteAsync(material.CourseId);
        if (failure is not null) return failure;

        var storedFileName = material.StoredFileName;
        _db.CourseMaterials.Remove(material);
        await _db.SaveChangesAsync();
        _materialFiles.Delete(storedFileName);
        return NoContent();
    }

    /// <summary>POST /api/doctor/materials/{id}/file — attach or replace the
    /// secure PDF/Word/PowerPoint file for one material. The material's course
    /// is re-authorized server-side; foreign or unassigned courses are 404.
    /// The old file is removed only after the new row state is saved.</summary>
    [HttpPost("materials/{materialId:int}/file")]
    [RequestSizeLimit(CourseMaterialFileStorage.TransportMaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CourseMaterialFileStorage.TransportMaxBytes)]
    public async Task<ActionResult<DoctorMaterialDto>> UploadMaterialFile(
        int materialId, IFormFile file)
    {
        var material = await _db.CourseMaterials.FirstOrDefaultAsync(m => m.Id == materialId);
        if (material is null) return NotFound();

        var (_, failure) = await AuthorizeMaterialWriteAsync(material.CourseId);
        if (failure is not null) return failure;

        if (file is null)
            return BadRequest("Choose a PDF, Word or PowerPoint file to upload.");

        StoredMaterialFile stored;
        try
        {
            stored = await _materialFiles.SaveAsync(file, HttpContext.RequestAborted);
        }
        catch (MaterialFileException ex)
        {
            return BadRequest(ex.Message);
        }

        var oldStoredFileName = material.StoredFileName;
        material.StoredFileName = stored.StoredFileName;
        material.OriginalFileName = stored.OriginalFileName;
        material.FileSizeBytes = stored.SizeBytes;
        material.FileContentType = stored.ContentType;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _materialFiles.Delete(stored.StoredFileName);
            throw;
        }

        _materialFiles.Delete(oldStoredFileName);
        return Ok(ToDto(material));
    }

    /// <summary>DELETE /api/doctor/materials/{id}/file — removes the attachment
    /// from the material and deletes the stored file. Same course ownership
    /// rule as every other material write.</summary>
    [HttpDelete("materials/{materialId:int}/file")]
    public async Task<IActionResult> DeleteMaterialFile(int materialId)
    {
        var material = await _db.CourseMaterials.FirstOrDefaultAsync(m => m.Id == materialId);
        if (material is null) return NotFound();

        var (_, failure) = await AuthorizeMaterialWriteAsync(material.CourseId);
        if (failure is not null) return failure;

        var storedFileName = material.StoredFileName;
        material.StoredFileName = null;
        material.OriginalFileName = null;
        material.FileSizeBytes = null;
        material.FileContentType = null;
        await _db.SaveChangesAsync();

        _materialFiles.Delete(storedFileName);
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
        var (me, failure) = await AuthorizeAsync(courseId);
        if (failure is not null) return failure;

        return await _db.Announcements.AsNoTracking()
            .Include(a => a.Course)
            .Where(a => a.CourseId == courseId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new DoctorAnnouncementDto(
                a.Id, a.CourseId, a.Course != null ? a.Course.Title : null,
                a.Title, a.Body, a.Category, a.CreatedAtUtc,
                a.CreatedByUserId == me!.Id))
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
                announcement.Title, announcement.Body, announcement.Category, announcement.CreatedAtUtc,
                IsMine: true));
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
            announcement.Title, announcement.Body, announcement.Category, announcement.CreatedAtUtc,
            IsMine: true);
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
        new(m.Id, m.CourseId, m.Title, m.Type, m.Description, m.Content, m.FileUrl, m.DeadlineUtc,
            m.CreatedAtUtc,
            !string.IsNullOrWhiteSpace(m.StoredFileName),
            m.OriginalFileName,
            m.FileSizeBytes);

    /// <summary>Student-facing material rows always carry the course display
    /// title and the instructor name(s), resolved server-side from the same
    /// assignment tables that authorize course access.</summary>
    private async Task<List<MaterialDto>> ToMaterialDtosAsync(IReadOnlyCollection<CourseMaterial> materials)
    {
        if (materials.Count == 0) return new List<MaterialDto>();

        var courseIds = materials.Select(m => m.CourseId).Distinct().ToList();
        var displayTitles = await InstructorDirectory.DisplayTitlesByCourseAsync(_db, courseIds);
        var instructors = await InstructorDirectory.NamesByCourseAsync(_db, courseIds);
        var catalogTitles = await _db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToDictionaryAsync(c => c.Id, c => c.Title);

        return materials.Select(m => new MaterialDto(
            m.Id, m.CourseId,
            displayTitles.GetValueOrDefault(m.CourseId, catalogTitles.GetValueOrDefault(m.CourseId, "")),
            m.Title, m.Type, m.Description, m.Content, m.FileUrl, m.CreatedAtUtc,
            instructors.GetValueOrDefault(m.CourseId, ""),
            !string.IsNullOrWhiteSpace(m.StoredFileName),
            m.OriginalFileName,
            m.FileSizeBytes)).ToList();
    }
}
