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
    private readonly IValidator<AdminUpdateUserRequest> _userValidator;
    public AdminController(
        AppDbContext db,
        PerformanceAnalysisService analysis,
        IValidator<PatchUserRoleRequest> roleValidator,
        IValidator<AdminUpdateUserRequest> userValidator)
    {
        _db = db;
        _analysis = analysis;
        _roleValidator = roleValidator;
        _userValidator = userValidator;
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

        var announcement = new Announcement
        {
            Title = req.Title,
            Body = req.Body,
            Category = req.Category ?? "general",
            CreatedByUserId = User.GetUserId()
        };

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        return Ok(new { announcement.Id, announcement.Title, announcement.Body, announcement.Category, announcement.CreatedAtUtc });
    }

    /// <summary>Get all announcements.</summary>
    [AllowAnonymous]
    [HttpGet("announcements")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetAnnouncements()
    {
        var announcements = await _db.Announcements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { a.Id, a.Title, a.Body, a.Category, a.CreatedAtUtc })
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

    /// <summary>Full profile of one user for the admin edit dialog. Admins
    /// are the only ones who can change these locked fields.</summary>
    [HttpGet("users/{id:int}")]
    public async Task<ActionResult<AdminUserDetailDto>> GetUser(int id)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.IsDeleted) return NotFound();

        string? universityName = null;
        if (user.UniversityId is not null)
        {
            universityName = await _db.Universities.AsNoTracking()
                .Where(u => u.Id == user.UniversityId.Value)
                .Select(u => u.Name)
                .FirstOrDefaultAsync();
        }

        return new AdminUserDetailDto(
            user.Id, user.FirstName, user.MiddleName, user.LastName,
            user.Email, user.Address, user.PhoneNumber,
            user.Department, user.UniversityId, universityName,
            user.Role.ToString(), user.IsActive, user.IsDeleted);
    }

    /// <summary>PUT /api/admin/users/{id} — admins edit the fields that are
    /// LOCKED for the user (name parts, email, address, phone, university,
    /// department). Never model-binds the User entity, never touches role/
    /// activity/password (dedicated endpoints exist for those), keeps
    /// FullName in sync with the name parts, validates the email is unique
    /// and that the university exists, and audit-logs the change.</summary>
    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] AdminUpdateUserRequest req)
    {
        var validation = await _userValidator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var email = req.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.IsDeleted) return NotFound();

        if (await _db.Users.AnyAsync(u => u.Id != id && u.Email == email))
            return Conflict("This email is already used by another account.");

        int? universityId = user.UniversityId; // keep current when not supplied
        if (req.UniversityId.HasValue)
        {
            if (!await _db.Universities.AnyAsync(u => u.Id == req.UniversityId.Value))
                return BadRequest("Selected university does not exist.");
            universityId = req.UniversityId.Value;
        }

        user.FirstName = req.FirstName.Trim();
        user.MiddleName = string.IsNullOrWhiteSpace(req.MiddleName) ? null : req.MiddleName.Trim();
        user.LastName = req.LastName.Trim();
        user.FullName = string.Join(" ",
            new[] { user.FirstName, user.MiddleName, user.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        user.Email = email;
        user.Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();
        user.PhoneNumber = req.PhoneNumber?.Trim() ?? user.PhoneNumber;
        user.Department = req.Department?.Trim() ?? user.Department;
        user.UniversityId = universityId;

        await LogAdminAsync(User.GetUserId(), $"PUT /api/admin/users/{id} (locked profile fields)");
        await _db.SaveChangesAsync();
        return NoContent();
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
public record CreateAnnouncementRequest(string Title, string Body, string? Category = null);
