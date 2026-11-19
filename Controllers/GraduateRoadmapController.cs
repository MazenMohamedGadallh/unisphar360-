using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/roadmap")]
public class GraduateRoadmapController(
    RoadmapService roadmap,
    IValidator<RoadmapItemCreateRequest> createValidator,
    IValidator<RoadmapItemUpdateRequest> updateValidator) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoadmapItemDto>>> List() => Ok(await roadmap.ListAsync(UserId));

    [HttpGet("progress")]
    public async Task<ActionResult<RoadmapProgressDto>> Progress() => await roadmap.GetProgressAsync(UserId);

    [HttpPost]
    public async Task<IActionResult> Create(RoadmapItemCreateRequest request)
    {
        var validation = await createValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        try
        {
            return Ok(await roadmap.CreateAsync(UserId, request));
        }
        catch (MissingFieldException)
        {
            return BadRequest(new { error = "Field 'title' is required." });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, RoadmapItemUpdateRequest request)
    {
        var validation = await updateValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await roadmap.UpdateAsync(UserId, id, request)
            ? NoContent()
            : NotFound(new { error = "Roadmap item not found." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await roadmap.DeleteAsync(UserId, id) ? NoContent() : NotFound(new { error = "Roadmap item not found." });

    /// <summary>AI-based roadmap suggestions (requires a configured provider).</summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest(CancellationToken ct)
    {
        try
        {
            var (suggestions, _) = await roadmap.SuggestAsync(UserId, ct);
            return Ok(new { suggestions, source = "Ai" });
        }
        catch (ChatProviderException ex)
        {
            return Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
        }
    }

    /// <summary>Apply AI suggestions as real roadmap items.</summary>
    [HttpPost("apply-suggestions")]
    public async Task<IActionResult> Apply([FromBody] List<RoadmapSuggestionDto> picked) =>
        Ok(await roadmap.ApplySuggestionsAsync(UserId, picked ?? []));
}
