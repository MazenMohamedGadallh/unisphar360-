using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Doctor")]
[ApiController]
[Route("api/doctor/exam-generator")]
public class DoctorExamGeneratorController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IExamGeneratorService _examService;
    private readonly DoctorAccessService _access;

    public DoctorExamGeneratorController(
        AppDbContext db,
        IExamGeneratorService examService,
        DoctorAccessService access)
    {
        _db = db;
        _examService = examService;
        _access = access;
    }

    private int MeId => User.GetUserId();

    // Step 1: course picklist for the doctor (dropdown)

    [HttpGet("courses")]
    public async Task<ActionResult<IReadOnlyList<DoctorCourseDto>>> Courses(CancellationToken ct)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();
        return Ok(await _examService.GetAssignedCoursesAsync(doctorUserId: MeId, ct));
    }

    // Step 2: generate questions from materials (draft, not saved)

    [HttpPost("generate")]
    [EnableRateLimiting("exam-generator")]
    public async Task<ActionResult<ExamGenerationResponse>> Generate(
        [FromBody] ExamGenerationRequest req,
        [FromServices] FluentValidation.IValidator<ExamGenerationRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        try
        {
            var result = await _examService.GenerateAsync(MeId, req, ct);
            return Ok(result);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
        catch (AiInvalidResponseException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = ex.Message });
        }
    }

    // Step 3: save / list / get / update / delete exams

    [HttpPost("exams")]
    [EnableRateLimiting("exam-generator")]
    public async Task<ActionResult<ExamDto>> SaveExam(
        [FromBody] UpdateExamRequest req,
        [FromServices] FluentValidation.IValidator<UpdateExamRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        try
        {
            var exam = await _examService.SaveExamAsync(MeId, req, ct);
            return CreatedAtAction(nameof(GetExam), new { examId = exam.Id }, exam);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
    }

    [HttpGet("exams")]
    public async Task<ActionResult<IReadOnlyList<ExamListDto>>> ListExams(CancellationToken ct)
    {
        var exams = await _examService.ListExamsAsync(MeId, ct);
        return Ok(exams);
    }

    [HttpGet("exams/{examId:int}")]
    public async Task<ActionResult<ExamDto>> GetExam(int examId, CancellationToken ct)
    {
        var exam = await _examService.GetExamAsync(MeId, examId, ct);
        if (exam is null) return NotFound();
        return Ok(exam);
    }

    [HttpPut("exams/{examId:int}")]
    [EnableRateLimiting("exam-generator")]
    public async Task<ActionResult<ExamDto>> UpdateExam(
        int examId,
        [FromBody] UpdateExamRequest req,
        [FromServices] FluentValidation.IValidator<UpdateExamRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        try
        {
            var exam = await _examService.UpdateExamAsync(MeId, examId, req, ct);
            if (exam is null) return NotFound();
            return Ok(exam);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
    }

    [HttpDelete("exams/{examId:int}")]
    public async Task<IActionResult> DeleteExam(int examId, CancellationToken ct)
    {
        var deleted = await _examService.DeleteExamAsync(MeId, examId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPut("exams/{examId:int}/questions/{questionId:int}")]
    public async Task<ActionResult<ExamQuestionDto>> UpdateQuestion(
        int examId, int questionId,
        [FromBody] UpdateExamQuestionRequest req,
        [FromServices] FluentValidation.IValidator<UpdateExamQuestionRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        var question = await _examService.UpdateQuestionAsync(
            MeId, examId, questionId, req, ct);
        if (question is null) return NotFound();
        return Ok(question);
    }

    [HttpDelete("exams/{examId:int}/questions/{questionId:int}")]
    public async Task<IActionResult> DeleteQuestion(
        int examId, int questionId, CancellationToken ct)
    {
        var deleted = await _examService.DeleteQuestionAsync(
            MeId, examId, questionId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }
}