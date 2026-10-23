using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/jobs")]
public class GraduateJobsController(
    JobCatalogService jobs,
    JobMatchingService matching) : ControllerBase
{
    private int UserId => User.GetUserId();

    /// <summary>Search the job catalog (demo + synced live rows), paged and filtered.
    /// Also answers the plain "api/grad/jobs" base route used by the client search page.</summary>
    [HttpGet]
    [HttpGet("search")]
    public async Task<ActionResult<PagedJobsDto>> Search(
        [FromQuery] string? search, [FromQuery] string? location, [FromQuery] string? industry,
        [FromQuery] string? remote, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10) =>
        await jobs.SearchAsync(UserId, page, pageSize, search, location, industry, remote, sort);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobListingDto>> Get(int id) =>
        await jobs.GetAsync(UserId, id) is { } job ? job : NotFound(new { error = "Job not found." });

    // ---- Saved jobs ----

    [HttpPost("{id:int}/save")]
    public async Task<IActionResult> Save(int id)
    {
        try
        {
            await jobs.SaveJobAsync(UserId, id);
            return Ok(new { saved = true });
        }
        catch (KeyNotFoundException) { return NotFound(new { error = "Job not found." }); }
        catch (InvalidOperationException)
        {
            return StatusCode(409, new { error = "This job is already in your saved list." });
        }
    }

    [HttpDelete("{id:int}/save")]
    public async Task<IActionResult> Unsave(int id) =>
        await jobs.UnsaveJobAsync(UserId, id) ? NoContent() : NotFound(new { error = "Saved job not found." });

    [HttpGet("saved")]
    public async Task<ActionResult<IReadOnlyList<JobListingDto>>> Saved() =>
        Ok(await jobs.ListSavedAsync(UserId));

    // ---- Live sync (Remotive) ----

    /// <summary>Sync the catalog with the live remote-jobs feed (throttled: once per 5 minutes).</summary>
    [HttpGet("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var result = await jobs.SyncRemotiveAsync(ct);
        if (result.Error is not null) return Ok(result); // feed down is a state, not a crash
        return Ok(result);
    }

    [HttpGet("insights")]
    public async Task<ActionResult<GradInsightsDto>> Insights() =>
        await jobs.GetInsightsAsync();

    // ---- AI-free matching + skill gap ----

    /// <summary>Deterministic match ranking for the documented scoring formula (no AI used).</summary>
    [HttpGet("matches")]
    public async Task<ActionResult<IReadOnlyList<JobMatchDto>>> Matches(
        [FromQuery] int limit = 10, [FromQuery] int minScore = 20) =>
        Ok(await matching.MatchForUserAsync(UserId, limit, minScore));

    /// <summary>Required-vs-actual skills for a catalog target role (deterministic).</summary>
    [HttpGet("skill-gap")]
    public async Task<ActionResult<SkillGapReportDto>> SkillGap([FromQuery] string? role)
    {
        try
        {
            var gap = await matching.SkillGapAsync(UserId, role);
            return Ok(new SkillGapReportDto(
                gap.TargetRole, gap.CoveragePercent, gap.Matched, gap.Missing, gap.Steps));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
