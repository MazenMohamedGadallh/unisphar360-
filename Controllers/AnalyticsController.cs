using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>Personal performance analytics for the logged-in student.
/// The user id always comes from the JWT claims, never from the client.</summary>
[Authorize(Roles = "Student")]
[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly PerformanceAnalysisService _analysis;
    public AnalyticsController(PerformanceAnalysisService analysis) => _analysis = analysis;

    [HttpGet("me/performance")]
    public async Task<ActionResult<PerformanceAnalysisDto>> MyPerformance()
    {
        var dto = await _analysis.AnalyzeAsync(User.GetUserId());
        return dto is null ? NotFound("No academic data for this user yet.") : dto;
    }

    [HttpGet("me/gpa-trend")]
    public async Task<IReadOnlyList<GpaTrendPointDto>> GpaTrend() =>
        await _analysis.GetGpaTrendAsync(User.GetUserId());
}
