using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Student")]
[ApiController]
[Route("api/student/ai")]
public class StudentAiController(AppDbContext db, GraduateAiService ai) : ControllerBase
{
    private int MeId => User.GetUserId();

    [HttpGet("courses")]
    public async Task<ActionResult<IReadOnlyList<DoctorCourseSummaryDto>>> Courses(CancellationToken ct)
    {
        var courseIds = await db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == MeId)
            .Select(e => e.CourseId)
            .Distinct().ToListAsync(ct);

        var courses = await db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new DoctorCourseSummaryDto(c.Id, c.Code, c.Title, c.Department, c.Credits,
                c.Semester ?? "", 0, null, null))
            .ToListAsync(ct);
        return Ok(courses);
    }

    [HttpGet("courses/{courseId:int}/materials")]
    public async Task<ActionResult<IReadOnlyList<DoctorMaterialDto>>> Materials(int courseId, CancellationToken ct)
    {
        var enrolled = await db.Enrollments.AsNoTracking().AnyAsync(e => e.UserId == MeId && e.CourseId == courseId, ct);
        if (!enrolled) return NotFound();

        var materials = await db.CourseMaterials.AsNoTracking()
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.Title)
            .Select(m => new DoctorMaterialDto(m.Id, m.CourseId, m.Title, m.Type, m.Description ?? "",
                m.Content ?? "", m.FileUrl, m.DeadlineUtc, m.CreatedAtUtc,
                m.StoredFileName != null, m.OriginalFileName, m.FileSizeBytes))
            .ToListAsync(ct);
        return Ok(materials);
    }

    [HttpPost("quiz")]
    public async Task<ActionResult<QuizGenerationResponse>> Quiz([FromBody] QuizGenerationRequest req, CancellationToken ct)
    {
        if (req.CourseId <= 0) return BadRequest(new { message = "Select a course." });
        var enrolled = await db.Enrollments.AsNoTracking().AnyAsync(e => e.UserId == MeId && e.CourseId == req.CourseId, ct);
        if (!enrolled) return NotFound();

        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CourseId, ct);
        if (course is null) return NotFound();

        var materialQuery = db.CourseMaterials.AsNoTracking().Where(m => m.CourseId == req.CourseId);
        if (req.MaterialId.HasValue) materialQuery = materialQuery.Where(m => m.Id == req.MaterialId.Value);
        var material = await materialQuery.OrderBy(m => m.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (material is null) return BadRequest(new { message = "This course has no available lecture material yet." });

        var content = !string.IsNullOrWhiteSpace(material.Content) ? material.Content : material.Description ?? "";
        if (content.Length < 80) return BadRequest(new { message = "The selected material does not contain enough text to generate a grounded quiz." });

        try
        {
            var (quiz, meta) = await ai.GeneratePracticeQuizAsync(course.Title, material.Title, content,
                Math.Clamp(req.QuestionCount, 1, 15), ct, MeId);
            var questions = quiz.Questions.Select(q => new GeneratedQuestionDto(q.Question,
                q.Options, q.CorrectIndex, q.Explanation,
                string.IsNullOrWhiteSpace(q.Reference) ? material.Title : q.Reference)).ToArray();
            return Ok(new QuizGenerationResponse(meta.ProviderName, meta.Model, questions, IsDraft: true));
        }
        catch (ChatProviderException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (OperationCanceledException) { throw; }
    }
}
