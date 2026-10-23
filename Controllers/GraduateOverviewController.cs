using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>The graduate dashboard aggregate (profile state, matches,
/// applications, roadmap progress, events, activity, unread notifications).</summary>
[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/overview")]
public class GraduateOverviewController(GraduateCareerService career, GraduateNotificationService notifications) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<GraduateOverviewDto>> Get()
    {
        // Idempotent career notification generation runs when the graduate
        // dashboard loads: notifications exist by the time the unread badge
        // is computed (so the count and page always agree).
        await notifications.GenerateAllAsync(UserId);
        return await career.GetOverviewAsync(UserId);
    }
}

/// <summary>Alumni & university services view (role-aware university data).</summary>
[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/alumni")]
public class GraduateAlumniController(GraduateCareerService career) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<GradAlumniDto>> Get() =>
        await career.GetAlumniAsync(UserId);
}

/// <summary>What the job catalog looks like top-down (skills, industries, work types).</summary>
[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/insights")]
public class GraduateInsightsController(JobCatalogService jobs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GradInsightsDto>> Get() =>
        await jobs.GetInsightsAsync();
}

/// <summary>Career notification preferences + profile privacy.</summary>
[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/settings")]
public class GraduateSettingsController(GraduateCareerService career) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<GradSettingsDto>> Settings() =>
        await career.GetSettingsAsync(UserId);

    [HttpPut]
    public async Task<ActionResult<GradSettingsDto>> UpdateSettings([FromBody] GradSettingsUpdateRequest request) =>
        await career.UpdateSettingsAsync(UserId, request.NotifyNewMatches, request.NotifyFollowUps,
            request.MatchAlertThresholdPercent, request.ShowGpaOnProfile);
}

public record GradSettingsUpdateRequest(
    bool NotifyNewMatches, bool NotifyFollowUps, int MatchAlertThresholdPercent, bool ShowGpaOnProfile);
