using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class HomeController : ControllerBase
{
    private readonly AppDbContext _db;
    public HomeController(AppDbContext db) => _db = db;

    /// <summary>Everything the Flutter home screen needs in one call.</summary>
    [HttpGet]
    public async Task<ActionResult<HomeResponse>> Get()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var modules = await _db.Modules.AsNoTracking()
            .OrderBy(m => m.SortOrder)
            .Select(m => new ModuleDto(m.Key, m.Title, m.Subtitle, m.Icon, m.BackgroundColor, m.AccentColor))
            .ToListAsync();

        var now = DateTime.UtcNow;
        var events = await _db.Events.AsNoTracking()
            .Where(e => e.StartsAtUtc >= now)
            .OrderBy(e => e.StartsAtUtc)
            .Take(3)
            .Select(e => new EventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.ImageUrl, e.Description))
            .ToListAsync();

        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

        var userDto = new UserDto(user.Id, user.FullName, user.FullName.Split(' ')[0], user.AvatarUrl, user.Major);

        return new HomeResponse(userDto, GreetingFor(DateTime.Now.Hour), unread, modules, events, await BuildRecommendationAsync(user));
    }

    private static string GreetingFor(int hour) =>
        hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";

    private async Task<RecommendationDto> BuildRecommendationAsync(User user)
    {
        var courses = await _db.Courses.AsNoTracking().ToListAsync();
        var top = Recommender.Rank(courses, user).FirstOrDefault();

        return top is null
            ? new RecommendationDto("Your next step matters!", "Explore courses based on your profile.", "/courses/recommended")
            : new RecommendationDto("Your next step matters!", $"We recommend {top.Title} ({top.Code}) for you.", "/courses/recommended");
    }
}
