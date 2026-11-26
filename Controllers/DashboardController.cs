using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Student")]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AcademicService _academic;
    public DashboardController(AcademicService academic) => _academic = academic;

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary()
    {
        var summary = await _academic.GetDashboardSummaryAsync(User.GetUserId());
        return summary is null ? NotFound() : summary;
    }

    [HttpGet("performance")]
    public async Task<IReadOnlyList<DashboardPerformanceRowDto>> Performance([FromQuery] string? semester = "current") =>
        await _academic.GetDashboardPerformanceAsync(User.GetUserId(), semester);

    [HttpGet("study-plan")]
    public async Task<DashboardStudyPlanDto> StudyPlan([FromQuery] DateTime? weekStart = null) =>
        await _academic.GetDashboardStudyPlanAsync(User.GetUserId(), weekStart);

    [HttpPatch("study-plan/{id:int}/toggle")]
    public async Task<ActionResult<DashboardStudyDayDto>> Toggle(int id)
    {
        var day = await _academic.ToggleDashboardStudyDayAsync(User.GetUserId(), id);
        return day is null ? NotFound() : day;
    }

    [HttpGet("insights")]
    public async Task<IReadOnlyList<DashboardInsightDto>> Insights() =>
        await _academic.GetDashboardInsightsAsync(User.GetUserId());

    [HttpGet("recommended-courses")]
    public async Task<IReadOnlyList<DashboardRecommendedCourseDto>> RecommendedCourses() =>
        await _academic.GetDashboardRecommendedAsync(User.GetUserId());

    [HttpGet("recent-activity")]
    public async Task<IReadOnlyList<DashboardActivityDto>> RecentActivity([FromQuery] int take = 4) =>
        await _academic.GetDashboardActivityAsync(User.GetUserId(), take);
}
