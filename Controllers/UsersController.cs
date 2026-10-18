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
public class UsersController(AppDbContext db) : ControllerBase
{
    private readonly AppDbContext _db = db;

    private static UserDto ToDto(AppUser u) =>
        new(u.Id, u.FullName, u.FullName.Split(' ')[0], u.AvatarUrl, u.Major);

    /// <summary>List the users that have a role number (Admin or Doctor only). 404 if the role number does not exist.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet("by-role/{roleId:int}")]
    public async Task<ActionResult<IReadOnlyList<AuthUserDto>>> GetByRole(int roleId)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
            return NotFound($"Role {roleId} not found.");

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.Role == (UserRole)roleId)
            .OrderBy(u => u.Id)
            .ToListAsync();

        return users
            .ConvertAll(u => new AuthUserDto(u.Id, u.FullName, u.Email, (int)u.Role, u.Role.ToString(), u.Major, u.AvatarUrl))
;
    }

    /// <summary>Which role does this user have? (Admin or Doctor only). 404 if the user does not exist.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet("{userId:int}/role")]
    public async Task<ActionResult<RoleDto>> GetUserRole(int userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
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

        var u = await _db.Users.FindAsync(id);
        if (u is null) return NotFound();

        u.FullName = req.FullName.Trim();
        u.Major = req.Major.Trim();
        u.AvatarUrl = req.AvatarUrl;
        await _db.SaveChangesAsync();
        return ToDto(u);
    }
}
