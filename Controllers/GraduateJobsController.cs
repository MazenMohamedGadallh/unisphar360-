using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/jobs")]
public class GraduateJobsController(
    JobCatalogService jobs,
    JobMatchingService matching,
    AppDbContext db,
    CvFileService cvs,
    GraduateAiService ai,
    InterviewService interviews) : ControllerBase
{
    private int UserId => User.GetUserId();

    // AI preparation and outreach, grounded in the CV and this job's description

    [HttpPost("/api/graduate/jobs/{jobId:int}/prepare")]
    public Task<IActionResult> Prepare(int jobId, CancellationToken ct) =>
        WithCvAndJobAsync(jobId, async (cv, job, documents) =>
        {
            var (prep, meta) = await ai.PrepareForJobAsync(job.Title, documents, ct);
            var session = await interviews.CreateFromQuestionsAsync(
                UserId, job.Title, job.Id, prep.Questions, meta.ProviderName, meta.Model);
            // Drop "gaps" the CV already contains: that is checkable, so the model is not trusted on it.
            var missing = prep.MissingKeywords
                .Where(k => !Regex.IsMatch(cv, $@"(?<!\w){Regex.Escape(k)}(?!\w)", RegexOptions.IgnoreCase))
                .ToList();
            return new JobPrepDto(missing, prep.Bullets, session);
        });

    [HttpPost("/api/graduate/jobs/{jobId:int}/outreach")]
    public Task<IActionResult> Outreach(int jobId, OutreachRequest request, CancellationToken ct) =>
        WithCvAndJobAsync(jobId, async (_, job, documents) =>
        {
            var tone = string.Equals(request.Tone, "Direct", StringComparison.OrdinalIgnoreCase) ? "Direct" : "Formal";
            var name = await db.Users.Where(u => u.Id == UserId).Select(u => u.FullName).FirstAsync(ct);
            return await ai.DraftOutreachAsync(tone, name, job.Title, job.CompanyName, documents, ct);
        });

    private async Task<IActionResult> WithCvAndJobAsync<T>(
        int jobId, Func<string, JobListing, List<RagDocument>, Task<T>> action)
    {
        var job = await db.JobListings.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is null) return NotFound(new { error = "Job not found." });
        var cv = await cvs.GetLatestTextAsync(UserId);
        if (string.IsNullOrWhiteSpace(cv))
            return UnprocessableEntity(new { error = "Upload a readable CV in CV Studio first." });
        try
        {
            return Ok(await action(cv, job,
            [
                new("cv", "Candidate CV", cv),
                new("job", "Job description",
                    $"{job.Title} at {job.CompanyName}\nSkills: {job.KeySkillsCsv}\n{job.Description}")
            ]));
        }
        catch (ChatProviderException ex)
        {
            return Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
        }
    }

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

    // Saved jobs

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

    // Live sync (Remotive)

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

    // AI-free matching + skill gap

    [HttpGet("matches")]
    public async Task<ActionResult<IReadOnlyList<JobMatchDto>>> Matches(
        [FromQuery] int limit = 10, [FromQuery] int minScore = 20) =>
        Ok(await matching.MatchForUserAsync(UserId, limit, minScore));

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
