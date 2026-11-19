using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Student")]
[ApiController]
[Route("api/study-planner")]
public class StudyPlannerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AcademicService _academic;

    public StudyPlannerController(AppDbContext db, AcademicService academic)
    {
        _db = db;
        _academic = academic;
    }

    /// <summary>The week's plan. Optional ?weekStart=2026-10-04 (any date inside the wanted week).</summary>
    [HttpGet]
    public async Task<StudyPlannerResponse> Get([FromQuery] DateTime? weekStart = null) =>
        await _academic.GetPlannerAsync(User.GetUserId(), weekStart);

    [HttpPost]
    public async Task<ActionResult<StudyDayDto>> Create(CreateStudyItemRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        if (req.Hours < 0 || req.Hours > 12) return BadRequest("Hours must be between 0 and 12.");

        var item = new StudyPlanItem
        {
            UserId = User.GetUserId(),
            Date = req.Date.Date,
            Title = req.Title.Trim(),
            Hours = req.Hours,
            Note = req.Note
        };
        _db.StudyPlanItems.Add(item);
        await _db.SaveChangesAsync();
        return Ok(AcademicService.ToDto(item));
    }

    /// <summary>Tick / untick a study item (the circle in the planner).</summary>
    [HttpPost("{id:int}/toggle")]
    public async Task<ActionResult<StudyDayDto>> Toggle(int id)
    {
        var item = await _db.StudyPlanItems.FindAsync(id);
        if (item is null || item.UserId != User.GetUserId()) return NotFound();
        item.IsDone = !item.IsDone;
        await _db.SaveChangesAsync();
        return AcademicService.ToDto(item);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.StudyPlanItems.FindAsync(id);
        if (item is null || item.UserId != User.GetUserId()) return NotFound();
        _db.StudyPlanItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
