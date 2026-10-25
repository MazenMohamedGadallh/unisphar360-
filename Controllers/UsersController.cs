using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using AppUser = UniSphere.Api.Models.User;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;

    public UsersController(AppDbContext db, IUniversityScope scope)
    {
        _db = db;
        _scope = scope;
    }

    private static UserDto ToDto(AppUser u) =>
        new(u.Id, u.FullName, u.FullName.Split(' ')[0], u.AvatarUrl, u.Major);

    /// <summary>List the users that have a role number (Admin or Doctor only).
    /// Doctors only see users of their own university; Admin is global.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet("by-role/{roleId:int}")]
    public async Task<ActionResult<IReadOnlyList<AuthUserDto>>> GetByRole(int roleId)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
            return NotFound($"Role {roleId} not found.");

        var scope = await _scope.GetAsync();
        var query = _db.Users.AsNoTracking().Where(u => u.Role == (UserRole)roleId);
        if (!scope.IsAdmin)
        {
            if (!scope.UniversityId.HasValue) return new List<AuthUserDto>();
            var universityId = scope.UniversityId.Value;
            query = query.Where(u => u.UniversityId == universityId);
        }

        var users = await query.OrderBy(u => u.Id).ToListAsync();

        return users
            .ConvertAll(u => new AuthUserDto(u.Id, u.FullName, u.Email, (int)u.Role, u.Role.ToString(), u.Major, u.AvatarUrl))
;
    }

    /// <summary>Which role does this user have? (Admin or Doctor only).
    /// A user from another university is reported as missing.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet("{userId:int}/role")]
    public async Task<ActionResult<RoleDto>> GetUserRole(int userId)
    {
        var scope = await _scope.GetAsync();
        var query = _db.Users.AsNoTracking().Where(u => u.Id == userId);
        if (!scope.IsAdmin)
        {
            if (!scope.UniversityId.HasValue) return NotFound($"User {userId} not found.");
            var universityId = scope.UniversityId.Value;
            query = query.Where(u => u.UniversityId == universityId);
        }

        var user = await query.FirstOrDefaultAsync();
        if (user is null) return NotFound($"User {userId} not found.");
        return new RoleDto((int)user.Role, user.Role.ToString());
    }

    /// <summary>Users can only read / edit their own profile.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetById(int id)
    {
        if (id != User.GetUserId()) return Forbid();
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return u is null ? NotFound() : ToDto(u);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest req)
    {
        if (id != User.GetUserId()) return Forbid();
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Major))
            return BadRequest("FullName and Major are required.");

        var fullName = req.FullName.Trim();
        var major = req.Major.Trim();

        var u = await _db.Users.FindAsync(id);
        if (u is null) return NotFound();

        // Doctor identity fields are locked to the admin panel; from the
        // generic profile endpoint a doctor may only change the avatar.
        // (/api/doctor/profile is the dedicated doctor endpoint.)
        if (u.Role == UserRole.Doctor
            && (!string.Equals(fullName, u.FullName, StringComparison.Ordinal)
                || !string.Equals(major, u.Major, StringComparison.Ordinal)))
        {
            return BadRequest("Doctor identity fields (name, department, email, phone, address, university) are locked. Contact an administrator to change them.");
        }

        u.FullName = fullName;
        u.Major = major;
        u.AvatarUrl = req.AvatarUrl;
        await _db.SaveChangesAsync();
        return ToDto(u);
    }
}
