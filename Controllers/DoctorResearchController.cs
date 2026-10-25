using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>
/// Phase 2 research hub.
///
/// My Research routes are Doctor-only and owner-scoped: foreign paper ids are
/// 404 (indistinguishable from missing ids). Browse is deliberately
/// cross-university (the one exception to university isolation) and exposes
/// published papers to Doctors and Admins only, with just the author's name,
/// university and department - never email, phone or any other private data.
/// PDFs live outside wwwroot and are streamed by this authenticated controller;
/// the database stores only a generated file name.
/// </summary>
[ApiController]
[Route("api/doctor/research")]
public class DoctorResearchController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ResearchFileStorage _files;

    public DoctorResearchController(AppDbContext db, ResearchFileStorage files)
    {
        _db = db;
        _files = files;
    }

    private int UserId => User.GetUserId();

    private Task<User?> MeAsync() =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == UserId && !u.IsDeleted);

    private void LogAudit(string path, int statusCode)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = UserId,
            Method = HttpContext.Request.Method,
            Path = path,
            StatusCode = statusCode,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
    }

    private static DoctorResearchDto ToDto(DoctorResearch paper) =>
        new(paper.Id, paper.Title, paper.Abstract, paper.Authors, paper.Keywords,
            paper.Year, paper.Field, paper.DoiOrUrl, paper.IsPublished,
            !string.IsNullOrWhiteSpace(paper.PdfPath),
            paper.CreatedAtUtc, paper.UpdatedAtUtc);

    private BadRequestObjectResult ValidationBadRequest(
        FluentValidation.Results.ValidationResult validation) =>
        BadRequest(new ValidationProblemDetails(validation.ToDictionary())
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest
        });

    private BadRequestObjectResult PdfBadRequest(string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["pdf"] = new[] { message }
        })
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest
        });

    // ---- My Research (Doctor only, owner-scoped) ----

    [HttpGet]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<IReadOnlyList<DoctorResearchDto>>> MyResearch()
    {
        var me = await MeAsync();
        if (me is null) return Unauthorized();

        var papers = await _db.DoctorResearches.AsNoTracking()
            .Where(r => r.DoctorUserId == me.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .ToListAsync();

        return papers.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<DoctorResearchDto>> MyResearchById(int id)
    {
        var me = await MeAsync();
        if (me is null) return Unauthorized();

        var paper = await _db.DoctorResearches.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.DoctorUserId == me.Id);
        if (paper is null) return NotFound();

        return ToDto(paper);
    }

    [HttpPost]
    [Authorize(Roles = "Doctor")]
    [EnableRateLimiting("research-upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 25 * 1024 * 1024)]
    public async Task<ActionResult<DoctorResearchDto>> Create(
        [FromForm] DoctorResearchUploadRequest req,
        [FromServices] IValidator<DoctorResearchUploadRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid) return ValidationBadRequest(validation);

        var me = await MeAsync();
        if (me is null) return Unauthorized();

        if (req.Pdf is null)
            return PdfBadRequest("A PDF file is required.");

        string fileName;
        try
        {
            fileName = await _files.SavePdfAsync(req.Pdf, HttpContext.RequestAborted);
        }
        catch (ResearchFileException ex)
        {
            return PdfBadRequest(ex.Message);
        }

        var now = DateTime.UtcNow;
        var paper = new DoctorResearch
        {
            // Ownership and university always come from the authenticated
            // account, never from the form. The university id is part of the
            // module's model (non-null); when a research doctor has no
            // university assigned yet, 0 means "none" (never matches a filter).
            DoctorUserId = me.Id,
            UniversityId = me.UniversityId ?? 0,
            Title = req.Title.Trim(),
            Abstract = req.Abstract.Trim(),
            Authors = req.Authors.Trim(),
            Keywords = req.Keywords?.Trim() ?? "",
            Year = req.Year,
            Field = req.Field.Trim(),
            DoiOrUrl = string.IsNullOrWhiteSpace(req.DoiOrUrl) ? null : req.DoiOrUrl.Trim(),
            IsPublished = req.IsPublished,
            PdfPath = fileName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.DoctorResearches.Add(paper);
        LogAudit("/api/doctor/research", StatusCodes.Status201Created);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _files.Delete(fileName);
            throw;
        }

        return CreatedAtAction(nameof(MyResearchById), new { id = paper.Id }, ToDto(paper));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Doctor")]
    [EnableRateLimiting("research-upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 25 * 1024 * 1024)]
    public async Task<ActionResult<DoctorResearchDto>> Update(
        int id,
        [FromForm] DoctorResearchUploadRequest req,
        [FromServices] IValidator<DoctorResearchUploadRequest> validator)
    {
        var validation = await validator.ValidateAsync(req);
        if (!validation.IsValid) return ValidationBadRequest(validation);

        var me = await MeAsync();
        if (me is null) return Unauthorized();

        var paper = await _db.DoctorResearches
            .FirstOrDefaultAsync(r => r.Id == id && r.DoctorUserId == me.Id);
        if (paper is null) return NotFound();

        var oldFileName = paper.PdfPath;
        string? newFileName = null;

        if (req.Pdf is not null)
        {
            try
            {
                newFileName = await _files.SavePdfAsync(req.Pdf, HttpContext.RequestAborted);
            }
            catch (ResearchFileException ex)
            {
                return PdfBadRequest(ex.Message);
            }
        }

        paper.Title = req.Title.Trim();
        paper.Abstract = req.Abstract.Trim();
        paper.Authors = req.Authors.Trim();
        paper.Keywords = req.Keywords?.Trim() ?? "";
        paper.Year = req.Year;
        paper.Field = req.Field.Trim();
        paper.DoiOrUrl = string.IsNullOrWhiteSpace(req.DoiOrUrl) ? null : req.DoiOrUrl.Trim();
        paper.IsPublished = req.IsPublished;
        paper.UpdatedAtUtc = DateTime.UtcNow;
        if (newFileName is not null) paper.PdfPath = newFileName;

        LogAudit($"/api/doctor/research/{id}", StatusCodes.Status200OK);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (newFileName is not null) _files.Delete(newFileName);
            throw;
        }

        if (newFileName is not null && !string.Equals(oldFileName, newFileName, StringComparison.Ordinal))
            _files.Delete(oldFileName);

        return ToDto(paper);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Delete(int id)
    {
        var me = await MeAsync();
        if (me is null) return Unauthorized();

        var paper = await _db.DoctorResearches
            .FirstOrDefaultAsync(r => r.Id == id && r.DoctorUserId == me.Id);
        if (paper is null) return NotFound();

        _db.DoctorResearches.Remove(paper);
        LogAudit($"/api/doctor/research/{id}", StatusCodes.Status204NoContent);
        await _db.SaveChangesAsync();

        _files.Delete(paper.PdfPath);
        return NoContent();
    }

    // ---- Browse (cross-university; published papers only) ----

    [HttpGet("browse")]
    [Authorize(Roles = "Doctor,Admin")]
    public async Task<ActionResult<PagedResultDto<ResearchBrowseItemDto>>> Browse(
        [FromQuery] string? search = null,
        [FromQuery] int? universityId = null,
        [FromQuery] string? field = null,
        [FromQuery] int? year = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // Deliberately not scoped by IUniversityScope: Browse shows published
        // work from every university. Deleted authors are hidden.
        var query =
            from r in _db.DoctorResearches.AsNoTracking()
            join u in _db.Users.AsNoTracking() on r.DoctorUserId equals u.Id
            join uni in _db.Universities.AsNoTracking()
                on r.UniversityId equals (int?)uni.Id into universities
            from uni in universities.DefaultIfEmpty()
            where r.IsPublished && !u.IsDeleted
            select new
            {
                Research = r,
                Author = u,
                UniversityName = uni != null ? uni.Name : ""
            };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Research.Title.ToLower().Contains(term) ||
                x.Research.Abstract.ToLower().Contains(term) ||
                x.Research.Keywords.ToLower().Contains(term) ||
                x.Research.Authors.ToLower().Contains(term) ||
                x.Author.FullName.ToLower().Contains(term));
        }

        if (universityId.HasValue)
            query = query.Where(x => x.Research.UniversityId == universityId.Value);

        if (!string.IsNullOrWhiteSpace(field))
        {
            var wanted = field.Trim().ToLower();
            query = query.Where(x => x.Research.Field.ToLower() == wanted);
        }

        if (year.HasValue)
            query = query.Where(x => x.Research.Year == year.Value);

        var total = await query.CountAsync();

        var rows = await query
            .OrderByDescending(x => x.Research.CreatedAtUtc)
            .ThenByDescending(x => x.Research.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = rows.Select(x => new ResearchBrowseItemDto(
            x.Research.Id, x.Research.Title, x.Research.Authors, x.Research.Keywords,
            x.Research.Year, x.Research.Field, x.Author.FullName, x.UniversityName,
            !string.IsNullOrWhiteSpace(x.Research.PdfPath), x.Research.CreatedAtUtc)).ToList();

        return new PagedResultDto<ResearchBrowseItemDto>(items, total, page, pageSize);
    }

    [HttpGet("browse/filters")]
    [Authorize(Roles = "Doctor,Admin")]
    public async Task<ActionResult<ResearchFiltersDto>> Filters()
    {
        var rows = await (
            from r in _db.DoctorResearches.AsNoTracking()
            join u in _db.Users.AsNoTracking() on r.DoctorUserId equals u.Id
            where r.IsPublished && !u.IsDeleted
            select new { r.UniversityId, r.Field, r.Year }).ToListAsync();

        var universityIds = rows
            .Select(x => x.UniversityId)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var universities = await _db.Universities.AsNoTracking()
            .Where(u => universityIds.Contains(u.Id))
            .OrderBy(u => u.Name)
            .Select(u => new ResearchUniversityOptionDto(u.Id, u.Name))
            .ToListAsync();

        var fields = rows
            .Select(x => x.Field)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var years = rows
            .Select(x => x.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        return new ResearchFiltersDto(universities, fields, years);
    }

    [HttpGet("browse/{id:int}")]
    [Authorize(Roles = "Doctor,Admin")]
    public async Task<ActionResult<ResearchDetailDto>> BrowseDetails(int id)
    {
        var row = await (
            from r in _db.DoctorResearches.AsNoTracking()
            join u in _db.Users.AsNoTracking() on r.DoctorUserId equals u.Id
            join uni in _db.Universities.AsNoTracking()
                on r.UniversityId equals (int?)uni.Id into universities
            from uni in universities.DefaultIfEmpty()
            where r.Id == id && r.IsPublished && !u.IsDeleted
            select new
            {
                Research = r,
                Author = u,
                UniversityName = uni != null ? uni.Name : ""
            }).FirstOrDefaultAsync();

        // Drafts are never visible in Browse, even to other doctors/admins.
        if (row is null) return NotFound();

        var paper = row.Research;
        return new ResearchDetailDto(
            paper.Id, paper.Title, paper.Abstract, paper.Authors, paper.Keywords,
            paper.Year, paper.Field, paper.DoiOrUrl,
            row.Author.FullName, row.Author.Department, row.UniversityName,
            !string.IsNullOrWhiteSpace(paper.PdfPath),
            paper.CreatedAtUtc, paper.UpdatedAtUtc);
    }

    /// <summary>Authenticated PDF download. Published papers are available to
    /// any Doctor/Admin; drafts only to their owner. The response never
    /// includes the physical storage path.</summary>
    [HttpGet("{id:int}/pdf")]
    [Authorize(Roles = "Doctor,Admin")]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var paper = await _db.DoctorResearches.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (paper is null) return NotFound();

        if (!paper.IsPublished && paper.DoctorUserId != UserId) return NotFound();

        var path = _files.ResolvePath(paper.PdfPath);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, "application/pdf", DownloadName(paper),
            enableRangeProcessing: true);
    }

    private static string DownloadName(DoctorResearch paper)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(paper.Title
            .Where(c => !invalid.Contains(c) && c != '\r' && c != '\n')
            .ToArray())
            .Trim();
        if (cleaned.Length > 80) cleaned = cleaned[..80];
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = $"research-{paper.Id}";
        return $"{cleaned}.pdf";
    }
}
