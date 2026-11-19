using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>
/// Doctor AI teaching assistant. Provider availability is honest: when no AI
/// provider is configured the endpoints return 503 with a clear message
/// (never a fake success). Course content is always authorized server-side.
/// </summary>
[Authorize(Roles = "Doctor")]
[ApiController]
[Route("api/doctor/ai")]
public class DoctorAiController : ControllerBase
{
    private readonly IDoctorAiService _ai;
    private readonly IQuizPublishService _quizPublish;

    public DoctorAiController(IDoctorAiService ai, IQuizPublishService quizPublish)
    {
        _ai = ai;
        _quizPublish = quizPublish;
    }

    private int MeId => User.GetUserId();

    [HttpGet("status")]
    public async Task<ActionResult<object>> Status(CancellationToken ct)
    {
        var providers = await _ai.GetProvidersAsync(ct);
        return Ok(new
        {
            Available = providers.Count > 0,
            Providers = providers,
            Message = providers.Count > 0
                ? "AI assistant is available."
                : AiSetupHint.Message
        });
    }

    [HttpPost("quiz")]
    public async Task<ActionResult<QuizGenerationResponse>> Quiz(
        [FromBody] QuizGenerationRequest req,
        [FromServices] FluentValidation.IValidator<QuizGenerationRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        return await RunAsync(() => _ai.GenerateQuizAsync(MeId, req, ct));
    }

    /// <summary>Publish the generated quiz to eligible enrolled students by
    /// reusing the exam infrastructure (draft + publish + notify, one
    /// transaction). Course existence, doctor assignment and material
    /// ownership are recomputed server-side; the draft rolls back if the
    /// publication step fails.</summary>
    [HttpPost("quiz/publish")]
    public async Task<ActionResult<QuizPublishResponse>> PublishQuiz(
        [FromBody] QuizPublishRequest req,
        [FromServices] FluentValidation.IValidator<QuizPublishRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var outcome = await _quizPublish.PublishAsync(MeId, req, ct);
        if (outcome.IsConflict)
            return StatusCode(StatusCodes.Status409Conflict, new { message = outcome.Error });
        if (!outcome.Ok)
            return BadRequest(new { message = outcome.Error });
        return Ok(outcome.Result);
    }

    [HttpPost("summarize")]
    public async Task<ActionResult<LectureSummaryDto>> Summarize(
        [FromBody] SummarizeRequest req,
        [FromServices] FluentValidation.IValidator<SummarizeRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        return await RunAsync(() => _ai.SummarizeAsync(MeId, req, ct));
    }

    [HttpPost("ask")]
    public async Task<ActionResult<LectureAnswerDto>> Ask(
        [FromBody] LectureQuestionRequest req,
        [FromServices] FluentValidation.IValidator<LectureQuestionRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        return await RunAsync(() => _ai.AnswerLectureQuestionAsync(MeId, req, ct));
    }

    private static async Task<ActionResult<T>> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return new OkObjectResult(await action());
        }
        catch (AiUnavailableException ex)
        {
            return new ObjectResult(new { message = ex.Message })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
        catch (AiInvalidResponseException ex)
        {
            return new ObjectResult(new { message = ex.Message })
            { StatusCode = StatusCodes.Status502BadGateway };
        }
    }
}
