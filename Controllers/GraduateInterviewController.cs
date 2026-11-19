using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[ApiController]
[Authorize(Roles = "Graduate")]
[Route("api/grad/interview")]
public class GraduateInterviewController(
    InterviewService interviews,
    IValidator<InterviewSessionCreateRequest> sessionValidator,
    IValidator<InterviewAnswerRequest> answerValidator) : ControllerBase
{
    private int UserId => User.GetUserId();

    /// <summary>Quiz start: real AI questions when a provider is configured,
    /// otherwise the curated bank is used and clearly labeled ("Curated").
    /// Pass useAi=false to force the curated bank, or useAi=true to fail
    /// loudly (503) when no provider is configured.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession(InterviewSessionCreateRequest request)
    {
        var validation = await sessionValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        try
        {
            return Ok(await interviews.CreateSessionAsync(UserId, request, HttpContext.RequestAborted));
        }
        catch (ChatProviderException ex)
        {
            return Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
        }
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<InterviewSessionDto>>> List() =>
        Ok(await interviews.ListSessionsAsync(UserId));

    [HttpGet("sessions/{id:int}")]
    public async Task<ActionResult<InterviewSessionDto>> Get(int id) =>
        await interviews.GetSessionAsync(UserId, id)
        is { } session ? session : NotFound(new { error = "Interview session not found." });

    [HttpDelete("sessions/{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await interviews.DeleteSessionAsync(UserId, id) ? NoContent() : NotFound(new { error = "Interview session not found." });

    /// <summary>Submit an answer; REAL provider-generated feedback is attached
    /// (never faked) or a 503 explains exactly why feedback is unavailable.</summary>
    [HttpPost("sessions/{sessionId:int}/questions/{questionId:int}/answer")]
    public async Task<IActionResult> Answer(int sessionId, int questionId, InterviewAnswerRequest request)
    {
        var validation = await answerValidator.ValidateAsync(request);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        try
        {
            return Ok(await interviews.AnswerAsync(UserId, sessionId, questionId, request.AnswerText, HttpContext.RequestAborted));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ChatProviderException ex)
        {
            return Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
        }
    }
}
