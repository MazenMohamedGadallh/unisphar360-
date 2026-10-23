using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/applications")]
public class GraduateApplicationsController(
    JobApplicationService applications,
    IValidator<ApplicationCreateRequest> createValidator,
    IValidator<ApplicationUpdateRequest> updateValidator) : ControllerBase
{
    private int UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationDto>>> List([FromQuery] string? status) =>
        Ok(await applications.ListAsync(UserId, status));

    [HttpGet("summary")]
    public async Task<ActionResult<ApplicationSummaryDto>> Summary() =>
        await applications.GetSummaryAsync(UserId);

    /// <summary>Record an application the graduate actually submitted
    /// (optionally linked to a catalog listing). No employer submission is implied.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(ApplicationCreateRequest request)
    {
        var validation = await createValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        try
        {
            var app = await applications.CreateAsync(UserId, request);
            return Ok(app);
        }
        catch (KeyNotFoundException) { return NotFound(new { error = "The linked job listing does not exist." }); }
        catch (MissingFieldException ex)
        {
            return BadRequest(new { error = $"Field '{ex.Message}' is required." });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ApplicationUpdateRequest request)
    {
        var validation = await updateValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await applications.UpdateAsync(UserId, id, request)
            ? NoContent()
            : NotFound(new { error = "Application not found." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await applications.DeleteAsync(UserId, id)
            ? NoContent()
            : NotFound(new { error = "Application not found." });
}
