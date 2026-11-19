using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>
/// Student attempt endpoints for published exams. Route segment
/// "attempts" / "{id}/start|submit|attempt" — separate templates from the
/// StudentExamsController list/detail routes. Attempt creation, window
/// enforcement and grading all happen server-side.
/// </summary>
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

    /// <summary>This student's own attempts (score + submission date), for
    /// the My Exams page chips. Own rows only.</summary>
    [HttpGet("attempts")]
    public async Task<ActionResult<IReadOnlyList<StudentAttemptListRowDto>>> MyAttempts(
        CancellationToken ct)
        => Ok(await _attempts.ListMyAttemptsAsync(MeId, ct));

    /// <summary>Get (never create) the attempt state of one exam for the
    /// current student: window info, attempt summary, take-able questions or
    /// the post-submit review per the exam's show-answers flag.</summary>
    [HttpGet("{examId:int}/attempt")]
    public async Task<ActionResult<StudentExamAttemptStateDto>> State(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.GetAttemptStateAsync(MeId, examId, ct);
        return At(result);
    }

    /// <summary>Start (or resume) the single attempt for this exam.
    /// 404 when the exam is not visible; 409 before the exam opens or when
    /// it was already submitted.</summary>
    [HttpPost("{examId:int}/start")]
    public async Task<ActionResult<StudentExamAttemptStateDto>> Start(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.StartAttemptAsync(MeId, examId, ct);
        return At(result);
    }

    /// <summary>Submit the answer set for the active attempt. The server
    /// grades: it derives the correct option per question itself, enforces
    /// the close-at deadline itself and ignores any client claim of a score.
    /// 409 on resubmit (one attempt per student per exam) or after the exam
    /// closed.</summary>
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

/// <summary>
/// Doctor results for their own published exams: who took it (name, score
/// out of max, percentage, submitted date) and who did not. The exam must
/// belong to the calling doctor — any other id reads as 404; students never
/// pass the Doctor role gate (403).
/// </summary>
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

    /// <summary>Results of one of the doctor's own exams: submitted attempts
    /// with server-graded scores plus the enrolled non-takers.</summary>
    [HttpGet("{examId:int}/results")]
    public async Task<ActionResult<DoctorExamResultsDto>> Results(
        int examId, CancellationToken ct)
    {
        var result = await _attempts.GetDoctorResultsAsync(MeId, examId, ct);
        if (result.Ok && result.Value is not null)
            return Ok(result.Value);
        return NotFound();
    }
}
