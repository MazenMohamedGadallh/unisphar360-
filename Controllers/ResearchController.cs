using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ResearchController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ResearchController> _logger;
    public ResearchController(AppDbContext db, ILogger<ResearchController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Search research papers from the local database.</summary>
    [HttpGet("papers")]
    public async Task<ActionResult<IReadOnlyList<object>>> SearchPapers([FromQuery] string? q)
    {
        var query = _db.ResearchPapers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p =>
                p.Title.ToLower().Contains(term) ||
                (p.Authors != null && p.Authors.ToLower().Contains(term)) ||
                (p.Abstract != null && p.Abstract.ToLower().Contains(term)));
        }

        var papers = await query
            .OrderByDescending(p => p.PublishedDate)
            .Select(p => new { p.Id, p.Title, p.Authors, p.Abstract, p.Url, p.Source, p.PublishedDate })
            .ToListAsync();

        return Ok(papers);
    }

    /// <summary>Add a research paper to the local database (Admin/Doctor only).</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpPost("papers")]
    public async Task<ActionResult<object>> AddPaper([FromBody] CreatePaperRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("Title is required.");

        var paper = new ResearchPaper
        {
            Title = req.Title,
            Authors = req.Authors ?? "",
            Abstract = req.Abstract,
            Url = req.Url,
            Source = req.Source ?? "other",
            PublishedDate = req.PublishedDate
        };

        _db.ResearchPapers.Add(paper);
        await _db.SaveChangesAsync();

        return Ok(new { paper.Id, paper.Title, paper.Authors, paper.Abstract, paper.Url, paper.Source });
    }

    /// <summary>Simple extractive summarization: returns the first 2-3 sentences of the abstract.</summary>
    [HttpGet("summarize/{id:int}")]
    public async Task<ActionResult<object>> Summarize(int id)
    {
        var paper = await _db.ResearchPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (paper is null) return NotFound();

        if (string.IsNullOrEmpty(paper.Abstract))
            return Ok(new { paper.Id, paper.Title, Summary = "No abstract available for summarization." });

        // Simple extractive summary: first 3 sentences
        var sentences = paper.Abstract.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
        var summary = sentences.Length <= 3
            ? paper.Abstract
            : string.Join(". ", sentences.Take(3)) + ".";

        return Ok(new { paper.Id, paper.Title, Summary = summary, OriginalLength = paper.Abstract.Length, SummaryLength = summary.Length });
    }

    /// <summary>Get trending papers (most recent).</summary>
    [HttpGet("trending")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetTrending()
    {
        var papers = await _db.ResearchPapers.AsNoTracking()
            .OrderByDescending(p => p.PublishedDate)
            .Take(10)
            .Select(p => new { p.Id, p.Title, p.Authors, p.Abstract, p.Url, p.Source, p.PublishedDate })
            .ToListAsync();

        return Ok(papers);
    }
}

public record CreatePaperRequest(string Title, string? Authors = null, string? Abstract = null, string? Url = null, string? Source = null, DateTime? PublishedDate = null);
