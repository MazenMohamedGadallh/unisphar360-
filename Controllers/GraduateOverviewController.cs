using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/overview")]
public class GraduateOverviewController(GraduateCareerService career, GraduateNotificationService notifications) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<GraduateOverviewDto>> Get()
    {
        await notifications.GenerateAllAsync(UserId);
        return await career.GetOverviewAsync(UserId);
    }
}

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

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/insights")]
public class GraduateInsightsController(JobCatalogService jobs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GradInsightsDto>> Get() =>
        await jobs.GetInsightsAsync();
}

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
            request.MatchAlertThresholdPercent, request.ShowGpaOnProfile,
            request.ThemePreference, request.AlertKeywords, request.AlertLocations);
}

public record GradSettingsUpdateRequest(
    bool NotifyNewMatches, bool NotifyFollowUps, int MatchAlertThresholdPercent, bool ShowGpaOnProfile,
    string ThemePreference = "system",
    string? AlertKeywords = null,
    string? AlertLocations = null);
