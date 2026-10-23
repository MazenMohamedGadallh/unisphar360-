using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/profile")]
public class GraduateProfileController(
    GraduateProfileService profiles,
    CvFileService cvs,
    GraduateAiService ai,
    AppDbContext db,
    IValidator<GraduateProfileUpdateRequest> profileValidator,
    IValidator<GraduateSkillCreateRequest> skillValidator,
    IValidator<ProfileEntryCreateRequest> entryValidator) : ControllerBase
{
    private int UserId => User.GetUserId();

    /// <summary>Current graduate's career profile (lazily created).</summary>
    [HttpGet]
    public async Task<ActionResult<GraduateProfileDto>> Get() => await profiles.GetProfileAsync(UserId);

    /// <summary>Update the career profile fields.</summary>
    [HttpPut]
    public async Task<ActionResult<GraduateProfileDto>> Update(GraduateProfileUpdateRequest request)
    {
        var validation = await profileValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await profiles.UpdateAsync(UserId, request);
    }

    /// <summary>Deterministic profile completion audit (weights are documented).</summary>
    [HttpGet("completion")]
    public async Task<ActionResult<ProfileCompletionDto>> Completion() =>
        await profiles.GetCompletionAsync(UserId);

    // ---- Skills ----

    /// <summary>Add or update a skill (same name, case-insensitive).</summary>
    [HttpPost("skills")]
    public async Task<ActionResult<GraduateSkillDto>> AddSkill(GraduateSkillCreateRequest request)
    {
        var validation = await skillValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await profiles.AddSkillAsync(UserId, request));
    }

    [HttpDelete("skills/{id:int}")]
    public async Task<IActionResult> RemoveSkill(int id) =>
        await profiles.RemoveSkillAsync(UserId, id) ? NoContent() : NotFound();

    // ---- Profile entries ----

    [HttpPost("entries")]
    public async Task<ActionResult<ProfileEntryDto>> AddEntry(ProfileEntryCreateRequest request)
    {
        var validation = await entryValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await profiles.AddEntryAsync(UserId, request));
    }

    [HttpPut("entries/{id:int}")]
    public async Task<IActionResult> UpdateEntry(int id, ProfileEntryCreateRequest request)
    {
        var validation = await entryValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await profiles.UpdateEntryAsync(UserId, id, request) ? NoContent() : NotFound();
    }

    [HttpDelete("entries/{id:int}")]
    public async Task<IActionResult> RemoveEntry(int id) =>
        await profiles.RemoveEntryAsync(UserId, id) ? NoContent() : NotFound();

    // ---- CV management ----

    /// <summary>Upload a CV (pdf/docx/txt, max 10MB). Extraction is best-effort per format.</summary>
    [HttpPost("cv")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadCv(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Please attach a CV file." });
        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await cvs.UploadAsync(UserId, file.FileName ?? "cv", file.ContentType, stream));
        }
        catch (CvUploadException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("cv")]
    public async Task<ActionResult<IReadOnlyList<CvDocumentDto>>> ListCv() => Ok(await cvs.ListAsync(UserId));

    [HttpDelete("cv/{id:int}")]
    public async Task<IActionResult> DeleteCv(int id) =>
        await cvs.DeleteAsync(UserId, id) ? NoContent() : NotFound();

    /// <summary>Store manually typed CV text (fallback when extraction failed).</summary>
    [HttpPut("cv/{id:int}/manual-text")]
    public async Task<IActionResult> SaveCvManualText(int id, [FromBody] CvManualTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest(new { error = "Text cannot be empty." });
        try
        {
            return Ok(await cvs.SaveManualTextAsync(UserId, id, request.Text));
        }
        catch (CvUploadException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>Run a REAL AI analysis (requires a configured provider; never faked).</summary>
    [HttpPost("cv/{id:int}/analyze")]
    public async Task<IActionResult> AnalyzeCv(int id, [FromBody] CvAnalyzeRequest? request)
    {
        var cv = await cvs.GetAsync(UserId, id);
        if (cv is null) return NotFound(new { error = "CV not found for this account." });
        var text = await cvs.GetEffectiveTextAsync(cv);
        if (string.IsNullOrWhiteSpace(text))
            return UnprocessableEntity(new
            {
                error = "No readable text for this CV yet. Upload a text-based file or enter the CV text manually before analyzing."
            });

        try
        {
            var (data, meta) = await ai.AnalyzeCvAsync(UserId, text, request?.TargetRole, HttpContext.RequestAborted);

            var analysis = new CvAnalysis
            {
                CvDocumentId = cv.Id,
                OverallAssessment = data.OverallAssessment,
                ScorePercent = data.ScorePercent,
                StrengthsJson = JsonSerializer.Serialize(data.Strengths),
                WeaknessesJson = JsonSerializer.Serialize(data.Weaknesses),
                SuggestionsJson = JsonSerializer.Serialize(data.Suggestions),
                RewritesJson = JsonSerializer.Serialize(data.Rewrites),
                KeywordsJson = JsonSerializer.Serialize(data.Keywords),
                ProviderName = meta.ProviderName,
                Model = meta.Model,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.CvAnalyses.Add(analysis);
            await db.SaveChangesAsync();

            return Ok(new CvAnalysisDto(
                analysis.Id, analysis.CvDocumentId, analysis.OverallAssessment, analysis.ScorePercent,
                data.Strengths, data.Weaknesses, data.Suggestions,
                [.. data.Rewrites.Select(r => new KeywordRewrite(r.Original, r.Rewritten, r.Why ?? ""))],
                data.Keywords, meta.ProviderName, meta.Model, analysis.CreatedAtUtc));
        }
        catch (ChatProviderException ex)
        {
            return Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
        }
    }

    [HttpGet("cv/{id:int}/analyses")]
    public async Task<IActionResult> GetCvAnalyses(int id)
    {
        var cv = await cvs.GetAsync(UserId, id);
        if (cv is null) return NotFound(new { error = "CV not found for this account." });

        var analyses = await db.CvAnalyses.AsNoTracking()
            .Where(a => a.CvDocumentId == id)
            .OrderByDescending(a => a.CreatedAtUtc).ToListAsync();
        return Ok(analyses.Select(MapAnalysis).ToList());
    }

    internal static CvAnalysisDto MapAnalysis(CvAnalysis a) =>
        new(a.Id, a.CvDocumentId, a.OverallAssessment, a.ScorePercent,
            DeserializeList(a.StrengthsJson), DeserializeList(a.WeaknessesJson), DeserializeList(a.SuggestionsJson),
            [.. JsonSerializer.Deserialize<List<KeywordRewrite>>(a.RewritesJson, JsonOpts) ?? []],
            DeserializeList(a.KeywordsJson), a.ProviderName, a.Model, a.CreatedAtUtc);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    internal static List<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
}

public record CvManualTextRequest(string Text);
