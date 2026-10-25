using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using AppUser = UniSphere.Api.Models.User;

namespace UniSphere.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly IValidator<RegisterRequest> _registerValidator;

    public AuthController(AppDbContext db, TokenService tokens, IValidator<RegisterRequest> registerValidator)
    {
        _db = db;
        _tokens = tokens;
        _registerValidator = registerValidator;
    }

    private static AuthUserDto ToDto(AppUser u) =>
        new(u.Id, u.FullName, u.Email, (int)u.Role, u.Role.ToString(), u.Major, u.AvatarUrl);

    private AuthResponse BuildResponse(AppUser user)
    {
        var (token, expires) = _tokens.Create(user);
        return new AuthResponse(token, expires, ToDto(user));
    }

    /// <summary>Create a Student, Graduate or Doctor account and return a token (the user is logged in right away).
    /// Admin accounts can never be created through registration.</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var validation = await _registerValidator.ValidateAsync(req);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict("This email is already registered.");

        if (!RegistrationRules.TryParseUserType(req.Role, out var userType))
            return BadRequest("Role must be Student, Doctor or Graduate.");
        // UserType has no Admin member, so Admin can never be registered here.
        var role = RegistrationRules.ToUserRole(userType);

        int? universityId = null;
        if (req.UniversityId.HasValue)
        {
            if (!await _db.Universities.AnyAsync(u => u.Id == req.UniversityId.Value))
                return BadRequest("Selected university does not exist.");
            universityId = req.UniversityId.Value;
        }

        var fullName = string.Join(" ", new[] { req.FirstName?.Trim(), req.MiddleName?.Trim(), req.LastName?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var user = new AppUser
        {
            FirstName = req.FirstName?.Trim() ?? "",
            MiddleName = string.IsNullOrWhiteSpace(req.MiddleName) ? null : req.MiddleName.Trim(),
            LastName = req.LastName?.Trim() ?? "",
            FullName = fullName,
            PhoneNumber = req.PhoneNumber?.Trim() ?? "",
            Email = email,
            Address = req.Address?.Trim() ?? "",
            Gender = req.Gender.Trim(),
            Age = req.Age,
            Major = string.IsNullOrWhiteSpace(req.Department) ? "Undeclared" : req.Department.Trim(),
            Department = req.Department?.Trim() ?? "",
            UniversityId = universityId,
            AcademicYear = req.AcademicYear,
            Specialization = string.IsNullOrWhiteSpace(req.Specialization) ? null : req.Specialization.Trim(),
            GraduatedFrom = string.IsNullOrWhiteSpace(req.GraduatedFrom) ? null : req.GraduatedFrom.Trim(),
            GraduationYear = req.GraduationYear,
            PasswordHash = PasswordService.Hash(req.Password),
            Role = role
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Empty academic record + welcome items so the dashboard works for a brand-new student / graduate.
        if (role is UserRole.Student or UserRole.Graduate)
            _db.SemesterRecords.Add(new SemesterRecord { UserId = user.Id, Label = "Current semester", IsCurrent = true });
        _db.Notifications.Add(new Notification { UserId = user.Id, Title = "Welcome to UniSphere 360+", Body = "Your account is ready. Explore your dashboard." });
        _db.Activities.Add(new ActivityItem { UserId = user.Id, Kind = "announcement", Message = "Welcome to UniSphere 360+" });
        // The audit middleware only logs authenticated calls, so record
        // this anonymous registration explicitly in the same table.
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Method = HttpContext.Request.Method,
            Path = HttpContext.Request.Path.Value ?? "/api/auth/register",
            StatusCode = StatusCodes.Status200OK,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
        await _db.SaveChangesAsync();

        return Ok(BuildResponse(user));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null || user.IsDeleted || string.IsNullOrEmpty(req.Password) || !PasswordService.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Invalid email or password.");

        if (!user.IsActive)
            return StatusCode(StatusCodes.Status403Forbidden, "This account has been deactivated. Contact an administrator.");

        if (req.RoleId is not null && req.RoleId != (int)user.Role)
            return Unauthorized("This account is not registered with the selected role.");

        user.LastLoginAtUtc = DateTime.UtcNow;
        _db.LoginRecords.Add(new LoginRecord { UserId = user.Id });
        await _db.SaveChangesAsync();

        return Ok(BuildResponse(user));
    }

    /// <summary>All roles from the database (1 Student, 2 Graduate, 3 Doctor, 4 Admin).</summary>
    [AllowAnonymous]
    [HttpGet("roles")]
    public async Task<IReadOnlyList<RoleDto>> Roles() =>
        await _db.Roles.AsNoTracking().OrderBy(r => r.Id).Select(r => new RoleDto(r.Id, r.Name)).ToListAsync();

    /// <summary>Universities for the registration dropdown.</summary>
    [AllowAnonymous]
    [HttpGet("universities")]
    public async Task<IReadOnlyList<UniversityDto>> Universities() =>
        await _db.Universities.AsNoTracking().OrderBy(u => u.Name)
            .Select(u => new UniversityDto(u.Id, u.Name, u.Logo)).ToListAsync();

    /// <summary>Look up one role by its number. Returns 404 if the number is not in the database.</summary>
    [AllowAnonymous]
    [HttpGet("roles/{roleId:int}")]
    public async Task<ActionResult<RoleDto>> RoleById(int roleId)
    {
        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId);
        return role is null ? NotFound($"Role {roleId} not found.") : new RoleDto(role.Id, role.Name);
    }

    /// <summary>The logged-in user (use it to check that a token is valid).</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me()
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        return user is null ? Unauthorized() : ToDto(user);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest("New password must be at least 8 characters.");

        var user = await _db.Users.FindAsync(User.GetUserId());
        if (user is null) return Unauthorized();
        if (!PasswordService.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return BadRequest("Current password is incorrect.");

        user.PasswordHash = PasswordService.Hash(req.NewPassword);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Method = HttpContext.Request.Method,
            Path = HttpContext.Request.Path.Value ?? "/api/auth/change-password",
            StatusCode = StatusCodes.Status204NoContent,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
