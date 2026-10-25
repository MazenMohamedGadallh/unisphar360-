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

/// <summary>
/// Exam Assistant for doctors. Generate exams from selected lecture materials,
/// review/edit, then publish to eligible students with per-student
/// notifications. All endpoints enforce server-side course ownership:
/// foreign courses return 404, students get 403.
/// </summary>
[Authorize(Roles = "Doctor")]
[ApiController]
[Route("api/doctor/exam-assistant")]
public class DoctorExamAssistantController : ControllerBase
{
    private readonly IExamAssistantService _examAssistant;
    private readonly AppDbContext _db;

    public DoctorExamAssistantController(
        IExamAssistantService examAssistant,
        AppDbContext db)
    {
        _examAssistant = examAssistant;
        _db = db;
    }

    private int MeId => User.GetUserId();

    // ------------------------------------------------------------------
    // Courses + materials for the doctor
    // ------------------------------------------------------------------

    /// <summary>Courses assigned to this doctor (dropdown).</summary>
    [HttpGet("courses")]
    public async Task<ActionResult<IReadOnlyList<ExamCatalogCourseDto>>> Courses(CancellationToken ct)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MeId, ct);
        if (me is null || me.IsDeleted) return Unauthorized();
        var assigned = await _db.CourseAssignments.AsNoTracking()
            .Where(a => a.DoctorUserId == MeId)
            .Select(a => a.CourseId)
            .ToListAsync(ct);
        if (assigned.Count == 0) return Ok(new List<ExamCatalogCourseDto>());
        var courses = await _db.Courses.AsNoTracking()
            .Where(c => assigned.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new ExamCatalogCourseDto(
                c.Id, c.Code, c.Title, c.Department,
                c.AcademicYear, c.Semester ?? ""))
            .ToListAsync(ct);
        return Ok(courses);
    }

    /// <summary>Materials of a course (title + content preview) for selection.</summary>
    [HttpGet("courses/{courseId:int}/materials")]
    public async Task<ActionResult<IReadOnlyList<MaterialDto>>> Materials(int courseId, CancellationToken ct)
    {
        var assigned = await _db.CourseAssignments.AsNoTracking()
            .AnyAsync(a => a.DoctorUserId == MeId && a.CourseId == courseId, ct)
            || await _db.CourseInstructors.AsNoTracking()
                .AnyAsync(ci => ci.DoctorUserId == MeId && ci.CourseId == courseId, ct);
        if (!assigned) return NotFound();

        var materials = await _db.CourseMaterials.AsNoTracking()
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new MaterialDto(
                m.Id, m.CourseId, m.Course == null ? "" : m.Course.Title, m.Title, m.Type,
                m.Description, m.Content, m.FileUrl, m.CreatedAtUtc, ""))
            .ToListAsync(ct);
        return Ok(materials);
    }

    // ------------------------------------------------------------------
    // Generate
    // ------------------------------------------------------------------

    /// <summary>Generate exam questions from selected materials.</summary>
    [HttpPost("generate")]
    [EnableRateLimiting("exam-generator")]
    public async Task<ActionResult<ExamGenerationResponse>> Generate(
        [FromBody] ExamAssistantGenerationRequest req,
        [FromServices] FluentValidation.IValidator<ExamAssistantGenerationRequest> validator,
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
            var result = await _examAssistant.GenerateFromMaterialsAsync(MeId, req, ct);
            return Ok(result);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (AiInvalidResponseException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    // ------------------------------------------------------------------
    // Exams (doctor CRUD)
    // ------------------------------------------------------------------

    /// <summary>Save a draft exam with its questions.</summary>
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
            var exam = await _examAssistant.CreateDraftExamAsync(MeId, req, ct);
            return CreatedAtAction(nameof(GetExam), new { examId = exam.Id }, exam);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    /// <summary>List the doctor's exams (drafts and published).</summary>
    [HttpGet("exams")]
    public async Task<ActionResult<IReadOnlyList<ExamListDto>>> ListExams(CancellationToken ct)
    {
        var exams = await _examAssistant.ListExamsAsync(MeId, ct);
        return Ok(exams);
    }

    /// <summary>Get one exam with its questions.</summary>
    [HttpGet("exams/{examId:int}")]
    public async Task<ActionResult<ExamDto>> GetExam(int examId, CancellationToken ct)
    {
        var exam = await _examAssistant.GetExamAsync(MeId, examId, ct);
        if (exam is null) return NotFound();
        return Ok(exam);
    }

    /// <summary>Update a draft exam (title, questions).</summary>
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
            var exam = await _examAssistant.UpdateExamAsync(MeId, examId, req, ct);
            if (exam is null) return NotFound();
            return Ok(exam);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    /// <summary>Delete a draft exam.</summary>
    [HttpDelete("exams/{examId:int}")]
    public async Task<IActionResult> DeleteExam(int examId, CancellationToken ct)
    {
        var deleted = await _examAssistant.DeleteExamAsync(MeId, examId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    /// <summary>Publish a draft exam and notify eligible students.</summary>
    [HttpPost("exams/{examId:int}/publish")]
    [EnableRateLimiting("exam-generator")]
    public async Task<ActionResult<ExamDto>> PublishExam(
        int examId,
        [FromBody] ExamAssistantPublishRequest req,
        CancellationToken ct)
    {
        var publishReq = new ExamAssistantPublishRequest(examId, req.Title, req.Marks);
        try
        {
            var exam = await _examAssistant.PublishExamAsync(MeId, publishReq, ct);
            return Ok(exam);
        }
        catch (AiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }
}

/// <summary>
/// Student-facing exam endpoints. Visibility is evaluated at query time:
/// only published exams for currently-enrolled courses at the student's
/// university. Correct answers are never returned.
/// </summary>
[Authorize(Roles = "Student")]
[ApiController]
[Route("api/student/exams")]
public class StudentExamsController : ControllerBase
{
    private readonly IExamAssistantService _examAssistant;

    public StudentExamsController(IExamAssistantService examAssistant)
    {
        _examAssistant = examAssistant;
    }

    private int MeId => User.GetUserId();

    /// <summary>List published exams visible to this student.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudentExamDto>>> List(CancellationToken ct)
    {
        var exams = await _examAssistant.ListStudentExamsAsync(MeId, ct);
        return Ok(exams);
    }

    /// <summary>Get one published exam (questions only, no correct answers).</summary>
    [HttpGet("{examId:int}")]
    public async Task<ActionResult<StudentExamDto>> Get(int examId, CancellationToken ct)
    {
        var exam = await _examAssistant.GetStudentExamAsync(MeId, examId, ct);
        if (exam is null) return NotFound();
        return Ok(exam);
    }
}

/// <summary>
/// User notification settings. Each user can set On / MuteUntil / Off.
/// Applied server-side when publishing exams.
/// </summary>
[Authorize]
[ApiController]
[Route("api/user/notification-settings")]
public class UserNotificationSettingsController : ControllerBase
{
    private readonly IExamAssistantService _examAssistant;

    public UserNotificationSettingsController(IExamAssistantService examAssistant)
    {
        _examAssistant = examAssistant;
    }

    private int MeId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<UserNotificationSettingDto>> Get(CancellationToken ct)
    {
        var setting = await _examAssistant.GetNotificationSettingAsync(MeId, ct);
        return Ok(setting);
    }

    [HttpPut]
    public async Task<ActionResult<UserNotificationSettingDto>> Put(
        [FromBody] UpdateUserNotificationSettingRequest req,
        CancellationToken ct)
    {
        var setting = await _examAssistant.UpdateNotificationSettingAsync(MeId, req, ct);
        return Ok(setting);
    }
}
