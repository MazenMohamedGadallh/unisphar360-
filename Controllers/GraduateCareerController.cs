using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

public sealed record SkillGapRequest(string? Role, int? JobId, string? JobDescription);

public sealed record ProjectRequest(
    string? Title, string? Description, string? Problem, string? Technologies, string? Role,
    string? Outcomes, string? Skills, string? GitHubUrl, string? DemoUrl);

public sealed record DnaReport(DateTime GeneratedAtUtc, string ProviderName, string Model, GraduateAiService.CareerDnaData Data);

/// <summary>Career journey, Career DNA, skill gap, Skill Proof Lab, portfolio and reports. Every query is scoped to the caller.</summary>
[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/graduate")]
public class GraduateCareerController(
    AppDbContext db,
    CareerEvidenceService evidence,
    CareerJourneyService journey,
    SkillLabService lab,
    GraduateAiService ai) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet("journey")]
    public async Task<ActionResult<JourneyDto>> Journey() => await journey.GetAsync(UserId);

    // Career DNA

    [HttpGet("dna")]
    public async Task<IActionResult> Dna()
    {
        var e = await evidence.GatherAsync(UserId);
        var report = e.Profile.CareerDnaJson is null ? null : JsonSerializer.Deserialize<DnaReport>(e.Profile.CareerDnaJson);
        var cvSkills = CareerCatalog.DetectSkills(e.CvText);
        return Ok(new
        {
            report,
            // Profile edits, new CV analyses, projects and challenges all make an older assessment out of date.
            stale = report is not null && new[]
            {
                e.Profile.UpdatedAtUtc,
                e.Analyses.Count == 0 ? DateTime.MinValue : e.Analyses[^1].CreatedAtUtc,
                e.Projects.Count == 0 ? DateTime.MinValue : e.Projects.Max(p => p.UpdatedAtUtc),
                e.Challenges.Count == 0 ? DateTime.MinValue : e.Challenges.Max(c => c.SubmittedAtUtc!.Value)
            }.Max() > report.GeneratedAtUtc,
            reportedSkills = e.Skills.Select(s => new { s.Name, s.Level }),
            cvSkills,
            verifiedSkills = e.VerifiedSkills,
            readiness = CareerJourneyService.Snapshot(e, 0).Dimensions.Where(d => d.Name != "Technical evidence")
        });
    }

    [HttpPost("dna")]
    public async Task<IActionResult> GenerateDna(CancellationToken ct)
    {
        var e = await evidence.GatherAsync(UserId);
        if (!e.Profile.HasCompletedOnboarding && e.CvText.Length == 0)
            return UnprocessableEntity(new { error = "Finish onboarding or upload a CV first, so there is something to assess." });

        var entries = await db.ProfileEntries.AsNoTracking().Where(x => x.UserId == UserId).ToListAsync(ct);
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId, ct);
        var p = e.Profile;
        var text = new StringBuilder()
            .AppendLine($"Major: {user.Major}; department: {user.Department}; graduation year: {user.GraduationYear}")
            .AppendLine($"Target role: {e.TargetRole}; experience level (self-reported): {p.ExperienceLevel}")
            .AppendLine($"Industries: {p.PreferredIndustries}; goal: {p.CareerGoal}; languages: {p.Languages}")
            .AppendLine($"Challenges (self-reported): {p.PrimaryStruggle}. {p.CareerChallenges}")
            .AppendLine($"Skills (self-reported): {string.Join(", ", e.Skills.Select(s => $"{s.Name} {s.Level}".Trim()))}")
            .AppendLine($"Skills proven in challenges: {string.Join(", ", e.VerifiedSkills)}");
        foreach (var x in entries) text.AppendLine($"{x.Kind}: {x.Title} {x.Organization} {x.Period} {x.Description}");
        foreach (var x in e.Projects) text.AppendLine($"Portfolio project: {x.Title}. {x.Description} Role: {x.Role}. Tech: {x.Technologies}");

        try
        {
            var (data, meta) = await ai.CareerDnaAsync(
                [new("profile", "Graduate profile", text.ToString()), new("cv", "Candidate CV", e.CvText)], ct);
            var report = new DnaReport(DateTime.UtcNow, meta.ProviderName, meta.Model, data);
            var json = JsonSerializer.Serialize(report);
            await db.GraduateProfiles.Where(x => x.UserId == UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CareerDnaJson, json), ct);
            return Ok(report);
        }
        catch (ChatProviderException ex) { return Unavailable(ex); }
    }

    // Skill gap

    [HttpPost("skill-gap")]
    public async Task<IActionResult> SkillGap(SkillGapRequest request)
    {
        var e = await evidence.GatherAsync(UserId);
        IReadOnlyList<string>? required = null;
        string? target = null;

        if (request.JobId is int jobId)
        {
            var job = await db.JobListings.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
            if (job is null) return NotFound(new { error = "Job not found." });
            var listed = CareerEvidenceService.Csv(job.KeySkillsCsv).ToList();
            required = listed.Count > 0 ? listed : CareerCatalog.DetectSkills(job.Title + " " + job.Description);
            target = $"{job.Title} at {job.CompanyName}";
        }
        else if (!string.IsNullOrWhiteSpace(request.JobDescription))
        {
            required = CareerCatalog.DetectSkills(request.JobDescription);
            target = "the job description you pasted";
        }
        else if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var role = CareerCatalog.RoleSkills.Keys.FirstOrDefault(k => k.Equals(request.Role.Trim(), StringComparison.OrdinalIgnoreCase));
            if (role is null) return BadRequest(new { error = "Unknown role." });
            (required, target) = (CareerCatalog.RoleSkills[role], role);
        }

        if (required is { Count: 0 })
            return UnprocessableEntity(new { error = "No recognisable skills were found in that description." });

        var result = await evidence.SkillGapAsync(e, required, target);
        return Ok(new { result.Target, result.EvidencedPercent, result.Skills, roles = CareerCatalog.RoleSkills.Keys });
    }

    // Skill Proof Lab

    [HttpGet("lab")]
    public async Task<IActionResult> Lab() =>
        Ok(new { skills = SkillLabService.Skills, passScore = SkillLabService.PassScore, attempts = await lab.ListAsync(UserId) });

    [HttpPost("lab")]
    public Task<IActionResult> StartChallenge(LabStartRequest request, CancellationToken ct) =>
        LabAction(() => lab.StartAsync(UserId, request, ct));

    [HttpPost("lab/{id:int}/submit")]
    public Task<IActionResult> SubmitChallenge(int id, LabSubmitRequest request, CancellationToken ct) =>
        LabAction(() => lab.SubmitAsync(UserId, id, request, ct));

    private async Task<IActionResult> LabAction(Func<Task<LabAttemptDto>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ChatProviderException ex) { return Unavailable(ex); }
    }

    // Portfolio

    [HttpGet("portfolio")]
    public async Task<IActionResult> Portfolio() =>
        Ok((await db.PortfolioProjects.AsNoTracking().Where(p => p.UserId == UserId)
            .OrderByDescending(p => p.UpdatedAtUtc).ToListAsync()).Select(MapProject));

    [HttpPost("portfolio")]
    public Task<IActionResult> AddProject(ProjectRequest request) => SaveProject(new PortfolioProject { UserId = UserId }, request);

    [HttpPut("portfolio/{id:int}")]
    public async Task<IActionResult> EditProject(int id, ProjectRequest request) =>
        await db.PortfolioProjects.FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId) is { } project
            ? await SaveProject(project, request)
            : NotFound(new { error = "Project not found." });

    [HttpDelete("portfolio/{id:int}")]
    public async Task<IActionResult> DeleteProject(int id) =>
        await db.PortfolioProjects.Where(p => p.Id == id && p.UserId == UserId).ExecuteDeleteAsync() > 0
            ? NoContent() : NotFound(new { error = "Project not found." });

    [HttpPost("portfolio/{id:int}/improve")]
    public async Task<IActionResult> ImproveProject(int id, CancellationToken ct)
    {
        var p = await db.PortfolioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (p is null) return NotFound(new { error = "Project not found." });
        try
        {
            return Ok(await ai.ImproveProjectAsync(
                $"Title: {p.Title}\nDescription: {p.Description}\nProblem: {p.Problem}\nRole: {p.Role}\nOutcomes: {p.Outcomes}\nTechnologies: {p.Technologies}", ct));
        }
        catch (ChatProviderException ex) { return Unavailable(ex); }
    }

    private async Task<IActionResult> SaveProject(PortfolioProject project, ProjectRequest r)
    {
        static string? Clean(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

        var title = Clean(r.Title, 120);
        var description = Clean(r.Description, 4000);
        if (title is null || description is null) return BadRequest(new { error = "A project needs a title and a description." });
        var github = Clean(r.GitHubUrl, 300);
        var demo = Clean(r.DemoUrl, 300);
        if (github is not null && !PortfolioRules.IsGitHubUrl(github))
            return BadRequest(new { error = "The GitHub link must be a https://github.com/ address." });
        if (demo is not null && !PortfolioRules.IsWebUrl(demo))
            return BadRequest(new { error = "The demo link must start with http:// or https://." });

        project.Title = title;
        project.Description = description;
        project.Problem = Clean(r.Problem, 1000);
        project.Technologies = Clean(r.Technologies, 400);
        project.Role = Clean(r.Role, 600);
        project.Outcomes = Clean(r.Outcomes, 1000);
        project.Skills = Clean(r.Skills, 400);
        project.GitHubUrl = github;
        project.DemoUrl = demo;
        project.UpdatedAtUtc = DateTime.UtcNow;
        if (project.Id == 0) db.PortfolioProjects.Add(project);
        await db.SaveChangesAsync();
        return Ok(MapProject(project));
    }

    private static object MapProject(PortfolioProject p) => new
    {
        p.Id, p.Title, p.Description, p.Problem, p.Technologies, p.Role, p.Outcomes, p.Skills, p.GitHubUrl, p.DemoUrl,
        p.UpdatedAtUtc,
        complete = PortfolioRules.IsValid(p),
        checks = PortfolioRules.Checks(p).Select(c => new { name = c.Check, ok = c.Ok, tip = c.Tip })
    };

    // Reports

    [HttpGet("report")]
    public async Task<IActionResult> Report() =>
        Ok(new { current = await journey.CurrentReadinessAsync(UserId), history = await journey.ReportHistoryAsync(UserId) });

    [HttpPost("report")]
    public async Task<ActionResult<ReadinessSnapshot>> SaveReport() => await journey.SaveReportAsync(UserId);

    private ObjectResult Unavailable(ChatProviderException ex) =>
        Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
}
