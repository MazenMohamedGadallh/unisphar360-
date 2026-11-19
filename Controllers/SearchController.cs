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
/// Courses, materials and events are limited to the caller's university;
/// study-plan results are student-only and users are admin-only.</summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    public static readonly string[] KnownTypes =
        { "course", "material", "plan", "notification", "page", "event", "user" };

    private readonly AppDbContext _db;
    private readonly IValidator<SearchQuery> _validator;
    private readonly IUniversityScope _scope;

    public SearchController(
        AppDbContext db, IValidator<SearchQuery> validator, IUniversityScope scope)
    {
        _db = db;
        _validator = validator;
        _scope = scope;
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

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user is null || user.IsDeleted) return Unauthorized();
        var scope = await _scope.GetAsync();

        var term = query.Q!.Trim();
        var take = query.Take;
        var type = query.Type?.Trim().ToLowerInvariant();
        var groups = new List<SearchGroupDto>();
        bool Want(string t) => string.IsNullOrEmpty(type) || type == t;

        if (user.Role != UserRole.Graduate && Want("course"))
            groups.Add(await CourseGroupAsync(scope, user, term, take));
        if (user.Role is UserRole.Student or UserRole.Doctor && Want("material"))
            groups.Add(await MaterialGroupAsync(scope, user, term, take));
        if (user.Role == UserRole.Student && Want("plan"))
            groups.Add(await PlanGroupAsync(user, term, take));
        if (Want("notification"))
            groups.Add(await NotificationGroupAsync(user, term, take));
        if (Want("page"))
            groups.Add(PageGroup(user, term, take));
        if (user.Role is UserRole.Student or UserRole.Graduate or UserRole.Admin && Want("event"))
            groups.Add(await EventGroupAsync(scope, user, term, take));
        if (user.Role == UserRole.Admin && Want("user"))
            groups.Add(await UserGroupAsync(term, take));

        return new SearchResponseDto(groups.Where(g => g.Items.Count > 0).ToList());
    }

    private static string LikePattern(string term)
    {
        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }

    private async Task<SearchGroupDto> CourseGroupAsync(
        UniversityScopeSnapshot scope, User user, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.Courses.AsNoTracking().VisibleToCurrentUser(scope);
        if (user.Role == UserRole.Student)
            query = query.EligibleForStudent(user);

        var matches = await query
            .Where(c => EF.Functions.Like(c.Title, pattern, "\\")
                || EF.Functions.Like(c.Code, pattern, "\\")
                || EF.Functions.Like(c.Description, pattern, "\\"))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(c => c.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(c => new SearchItemDto(c.Id, c.Title, c.Code,
                user.Role switch
                {
                    UserRole.Student => $"/courses/{c.Id}",
                    UserRole.Doctor => "/doctor/courses",
                    _ => "/admin"
                }, "\U0001F393"))
            .ToList();
        return new SearchGroupDto("course", "Courses", items);
    }

    private async Task<SearchGroupDto> MaterialGroupAsync(
        UniversityScopeSnapshot scope, User user, string term, int take)
    {
        var pattern = LikePattern(term);
        var materialQuery = _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .VisibleToCurrentUser(scope);

        if (user.Role == UserRole.Student)
        {
            var courseIds = await _db.Courses.AsNoTracking()
                .VisibleToCurrentUser(scope)
                .EligibleForStudent(user)
                .Select(c => c.Id)
                .ToListAsync();
            materialQuery = materialQuery.Where(m => courseIds.Contains(m.CourseId));
        }

        var matches = await materialQuery
            .Where(m => EF.Functions.Like(m.Title, pattern, "\\")
                || EF.Functions.Like(m.Description, pattern, "\\"))
            .Take(take * 2)
            .ToListAsync();

        var items = matches
            .OrderByDescending(m => m.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(m => new SearchItemDto(m.Id, m.Title, $"{m.Course?.Title} · {m.Type}",
                user.Role == UserRole.Doctor
                    ? "/doctor/materials"
                    : $"/university?materialId={m.Id}", "\U0001F4C4"))
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
            ("Home", RoleRoutes.HomeFor(role), new[] { "Student", "Doctor", "Graduate", "Admin" }, "\U0001F3E0"),
            ("My University", "/university", new[] { "Student" }, "\U0001F3DB"),
            ("Academic Advisor", "/academic-advisor", new[] { "Student" }, "\U0001F393"),
            ("Career", "/career-assistant", new[] { "Student" }, "\U0001F4BC"),
            ("Research Hub", "/research-hub", new[] { "Student", "Graduate" }, "\U0001F50D"),
            ("Student Services", "/student-services", new[] { "Student" }, "\U0001F3E2"),
            ("My Analytics", "/analytics", new[] { "Student" }, "\U0001F4C8"),
            ("My Exams", "/student/exams", new[] { "Student" }, "\U0001F4DD"),
            ("AI Assistant", "/chat", new[] { "Student", "Doctor", "Graduate" }, "\U0001F4F1"),
            ("Student Events", "/events", new[] { "Student" }, "\U0001F4C5"),
            ("Doctor Dashboard", "/doctor", new[] { "Doctor" }, "\U0001F3E0"),
            ("My Courses", "/doctor/courses", new[] { "Doctor" }, "\U0001F4DA"),
            ("My Students", "/doctor/students", new[] { "Doctor" }, "\U0001F465"),
            ("Materials", "/doctor/materials", new[] { "Doctor" }, "\U0001F4C4"),
            ("Announcements", "/doctor/announcements", new[] { "Doctor" }, "\U0001F4E2"),
            ("Doctor Profile", "/doctor/profile", new[] { "Doctor" }, "\U0001F464"),
            ("Doctor Research", "/doctor/research", new[] { "Doctor" }, "\U0001F50D"),
            ("Doctor AI Assistant", "/doctor/ai", new[] { "Doctor" }, "\U0001F4F1"),
            ("Exam Assistant", "/doctor/exam-assistant", new[] { "Doctor" }, "\U0001F4DA"),
            ("CV Analysis", "/graduate/cv", new[] { "Graduate" }, "\U0001F4C4"),
            ("AI Job Matches", "/graduate/matches", new[] { "Graduate" }, "\U0001F44D"),
            ("Graduated Events", "/graduate/events", new[] { "Graduate" }, "\U0001F389"),
            ("Admin Panel", "/admin", new[] { "Admin" }, "\U0001F4CA"),
            ("My Profile", "/profile", new[] { "Student", "Graduate", "Doctor", "Admin" }, "\U0001F464"),
            ("Notifications", "/notifications", new[] { "Student", "Graduate", "Doctor", "Admin" }, "\U0001F514"),
            ("Settings", role == "Graduate" ? "/graduate/settings" : "/settings", new[] { "Student", "Graduate", "Doctor", "Admin" }, "⚙"),
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

    private async Task<SearchGroupDto> EventGroupAsync(
        UniversityScopeSnapshot scope, User user, string term, int take)
    {
        var pattern = LikePattern(term);
        var matches = await _db.Events.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .Where(e => EF.Functions.Like(e.Title, pattern, "\\")
                || EF.Functions.Like(e.Location, pattern, "\\"))
            .Take(take * 2)
            .ToListAsync();

        var route = user.Role switch
        {
            UserRole.Graduate => "/graduate/events",
            UserRole.Admin => "/admin",
            _ => "/events"
        };
        var items = matches
            .OrderByDescending(e => e.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(e => new SearchItemDto(e.Id, e.Title, e.Location, route, "\U0001F4CD"))
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
