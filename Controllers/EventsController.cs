using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUniversityScope _scope;

    public EventsController(AppDbContext db, IUniversityScope scope)
    {
        _db = db;
        _scope = scope;
    }

    /// <summary>List events visible to the caller's university. Use ?upcoming=true to hide past events.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<EventDto>> GetAll([FromQuery] bool upcoming = false)
    {
        var scope = await _scope.GetAsync();
        var query = _db.Events.AsNoTracking().VisibleToCurrentUser(scope);
        if (upcoming) query = query.Where(e => e.StartsAtUtc >= DateTime.UtcNow);

        return await query
            .OrderBy(e => e.StartsAtUtc)
            .Select(e => new EventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.ImageUrl, e.Description))
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EventDto>> GetById(int id)
    {
        var scope = await _scope.GetAsync();
        var e = await _db.Events.AsNoTracking()
            .VisibleToCurrentUser(scope)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return new EventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.ImageUrl, e.Description);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Location))
            return BadRequest("Title and Location are required.");
        if (!req.UniversityId.HasValue)
            return BadRequest("UniversityId is required.");
        if (!await _db.Universities.AnyAsync(u => u.Id == req.UniversityId.Value))
            return BadRequest("University does not exist.");

        var e = new EventItem
        {
            Title = req.Title,
            Location = req.Location,
            StartsAtUtc = req.StartsAtUtc,
            ImageUrl = req.ImageUrl,
            Description = req.Description,
            UniversityId = req.UniversityId.Value
        };
        _db.Events.Add(e);
        await _db.SaveChangesAsync();

        var dto = new EventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.ImageUrl, e.Description);
        return CreatedAtAction(nameof(GetById), new { id = e.Id }, dto);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<EventDto>> Update(int id, CreateEventRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Location))
            return BadRequest("Title and Location are required.");
        if (!req.UniversityId.HasValue)
            return BadRequest("UniversityId is required.");
        if (!await _db.Universities.AnyAsync(u => u.Id == req.UniversityId.Value))
            return BadRequest("University does not exist.");

        var e = await _db.Events.FindAsync(id);
        if (e is null) return NotFound();

        e.Title = req.Title;
        e.Location = req.Location;
        e.StartsAtUtc = req.StartsAtUtc;
        e.ImageUrl = req.ImageUrl;
        e.Description = req.Description;
        e.UniversityId = req.UniversityId.Value;
        await _db.SaveChangesAsync();
        return new EventDto(e.Id, e.Title, e.Location, e.StartsAtUtc, e.ImageUrl, e.Description);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.Events.FindAsync(id);
        if (e is null) return NotFound();
        _db.Events.Remove(e);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateEventRequest(
    string Title, string Location, DateTime StartsAtUtc,
    string? ImageUrl, string? Description, int? UniversityId = null);
