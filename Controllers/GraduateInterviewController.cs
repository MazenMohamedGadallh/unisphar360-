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

    // Recovery drill

    [HttpPost("/api/graduate/interview/recovery-drill")]
    public Task<IActionResult> StartDrill(RecoveryDrillRequest request, CancellationToken ct) =>
        DrillAsync(() => interviews.StartDrillAsync(UserId, request.SessionId, ct));

    [HttpPost("/api/graduate/interview/recovery-drill/answer")]
    public async Task<IActionResult> AnswerDrill(DrillAnswerRequest request, CancellationToken ct)
    {
        var length = request.AnswerText?.Trim().Length ?? 0;
        if (length is < 10 or > 10000)
            return BadRequest(new { error = "Write an answer between 10 and 10000 characters." });
        return await DrillAsync(() => interviews.AnswerDrillAsync(UserId, request, ct));
    }

    private async Task<IActionResult> DrillAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ChatProviderException ex)
        {
            return Problem(title: "AI provider required", detail: ex.Message, statusCode: 503);
        }
    }

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
