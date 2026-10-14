using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AcademicController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AcademicService _academic;

    public AcademicController(AppDbContext db, AcademicService academic)
    {
        _db = db;
        _academic = academic;
    }

    /// <summary>Everything the Academic Advisor screen needs in one call (for the logged-in user).</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<AcademicDashboardResponse>> Dashboard()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        var stats = await _academic.GetStatsAsync(userId);
        if (stats is null) return NotFound("No academic record for this user.");

        var first = user.FullName.Split(' ')[0];
        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";

        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

        return new AcademicDashboardResponse(
            new UserDto(user.Id, user.FullName, first, user.AvatarUrl, user.Major),
            $"{greeting}, {first}!",
            "Your academic journey matters. Let's make it better together.",
            unread,
            stats,
            await _academic.GetPerformanceAsync(userId),
            await _academic.GetPlannerAsync(userId),
            await _academic.GetInsightsAsync(userId),
            await _academic.GetRecommendedAsync(userId, 4),
            AcademicService.QuickActions,
            await _academic.GetActivityAsync(userId, 4));
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AcademicStats>> Stats()
    {
        var stats = await _academic.GetStatsAsync(User.GetUserId());
        return stats is null ? NotFound() : stats;
    }

    [HttpGet("performance")]
    public async Task<IReadOnlyList<GradeRowDto>> Performance() =>
        await _academic.GetPerformanceAsync(User.GetUserId());

    [HttpGet("insights")]
    public async Task<IReadOnlyList<InsightDto>> Insights() =>
        await _academic.GetInsightsAsync(User.GetUserId());

    [HttpGet("quick-actions")]
    public IReadOnlyList<QuickActionDto> QuickActions() => AcademicService.QuickActions;
}
