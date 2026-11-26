using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Student")]
[ApiController]
[Route("api/student/exams")]
public class StudentExamAttemptsController : ControllerBase
{
    private readonly IExamAttemptsService _attempts;

    public StudentExamAttemptsController(IExamAttemptsService attempts)
    {
        _attempts = attempts;
    }

    private int MeId => User.GetUserId();

    [HttpGet("attempts")]
    public async Task<ActionResult<IReadOnlyList<StudentAttemptListRowDto>>> MyAttempts(
        CancellationToken ct)
        => Ok(await _attempts.ListMyAttemptsAsync(MeId, ct));

    [HttpGet("{examId:int}/attempt")]
    public async Task<ActionResult<StudentExamAttemptStateDto>> State(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.GetAttemptStateAsync(MeId, examId, ct);
        return At(result);
    }

    [HttpPost("{examId:int}/start")]
    public async Task<ActionResult<StudentExamAttemptStateDto>> Start(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.StartAttemptAsync(MeId, examId, ct);
        return At(result);
    }

    [HttpPost("{examId:int}/submit")]
    public async Task<ActionResult<StudentExamAttemptStateDto>> Submit(
        int examId, [FromBody] SubmitExamAttemptRequest? req, CancellationToken ct)
    {
        var result = await _attempts.SubmitAttemptAsync(
            MeId, examId, req ?? new SubmitExamAttemptRequest(Array.Empty<SubmitExamAnswerDto>()), ct);
        return At(result);
    }

    private ActionResult<StudentExamAttemptStateDto> At(
        ExamAttemptResult<StudentExamAttemptStateDto> result)
    {
        if (result.Ok && result.Value is not null)
            return Ok(result.Value);

        return result.Failure switch
        {
            ExamAttemptFailure.NotFound => NotFound(),
            ExamAttemptFailure.AlreadySubmitted or
            ExamAttemptFailure.NotOpenYet or
            ExamAttemptFailure.Closed or
            ExamAttemptFailure.NotStarted => StatusCode(
                StatusCodes.Status409Conflict, new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }
}

[Authorize(Roles = "Doctor")]
[ApiController]
[Route("api/doctor/exam-assistant/exams")]
public class DoctorExamResultsDetailsController : ControllerBase
{
    private readonly IExamAttemptsService _attempts;

    public DoctorExamResultsDetailsController(IExamAttemptsService attempts)
    {
        _attempts = attempts;
    }

    private int MeId => User.GetUserId();

    [HttpGet("{examId:int}/results")]
    public async Task<ActionResult<DoctorExamResultsDto>> Results(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.GetDoctorResultsAsync(MeId, examId, ct);
        if (result.Ok && result.Value is not null)
            return Ok(result.Value);
        return NotFound();
    }

    [HttpGet("{examId:int}/written-answers")]
    public async Task<ActionResult<IReadOnlyList<DoctorWrittenAnswerDto>>> WrittenAnswers(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.ListWrittenAnswersAsync(MeId, examId, ct);
        return result.Ok && result.Value is not null ? Ok(result.Value) : NotFound();
    }

    [HttpPost("{examId:int}/written-answers/{answerId:int}/grade")]
    public async Task<ActionResult<DoctorWrittenAnswerDto>> GradeWrittenAnswer(
        int examId, int answerId, [FromBody] GradeWrittenAnswerRequest req, CancellationToken ct)
    {
        var result = await _attempts.GradeWrittenAnswerAsync(MeId, examId, answerId, req.IsCorrect, ct);
        return result.Ok && result.Value is not null ? Ok(result.Value) : NotFound();
    }
}
