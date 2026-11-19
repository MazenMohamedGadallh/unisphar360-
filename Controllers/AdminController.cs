using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PerformanceAnalysisService _analysis;
    private readonly IValidator<PatchUserRoleRequest> _roleValidator;
    public AdminController(
        AppDbContext db,
        PerformanceAnalysisService analysis,
        IValidator<PatchUserRoleRequest> roleValidator)
    {
        _db = db;
        _analysis = analysis;
        _roleValidator = roleValidator;
    }

    /// <summary>Administrative analytics: student count, enrollment stats, GPA distribution.</summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<object>> GetAnalytics()
    {
        var totalStudents = await _db.Users.CountAsync(u => u.Role == UserRole.Student);
        var totalGraduates = await _db.Users.CountAsync(u => u.Role == UserRole.Graduate);
        var totalDoctors = await _db.Users.CountAsync(u => u.Role == UserRole.Doctor);
        var totalCourses = await _db.Courses.CountAsync();
        var totalEnrollments = await _db.Enrollments.CountAsync();

        var avgGpa = await _db.SemesterRecords
            .Where(r => r.IsCurrent)
            .AverageAsync(r => (double?)r.Gpa) ?? 0;

        var avgAttendance = await _db.SemesterRecords
            .Where(r => r.IsCurrent)
            .AverageAsync(r => (double?)r.AttendanceRate) ?? 0;

        // GPA distribution — fetch data and group in-memory (switch expressions
        // are not translatable to SQL by EF Core)
        var allRecords = await _db.SemesterRecords
            .Where(r => r.IsCurrent)
            .ToListAsync();

        var gpaRanges = allRecords
            .GroupBy(r => r.Gpa switch
            {
                >= 3.5 => "3.5-4.0",
                >= 3.0 => "3.0-3.49",
                >= 2.5 => "2.5-2.99",
                >= 2.0 => "2.0-2.49",
                _ => "Below 2.0"
            })
            .Select(g => new { Range = g.Key, Count = g.Count() })
            .ToList();

        // Course enrollment counts
        var courseEnrollments = await _db.Enrollments
            .Include(e => e.Course)
            .GroupBy(e => e.Course!.Title)
            .Select(g => new { Course = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        // Recent audit logs
        var recentAudits = await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .Select(a => new { a.UserId, a.Method, a.Path, a.StatusCode, a.DurationMs, a.Timestamp })
            .ToListAsync();

        return Ok(new
        {
            TotalStudents = totalStudents,
            TotalGraduates = totalGraduates,
            TotalDoctors = totalDoctors,
            TotalCourses = totalCourses,
            TotalEnrollments = totalEnrollments,
            AverageGpa = Math.Round(avgGpa, 2),
            AverageAttendance = Math.Round(avgAttendance, 1),
            GpaDistribution = gpaRanges,
            TopCourses = courseEnrollments,
            RecentAuditLogs = recentAudits
        });
    }

    /// <summary>View audit logs with filtering.</summary>
    [HttpGet("audit-logs")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetAuditLogs(
        [FromQuery] int? userId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId.Value);

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new { a.Id, a.UserId, a.Method, a.Path, a.StatusCode, a.DurationMs, a.Timestamp, a.IpAddress })
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>Send a notification to a specific user or broadcast to a role.</summary>
    [HttpPost("notifications")]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest("Title and Body are required.");

        if (req.UserId.HasValue)
        {
            // Send to specific user
            _db.Notifications.Add(new Notification
            {
                UserId = req.UserId.Value,
                Title = req.Title,
                Body = req.Body
            });
        }
        else if (req.RoleId.HasValue)
        {
            // Broadcast to all users with a role
            var userIds = await _db.Users
                .Where(u => (int)u.Role == req.RoleId.Value)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var uid in userIds)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = uid,
                    Title = req.Title,
                    Body = req.Body
                });
            }
        }
        else
        {
            return BadRequest("Either UserId or RoleId must be specified.");
        }

        await _db.SaveChangesAsync();
        return Ok(new { Message = "Notification sent." });
    }

    /// <summary>Create an announcement.</summary>
    [HttpPost("announcements")]
    public async Task<ActionResult<object>> CreateAnnouncement([FromBody] CreateAnnouncementRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest("Title and Body are required.");
        if (!req.UniversityId.HasValue)
            return BadRequest("UniversityId is required.");
        if (!await _db.Universities.AnyAsync(u => u.Id == req.UniversityId.Value))
            return BadRequest("University does not exist.");

        var announcement = new Announcement
        {
            Title = req.Title,
            Body = req.Body,
            Category = req.Category ?? "general",
            CreatedByUserId = User.GetUserId(),
            UniversityId = req.UniversityId.Value
        };

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        return Ok(new { announcement.Id, announcement.Title, announcement.Body, announcement.Category, announcement.UniversityId, announcement.CreatedAtUtc });
    }

    /// <summary>Get all announcements (admin). Students use /api/announcements.</summary>
    [HttpGet("announcements")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetAnnouncements()
    {
        var announcements = await _db.Announcements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { a.Id, a.Title, a.Body, a.Category, a.CreatedAtUtc, a.UniversityId })
            .ToListAsync();

        return Ok(announcements);
    }

    /// <summary>Get all users (Admin only).</summary>
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetAllUsers()
    {
        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Major, Role = u.Role.ToString() })
            .ToListAsync();

        return Ok(users);
    }

    // ---- Admin panel: overview ----

    [HttpGet("overview")]
    public async Task<AdminOverviewDto> Overview()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var since7 = now.AddDays(-7);
        var since30 = now.AddDays(-30);
        var since14 = today.AddDays(-13);

        var users = await _db.Users.AsNoTracking().Where(u => !u.IsDeleted).ToListAsync();
        var logins = await _db.LoginRecords.AsNoTracking()
            .Where(l => l.Timestamp >= since14)
            .ToListAsync();

        var perDay = logins
            .GroupBy(l => l.Timestamp.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyCountDto(g.Key, g.Count()))
            .ToList();

        var onTrack = 0;
        var needsAttention = 0;
        var atRisk = 0;
        foreach (var u in users.Where(u => u.IsActive && (u.Role == UserRole.Student || u.Role == UserRole.Graduate)))
        {
            if (!await _analysis.HasEnrollmentsAsync(u.Id)) continue;
            var analysis = await _analysis.AnalyzeAsync(u.Id);
            if (analysis is null) continue;
            if (analysis.RiskLevel == "AtRisk") atRisk++;
            else if (analysis.RiskLevel == "NeedsAttention") needsAttention++;
            else onTrack++;
        }

        var current = await _db.SemesterRecords.AsNoTracking().Where(r => r.IsCurrent).ToListAsync();

        return new AdminOverviewDto(
            users.Count(u => u.Role == UserRole.Student),
            users.Count(u => u.Role == UserRole.Doctor),
            users.Count(u => u.Role == UserRole.Graduate),
            users.Count(u => u.Role == UserRole.Admin),
            users.Count(u => u.CreatedAtUtc >= since7),
            users.Count(u => u.CreatedAtUtc >= since30),
            logins.Where(l => l.Timestamp >= today).Select(l => l.UserId).Distinct().Count(),
            perDay,
            onTrack, needsAttention, atRisk,
            current.Count == 0 ? 0 : Math.Round(current.Average(r => r.Gpa), 2),
            current.Count == 0 ? 0 : Math.Round(current.Average(r => (double)r.AttendanceRate), 1),
            await _db.Courses.CountAsync());
    }

    // ---- Admin panel: user management ----

    /// <summary>Paged user list with search / type / status filters.</summary>
    [HttpGet("users/search")]
    public async Task<PagedResultDto<AdminUserDto>> SearchUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(s) || u.Email.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<UserRole>(type.Trim(), ignoreCase: true, out var role))
                return new PagedResultDto<AdminUserDto>(new List<AdminUserDto>(), 0, page, pageSize);
            query = query.Where(u => u.Role == role);
        }

        query = (status?.Trim().ToLowerInvariant()) switch
        {
            "active" => query.Where(u => u.IsActive && !u.IsDeleted),
            "inactive" => query.Where(u => !u.IsActive && !u.IsDeleted),
            "deleted" => query.Where(u => u.IsDeleted),
            _ => query
        };

        var total = await query.CountAsync();
        var items = await query.OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto(u.Id, u.FullName, u.Email, u.Role.ToString(), (int)u.Role,
                u.IsActive, u.IsDeleted, u.CreatedAtUtc, u.LastLoginAtUtc, u.Major))
            .ToListAsync();

        return new PagedResultDto<AdminUserDto>(items, total, page, pageSize);
    }

    /// <summary>One user's locked profile fields for the admin edit dialog.</summary>
    [HttpGet("users/{id:int}")]
    public async Task<ActionResult<AdminUserDetailDto>> GetUser(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.IsDeleted) return NotFound();
        return await ToAdminUserDetailDtoAsync(user);
    }

    /// <summary>Admin edit of the locked account fields (first/middle/last
    /// name, email, phone, address, department, university). Every change is
    /// audit-logged. Role and password can never be changed here.</summary>
    [HttpPut("users/{id:int}")]
    public async Task<ActionResult<AdminUserDetailDto>> UpdateUser(
        int id,
        [FromBody] AdminUpdateUserRequest req,
        [FromServices] IValidator<AdminUpdateUserRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest
        });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.IsDeleted) return NotFound();

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Id != id && u.Email == email))
            return Conflict("This email is already used by another account.");

        if (req.UniversityId.HasValue && !await _db.Universities.AnyAsync(u => u.Id == req.UniversityId.Value))
            return BadRequest("Selected university does not exist.");

        user.FirstName = req.FirstName.Trim();
        user.MiddleName = string.IsNullOrWhiteSpace(req.MiddleName) ? null : req.MiddleName.Trim();
        user.LastName = req.LastName.Trim();
        user.FullName = string.Join(" ", new[] { user.FirstName, user.MiddleName, user.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        user.Email = email;
        user.PhoneNumber = req.PhoneNumber.Trim();
        user.Address = req.Address?.Trim() ?? "";
        user.Department = req.Department?.Trim() ?? "";
        user.UniversityId = req.UniversityId;

        await LogAdminAsync(User.GetUserId(), $"PUT /api/admin/users/{id}");
        return await ToAdminUserDetailDtoAsync(user);
    }

    private async Task<AdminUserDetailDto> ToAdminUserDetailDtoAsync(User u)
    {
        var universityName = u.UniversityId.HasValue
            ? await _db.Universities.AsNoTracking()
                .Where(x => x.Id == u.UniversityId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync() ?? ""
            : "";
        return new AdminUserDetailDto(
            u.Id, u.FirstName, u.MiddleName, u.LastName, u.FullName,
            u.Email, u.Address, u.PhoneNumber,
            u.UniversityId, universityName, u.Department,
            u.Role.ToString(), (int)u.Role, u.IsActive, u.IsDeleted);
    }

    [HttpPatch("users/{id:int}/status")]
    public async Task<IActionResult> SetUserStatus(int id, PatchUserStatusRequest req)
    {
        var me = User.GetUserId();
        if (id == me) return BadRequest("You cannot change your own status.");
        if (!req.IsActive && await IsLastActiveAdminAsync(id))
            return BadRequest("You cannot deactivate the last active admin.");

        var user = await _db.Users.FindAsync(id);
        if (user is null || user.IsDeleted) return NotFound();
        user.IsActive = req.IsActive;
        await LogAdminAsync(me, $"PATCH /api/admin/users/{id}/status");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("users/{id:int}/role")]
    public async Task<IActionResult> SetUserRole(int id, PatchUserRoleRequest req)
    {
        var validation = await _roleValidator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var me = User.GetUserId();
        if (id == me) return BadRequest("You cannot change your own role.");
        var role = Enum.Parse<UserRole>(req.Role.Trim(), ignoreCase: true);

        var user = await _db.Users.FindAsync(id);
        if (user is null || user.IsDeleted) return NotFound();
        if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastActiveAdminAsync(id))
            return BadRequest("You cannot demote the last active admin.");

        user.Role = role;
        await LogAdminAsync(me, $"PATCH /api/admin/users/{id}/role");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Soft delete: the row stays for history, login is blocked.</summary>
    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var me = User.GetUserId();
        if (id == me) return BadRequest("You cannot delete your own account.");
        if (await IsLastActiveAdminAsync(id))
            return BadRequest("You cannot delete the last active admin.");

        var user = await _db.Users.FindAsync(id);
        if (user is null || user.IsDeleted) return NotFound();
        user.IsDeleted = true;
        user.IsActive = false;
        await LogAdminAsync(me, $"DELETE /api/admin/users/{id}");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> IsLastActiveAdminAsync(int id)
    {
        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (target is null || target.Role != UserRole.Admin || !target.IsActive || target.IsDeleted)
            return false;
        return !await _db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Admin && u.IsActive && !u.IsDeleted);
    }

    private async Task LogAdminAsync(int adminId, string path)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Method = HttpContext.Request.Method,
            Path = path,
            StatusCode = StatusCodes.Status200OK,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
        await _db.SaveChangesAsync();
    }

    // ---- Admin panel: instructor assignments (Part 1) ----

    /// <summary>All doctor-to-course assignments, optionally filtered.</summary>
    [HttpGet("instructor-assignments")]
    public async Task<IReadOnlyList<InstructorAssignmentDto>> InstructorAssignments(
        [FromQuery] int? universityId = null,
        [FromQuery] int? courseId = null)
    {
        var query = _db.CourseInstructors.AsNoTracking().AsQueryable();
        if (courseId.HasValue)
            query = query.Where(ci => ci.CourseId == courseId.Value);
        if (universityId.HasValue)
        {
            var courseIds = _db.Courses.AsNoTracking()
                .Where(c => c.UniversityId == universityId.Value)
                .Select(c => c.Id);
            query = query.Where(ci => courseIds.Contains(ci.CourseId));
        }

        var rows = await query.OrderBy(ci => ci.Id).ToListAsync();
        var courseIds2 = rows.Select(r => r.CourseId).Distinct().ToList();
        var doctorIds = rows.Select(r => r.DoctorUserId).Distinct().ToList();

        var courses = await _db.Courses.AsNoTracking()
            .Where(c => courseIds2.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Title, c.UniversityId })
            .ToListAsync();
        var doctors = await _db.Users.AsNoTracking()
            .Where(u => doctorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync();
        var universityIds = courses.Where(c => c.UniversityId.HasValue)
            .Select(c => c.UniversityId!.Value).Distinct().ToList();
        var universities = await _db.Universities.AsNoTracking()
            .Where(u => universityIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        return rows.Select(r =>
        {
            var course = courses.FirstOrDefault(c => c.Id == r.CourseId);
            return new InstructorAssignmentDto(
                r.Id, r.CourseId, course?.Code ?? "", course?.Title ?? "",
                course?.UniversityId,
                course?.UniversityId is int uid ? universities.GetValueOrDefault(uid, "") : "",
                r.DoctorUserId,
                doctors.FirstOrDefault(d => d.Id == r.DoctorUserId)?.FullName ?? "",
                r.Term, r.CreatedAtUtc);
        }).ToList();
    }

    /// <summary>Assign a doctor to a course for a term. Only doctors of the
    /// course's own university can be assigned.</summary>
    [HttpPost("instructor-assignments")]
    public async Task<ActionResult<InstructorAssignmentDto>> CreateInstructorAssignment(
        [FromBody] CreateInstructorAssignmentRequest req,
        [FromServices] IValidator<CreateInstructorAssignmentRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var course = await _db.Courses.FindAsync(req.CourseId);
        if (course is null) return NotFound("Course not found.");

        var doctor = await _db.Users.FindAsync(req.DoctorUserId);
        if (doctor is null || doctor.IsDeleted || doctor.Role != UserRole.Doctor)
            return NotFound("Doctor not found.");

        if (!course.UniversityId.HasValue || !doctor.UniversityId.HasValue
            || course.UniversityId.Value != doctor.UniversityId.Value)
        {
            return BadRequest("Doctor and course must belong to the same university.");
        }

        var term = req.Term.Trim();
        var exists = await _db.CourseInstructors.AnyAsync(ci =>
            ci.CourseId == course.Id && ci.DoctorUserId == doctor.Id && ci.Term == term);
        if (exists) return Conflict("This doctor is already assigned to the course for that term.");

        var assignment = new CourseInstructor
        {
            CourseId = course.Id,
            DoctorUserId = doctor.Id,
            Term = term
        };
        _db.CourseInstructors.Add(assignment);
        await LogAdminAsync(User.GetUserId(), "POST /api/admin/instructor-assignments");

        var universityName = await _db.Universities.AsNoTracking()
            .Where(u => u.Id == course.UniversityId!.Value)
            .Select(u => u.Name)
            .FirstOrDefaultAsync() ?? "";

        return StatusCode(StatusCodes.Status201Created, new InstructorAssignmentDto(
            assignment.Id, course.Id, course.Code, course.Title,
            course.UniversityId, universityName,
            doctor.Id, doctor.FullName, assignment.Term, assignment.CreatedAtUtc));
    }

    [HttpDelete("instructor-assignments/{id:int}")]
    public async Task<IActionResult> DeleteInstructorAssignment(int id)
    {
        var assignment = await _db.CourseInstructors.FindAsync(id);
        if (assignment is null) return NotFound();

        _db.CourseInstructors.Remove(assignment);
        await LogAdminAsync(User.GetUserId(), $"DELETE /api/admin/instructor-assignments/{id}");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Doctors for the assignment dropdown (optionally by university).</summary>
    [HttpGet("doctors")]
    public async Task<IReadOnlyList<AdminDoctorDto>> Doctors([FromQuery] int? universityId = null)
    {
        var query = _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Doctor && !u.IsDeleted);
        if (universityId.HasValue)
            query = query.Where(u => u.UniversityId == universityId.Value);

        var doctors = await query.OrderBy(u => u.FullName).ToListAsync();
        var uniIds = doctors.Where(d => d.UniversityId.HasValue)
            .Select(d => d.UniversityId!.Value).Distinct().ToList();
        var universities = await _db.Universities.AsNoTracking()
            .Where(u => uniIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        return doctors.Select(d => new AdminDoctorDto(
            d.Id, d.FullName, d.Email, d.UniversityId,
            d.UniversityId.HasValue ? universities.GetValueOrDefault(d.UniversityId.Value, "") : "",
            d.Specialization ?? "", d.IsActive)).ToList();
    }

    /// <summary>Courses for the assignment dropdown (optionally by university).</summary>
    [HttpGet("courses")]
    public async Task<IReadOnlyList<AdminCourseDto>> Courses(
        [FromQuery] int? universityId = null,
        [FromQuery] string? search = null)
    {
        var query = _db.Courses.AsNoTracking().AsQueryable();
        if (universityId.HasValue)
            query = query.Where(c => c.UniversityId == universityId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.Code.ToLower().Contains(s) || c.Title.ToLower().Contains(s));
        }

        return await query.OrderBy(c => c.Code).Take(500)
            .Select(c => new AdminCourseDto(c.Id, c.Code, c.Title, c.UniversityId))
            .ToListAsync();
    }

    // ---- Admin panel: risk, departments, audit, health ----

    [HttpGet("analytics/at-risk-students")]
    public async Task<IReadOnlyList<AtRiskStudentDto>> AtRiskStudents() =>
        await _analysis.GetAtRiskStudentsAsync();

    [HttpGet("analytics/departments")]
    public async Task<IReadOnlyList<DepartmentStatsDto>> Departments()
    {
        var students = await _db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && (u.Role == UserRole.Student || u.Role == UserRole.Graduate))
            .ToListAsync();
        var records = await _db.SemesterRecords.AsNoTracking().Where(r => r.IsCurrent).ToListAsync();

        return students
            .GroupBy(u => string.IsNullOrWhiteSpace(u.Department) ? "Undeclared" : u.Department)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var gpas = g.Select(u => records.FirstOrDefault(r => r.UserId == u.Id))
                    .Where(r => r is not null).ToList();
                return new DepartmentStatsDto(
                    g.Key, g.Count(),
                    gpas.Count == 0 ? 0 : Math.Round(gpas.Average(r => r!.Gpa), 2),
                    gpas.Count == 0 ? 0 : Math.Round(gpas.Average(r => (double)r!.AttendanceRate), 1));
            })
            .ToList();
    }

    [HttpGet("audit-log")]
    public async Task<PagedResultDto<AuditLogDto>> AuditLog([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.UserId, a.Method, a.Path, a.StatusCode,
                a.DurationMs, a.Timestamp, a.IpAddress))
            .ToListAsync();

        return new PagedResultDto<AuditLogDto>(items, total, page, pageSize);
    }

    [HttpGet("system-health")]
    public async Task<SystemHealthDto> SystemHealth()
    {
        var dbOk = false;
        try
        {
            dbOk = await _db.Database.CanConnectAsync();
        }
        catch
        {
            dbOk = false;
        }
        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        return new SystemHealthDto(dbOk ? "Healthy" : "Degraded", dbOk ? "Connected" : "Unreachable",
            Math.Round(uptime.TotalSeconds), version, DateTime.UtcNow);
    }
}

public record SendNotificationRequest(string Title, string Body, int? UserId = null, int? RoleId = null);
public record CreateAnnouncementRequest(string Title, string Body, string? Category = null, int? UniversityId = null);
