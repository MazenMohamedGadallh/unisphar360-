using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>Grouped, permission-aware search for the topbar search box.
/// Courses are a public catalog; materials are limited to the student's own
/// university + department + year; plan items, notifications and users are
/// always scoped to what the caller may see.</summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    public static readonly string[] KnownTypes =
        { "course", "material", "plan", "notification", "page", "event", "user" };

    private readonly AppDbContext _db;
    private readonly IValidator<SearchQuery> _validator;
    public SearchController(AppDbContext db, IValidator<SearchQuery> validator)
    {
        _db = db;
        _validator = validator;
    }

    [HttpGet]
    public async Task<ActionResult<SearchResponseDto>> Search([FromQuery] SearchQuery query)
    {
        var validation = await _validator.ValidateAsync(query);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user is null || user.IsDeleted) return Unauthorized();

        var term = query.Q!.Trim();
        var take = query.Take;
        var type = query.Type?.Trim().ToLowerInvariant();
        var groups = new List<SearchGroupDto>();
        bool Want(string t) => string.IsNullOrEmpty(type) || type == t;

        if (Want("course")) groups.Add(await CourseGroupAsync(term, take));
        if (Want("material")) groups.Add(await MaterialGroupAsync(user, term, take));
        if (Want("plan")) groups.Add(await PlanGroupAsync(user, term, take));
        if (Want("notification")) groups.Add(await NotificationGroupAsync(user, term, take));
        if (Want("page")) groups.Add(PageGroup(user, term, take));
        if (Want("event")) groups.Add(await EventGroupAsync(term, take));
        if (user.Role == UserRole.Admin && Want("user")) groups.Add(await UserGroupAsync(term, take));

        return new SearchResponseDto(groups.Where(g => g.Items.Count > 0).ToList());
    }

    private static string LikePattern(string term)
    {
        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }

    private async Task<SearchGroupDto> CourseGroupAsync(string term, int take)
    {
        var pattern = LikePattern(term);
        var matches = await _db.Courses.AsNoTracking()
            .Where(c => EF.Functions.Like(c.Title, pattern, "\\")
                || EF.Functions.Like(c.Code, pattern, "\\")
                || EF.Functions.Like(c.Description, pattern, "\\"))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(c => c.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(c => new SearchItemDto(c.Id, c.Title, c.Code, $"/courses/{c.Id}", "\U0001F393"))
            .ToList();
        return new SearchGroupDto("course", "Courses", items);
    }

    private async Task<SearchGroupDto> MaterialGroupAsync(User user, string term, int take)
    {
        if (!user.UniversityId.HasValue)
            return new SearchGroupDto("material", "Summaries & materials", new List<SearchItemDto>());

        var scopedIds = await UniversityController.InScope(_db.Courses.AsNoTracking(), user)
            .Select(c => c.Id).ToListAsync();
        var pattern = LikePattern(term);
        var matches = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .Where(m => scopedIds.Contains(m.CourseId)
                && (EF.Functions.Like(m.Title, pattern, "\\") || EF.Functions.Like(m.Description, pattern, "\\")))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(m => m.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(m => new SearchItemDto(m.Id, m.Title, $"{m.Course?.Title} · {m.Type}",
                $"/university?materialId={m.Id}", "\U0001F4C4"))
            .ToList();
        return new SearchGroupDto("material", "Summaries & materials", items);
    }

    private async Task<SearchGroupDto> PlanGroupAsync(User user, string term, int take)
    {
        var pattern = LikePattern(term);
        var matches = await _db.StudyPlanItems.AsNoTracking()
            .Where(i => i.UserId == user.Id && EF.Functions.Like(i.Title, pattern, "\\"))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(i => i.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(i => new SearchItemDto(i.Id, i.Title, i.Date.ToString("MMM d"),
                "/study-planner", "\U0001F4C5"))
            .ToList();
        return new SearchGroupDto("plan", "Study plan", items);
    }

    private async Task<SearchGroupDto> NotificationGroupAsync(User user, string term, int take)
    {
        var pattern = LikePattern(term);
        var matches = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == user.Id
                && (EF.Functions.Like(n.Title, pattern, "\\") || EF.Functions.Like(n.Body, pattern, "\\")))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(n => n.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(n => new SearchItemDto(n.Id, n.Title, n.Body, "/notifications", "\U0001F514"))
            .ToList();
        return new SearchGroupDto("notification", "Notifications", items);
    }

    private static SearchGroupDto PageGroup(User user, string term, int take)
    {
        var role = user.Role.ToString();
        var pages = new (string Title, string Route, string[] Roles, string Icon)[]
        {
            ("Home", "/dashboard", new[] { "Student", "Graduate", "Doctor", "Admin" }, "\U0001F3E0"),
            ("Academic Advisor", "/academic-advisor", new[] { "Student", "Graduate", "Doctor" }, "\U0001F393"),
            ("Career Assistant", "/career-assistant", new[] { "Student", "Graduate", "Doctor" }, "\U0001F4BC"),
            ("Research Hub", "/research-hub", new[] { "Student", "Graduate", "Doctor" }, "\U0001F50D"),
            ("My University", "/university", new[] { "Student", "Graduate", "Doctor" }, "\U0001F3DB"),
            ("Student Services", "/student-services", new[] { "Student", "Graduate", "Doctor" }, "\U0001F3E2"),
            ("My Analytics", "/analytics", new[] { "Student", "Graduate" }, "\U0001F4C8"),
            ("Admin Panel", "/admin", new[] { "Admin" }, "\U0001F4CA"),
            ("My Profile", "/profile", new[] { "Student", "Graduate", "Doctor", "Admin" }, "\U0001F464"),
            ("Notifications", "/notifications", new[] { "Student", "Graduate", "Doctor", "Admin" }, "\U0001F514"),
            ("Settings", "/settings", new[] { "Student", "Graduate", "Doctor", "Admin" }, "⚙"),
            ("Help & Support", "/help", new[] { "Student", "Graduate", "Doctor", "Admin" }, "❓"),
            ("Search", "/search", new[] { "Student", "Graduate", "Doctor", "Admin" }, "\U0001F50D"),
        };

        var items = pages
            .Where(p => p.Roles.Contains(role) && p.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(p => new SearchItemDto(null, p.Title, "Page", p.Route, p.Icon))
            .ToList();
        return new SearchGroupDto("page", "Pages", items);
    }

    private async Task<SearchGroupDto> EventGroupAsync(string term, int take)
    {
        var pattern = LikePattern(term);
        var matches = await _db.Events.AsNoTracking()
            .Where(e => EF.Functions.Like(e.Title, pattern, "\\") || EF.Functions.Like(e.Location, pattern, "\\"))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(e => e.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(e => new SearchItemDto(e.Id, e.Title, e.Location, "/student-services", "\U0001F4CD"))
            .ToList();
        return new SearchGroupDto("event", "Events", items);
    }

    private async Task<SearchGroupDto> UserGroupAsync(string term, int take)
    {
        var pattern = LikePattern(term);
        var matches = await _db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted
                && (EF.Functions.Like(u.FullName, pattern, "\\") || EF.Functions.Like(u.Email, pattern, "\\")))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(u => u.FullName.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(u => new SearchItemDto(u.Id, u.FullName, $"{u.Email} · {u.Role}", "/admin", "\U0001F464"))
            .ToList();
        return new SearchGroupDto("user", "Users", items);
    }
}
