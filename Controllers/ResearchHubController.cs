using System.Text;
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
/// Research Hub for doctors. Identity (doctor id + university id) is always
/// resolved from the JWT claims — never from client input. Uploaded PDFs are
/// verified by magic bytes (%PDF-), capped at 20 MB, stored under random GUID
/// names in App_Data/research (OUTSIDE wwwroot, so nothing is served
/// statically) and streamed back only through the authorized GET {id}/pdf
/// action; server paths are never exposed. The Browse tab is the deliberate
/// exception to university isolation: doctors and admins of ALL universities
/// see every PUBLISHED paper, and the output carries only the author's name,
/// university and department — never email or phone.
/// </summary>
[Authorize(Roles = "Doctor,Admin")]
[ApiController]
[Route("api/research-hub")]
public class ResearchHubController : ControllerBase
{
    private const long MaxPdfBytes = 20 * 1024 * 1024; // 20 MB
    private const string PdfMagic = "%PDF-";
    public const int PageSize = 20;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly DoctorAccessService _access;

    public ResearchHubController(AppDbContext db, IWebHostEnvironment env, DoctorAccessService access)
    {
        _db = db;
        _env = env;
        _access = access;
    }

    private int MeId => User.GetUserId();

    private string ResearchDir => Path.Combine(_env.ContentRootPath, "App_Data", "research");

    // ---- Browse (cross-university, published only) ----

    /// <summary>GET /api/research-hub — every PUBLISHED paper from doctors of
    /// ALL universities, with search (title/abstract/keywords/authors/author
    /// name), filters (university, field, year), sort = newest, paging 20.</summary>
    [HttpGet]
    public async Task<ActionResult<ResearchHubPageDto>> Browse(
        [FromQuery] string? q = null,
        [FromQuery] int? universityId = null,
        [FromQuery] string? field = null,
        [FromQuery] int? year = null,
        [FromQuery] int page = 1)
    {
        page = Math.Max(1, page);

        var query = _db.DoctorResearches.AsNoTracking()
            .Where(r => r.IsPublished);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(r =>
                r.Title.ToLower().Contains(term) ||
                r.Abstract.ToLower().Contains(term) ||
                r.Keywords.ToLower().Contains(term) ||
                r.Authors.ToLower().Contains(term) ||
                (r.DoctorUser != null && r.DoctorUser.FullName.ToLower().Contains(term)));
        }
        if (universityId is > 0)
            query = query.Where(r => r.UniversityId == universityId.Value);
        if (!string.IsNullOrWhiteSpace(field))
            query = query.Where(r => r.Field == field.Trim());
        if (year is > 0)
            query = query.Where(r => r.Year == year.Value);

        var total = await query.CountAsync();
        var items = await ProjectItems(query
                .OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize))
            .ToListAsync();

        return new ResearchHubPageDto(items, total, page, PageSize);
    }

    /// <summary>GET /api/research-hub/filters — dropdown options derived from
    /// published papers (universities referenced, distinct fields and years).</summary>
    [HttpGet("filters")]
    public async Task<ActionResult<ResearchHubFiltersDto>> Filters()
    {
        var published = _db.DoctorResearches.AsNoTracking().Where(r => r.IsPublished);

        var universityIds = await published
            .Select(r => r.UniversityId).Distinct().ToListAsync();
        var universities = await _db.Universities.AsNoTracking()
            .Where(u => universityIds.Contains(u.Id))
            .OrderBy(u => u.Name)
            .Select(u => new UniversityDto(u.Id, u.Name, u.Logo))
            .ToListAsync();

        var fields = await published
            .Select(r => r.Field).Distinct().OrderBy(f => f).ToListAsync();
        var years = await published
            .Select(r => r.Year).Distinct().OrderByDescending(y => y).ToListAsync();

        return new ResearchHubFiltersDto(universities, fields, years);
    }

    /// <summary>GET /api/research-hub/{id} — details. Published papers are
    /// visible to every Doctor/Admin; drafts resolve to the owner only
    /// (anyone else gets 404 so drafts never leak).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ResearchHubItemDto>> Details(int id)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var paper = await _db.DoctorResearches.AsNoTracking()
            .Include(r => r.DoctorUser)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (paper is null) return NotFound();
        if (!paper.IsPublished && paper.DoctorUserId != me.Id) return NotFound();

        return await ToItemAsync(paper);
    }

    /// <summary>GET /api/research-hub/{id}/pdf — download. Login required
    /// (class-level authorize). Published papers may be downloaded by any
    /// Doctor/Admin; a draft is downloadable by its owner only (404 for
    /// everyone else). Streams from App_Data with a sanitized client-facing
    /// file name — the physical path is never revealed.</summary>
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var paper = await _db.DoctorResearches.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (paper is null) return NotFound();

        var isOwner = paper.DoctorUserId == me.Id;
        if (!paper.IsPublished && !isOwner) return NotFound();

        if (string.IsNullOrWhiteSpace(paper.PdfPath))
            return NotFound("This paper has no PDF attached.");

        var physicalPath = Path.Combine(ResearchDir, Path.GetFileName(paper.PdfPath));
        if (!System.IO.File.Exists(physicalPath)) return NotFound("The PDF file is missing.");

        return PhysicalFile(physicalPath, "application/pdf", BuildDownloadName(paper));
    }

    // ---- My research (doctor-only ownership CRUD) ----

    /// <summary>GET /api/research-hub/mine — the doctor's OWN papers, any
    /// publish state, including drafts. Resolved from claims only; admins are
    /// excluded here (action role intersects the class role to Doctor).</summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<IReadOnlyList<ResearchHubItemDto>>> Mine()
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var papers = await ProjectItems(_db.DoctorResearches.AsNoTracking()
                .Where(r => r.DoctorUserId == me.Id)
                .OrderByDescending(r => r.UpdatedAtUtc).ThenByDescending(r => r.Id))
            .ToListAsync();
        return papers;
    }

    /// <summary>POST /api/research-hub — uploads a paper: multipart form
    /// (metadata + required PDF). The PDF is accepted only when its REAL
    /// bytes start with %PDF- (the client MIME type is ignored), it is
    /// capped at 20 MB (action limit + body size limit), and it is stored
    /// under a random GUID name in App_Data/research. The owner
    /// (DoctorUserId) and university (UniversityId) are stamped server-side
    /// from the authenticated user. Rate limited + audit logged.</summary>
    [HttpPost]
    [Authorize(Roles = "Doctor")]
    [EnableRateLimiting("research-upload")]
    [RequestSizeLimit(MaxPdfBytes)]
    public async Task<ActionResult<ResearchHubItemDto>> Create(
        [FromForm] ResearchUploadForm form,
        [FromServices] FluentValidation.IValidator<ResearchUploadForm> validator)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var validation = await validator.ValidateAsync(form);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        if (me.UniversityId is null)
            return BadRequest("Your account has no university assigned. Contact an administrator first.");

        if (form.PdfFile is null || form.PdfFile.Length == 0)
            return BadRequest("A PDF file is required.");
        if (form.PdfFile.Length > MaxPdfBytes)
            return BadRequest("The PDF exceeds the 20 MB limit.");
        var storeName = await StorePdfAsync(form.PdfFile);
        if (storeName is null)
            return BadRequest("The uploaded file is not a valid PDF (magic bytes %PDF- required).");

        var paper = new DoctorResearch
        {
            DoctorUserId = me.Id,                 // from claims — never from the client
            UniversityId = me.UniversityId.Value, // from claims — never from the client
            Title = form.Title.Trim(),
            Abstract = form.Abstract.Trim(),
            Authors = form.Authors.Trim(),
            Keywords = form.Keywords.Trim(),
            Year = form.Year,
            Field = form.Field.Trim(),
            DoiOrUrl = string.IsNullOrWhiteSpace(form.DoiOrUrl) ? null : form.DoiOrUrl.Trim(),
            PdfPath = storeName,
            IsPublished = form.IsPublished,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.DoctorResearches.Add(paper);
        await _db.SaveChangesAsync();

        var full = await _db.DoctorResearches.AsNoTracking()
            .Include(r => r.DoctorUser)
            .FirstAsync(r => r.Id == paper.Id);
        return CreatedAtAction(nameof(Details), new { id = paper.Id }, await ToItemAsync(full));
    }

    /// <summary>PUT /api/research-hub/{id} — edits the paper. Only the
    /// owner's own paper is editable; any other doctor's paper (or unknown
    /// id) returns 404. A replacement PDF goes through the same magic-byte
    /// validation and the old file is deleted only after the row is saved.
    /// Rate limited + audit logged.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Doctor")]
    [EnableRateLimiting("research-upload")]
    [RequestSizeLimit(MaxPdfBytes)]
    public async Task<ActionResult<ResearchHubItemDto>> Update(
        int id,
        [FromForm] ResearchUploadForm form,
        [FromServices] FluentValidation.IValidator<ResearchUploadForm> validator)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var validation = await validator.ValidateAsync(form);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary())
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            });

        // Ownership: the row must belong to this doctor, else 404 (no leak).
        var paper = await _db.DoctorResearches
            .FirstOrDefaultAsync(r => r.Id == id && r.DoctorUserId == me.Id);
        if (paper is null) return NotFound();

        var oldStoreName = paper.PdfPath;
        string? newStoreName = null;
        if (form.PdfFile is not null && form.PdfFile.Length > 0)
        {
            if (form.PdfFile.Length > MaxPdfBytes)
                return BadRequest("The PDF exceeds the 20 MB limit.");
            newStoreName = await StorePdfAsync(form.PdfFile);
            if (newStoreName is null)
                return BadRequest("The uploaded file is not a valid PDF (magic bytes %PDF- required).");
        }
        else if (string.IsNullOrWhiteSpace(oldStoreName))
        {
            return BadRequest("Attach a PDF file (this paper has none).");
        }

        paper.Title = form.Title.Trim();
        paper.Abstract = form.Abstract.Trim();
        paper.Authors = form.Authors.Trim();
        paper.Keywords = form.Keywords.Trim();
        paper.Year = form.Year;
        paper.Field = form.Field.Trim();
        paper.DoiOrUrl = string.IsNullOrWhiteSpace(form.DoiOrUrl) ? null : form.DoiOrUrl.Trim();
        paper.IsPublished = form.IsPublished;
        paper.UpdatedAtUtc = DateTime.UtcNow;
        if (newStoreName is not null)
            paper.PdfPath = newStoreName;
        await _db.SaveChangesAsync();

        // Old file deleted only after the row was persisted successfully.
        if (newStoreName is not null && !string.IsNullOrWhiteSpace(oldStoreName) && oldStoreName != newStoreName)
            DeleteStoredPdf(oldStoreName);
        var full = await _db.DoctorResearches.AsNoTracking()
            .Include(r => r.DoctorUser)
            .FirstAsync(r => r.Id == paper.Id);
        return Ok(await ToItemAsync(full));
    }

    /// <summary>PUT /api/research-hub/{id}/publish — publish/unpublish toggle.
    /// Owner only (else 404). Publishing requires an attached PDF so the
    /// Browse download path can never offer a broken file.</summary>
    [HttpPut("{id:int}/publish")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> SetPublished(int id, [FromBody] SetResearchPublishRequest req)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var paper = await _db.DoctorResearches
            .FirstOrDefaultAsync(r => r.Id == id && r.DoctorUserId == me.Id);
        if (paper is null) return NotFound();

        if (req.IsPublished && (string.IsNullOrWhiteSpace(paper.PdfPath)
            || !System.IO.File.Exists(Path.Combine(ResearchDir, Path.GetFileName(paper.PdfPath)))))
            return BadRequest("Attach a PDF before publishing the paper.");

        paper.IsPublished = req.IsPublished;
        paper.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>DELETE /api/research-hub/{id} — removes the paper and its
    /// stored PDF. Owner only; other doctors' papers return 404.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Delete(int id)
    {
        var me = await _access.GetUserAsync(MeId);
        if (me is null || me.IsDeleted) return Unauthorized();

        var paper = await _db.DoctorResearches
            .FirstOrDefaultAsync(r => r.Id == id && r.DoctorUserId == me.Id);
        if (paper is null) return NotFound();

        _db.DoctorResearches.Remove(paper);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(paper.PdfPath))
            DeleteStoredPdf(paper.PdfPath);
        return NoContent();
    }

    // ---- helpers ----

    /// <summary>Browse/mine projection — fully translated to SQL (the
    /// university name comes from a correlated subquery). Never includes
    /// server paths or private author data (email, phone).</summary>
    private IQueryable<ResearchHubItemDto> ProjectItems(IQueryable<DoctorResearch> query) => query
        .Select(r => new ResearchHubItemDto(
            r.Id, r.Title, r.Abstract, r.Authors, r.Keywords, r.Year, r.Field,
            r.DoiOrUrl, r.IsPublished, r.CreatedAtUtc, r.UpdatedAtUtc,
            r.DoctorUserId,
            r.DoctorUser != null ? r.DoctorUser.FullName : "",
            _db.Universities.Any(u => u.Id == r.UniversityId)
                ? _db.Universities.Where(u => u.Id == r.UniversityId).Select(u => u.Name).First()
                : "",
            r.DoctorUser != null ? r.DoctorUser.Department : "",
            !string.IsNullOrWhiteSpace(r.PdfPath)));

    /// <summary>Materializes one loaded paper (with DoctorUser included) into
    /// the DTO. The university name is looked up separately — Users store only
    /// the university id.</summary>
    private async Task<ResearchHubItemDto> ToItemAsync(DoctorResearch r)
    {
        var universityName = r.UniversityId > 0
            ? await _db.Universities.AsNoTracking()
                .Where(u => u.Id == r.UniversityId)
                .Select(u => u.Name)
                .FirstOrDefaultAsync()
            : null;

        return new ResearchHubItemDto(
            r.Id, r.Title, r.Abstract, r.Authors, r.Keywords, r.Year, r.Field,
            r.DoiOrUrl, r.IsPublished, r.CreatedAtUtc, r.UpdatedAtUtc,
            r.DoctorUserId,
            r.DoctorUser?.FullName ?? $"Doctor {r.DoctorUserId}",
            universityName ?? "",
            r.DoctorUser?.Department ?? "",
            !string.IsNullOrWhiteSpace(r.PdfPath));
    }

    /// <summary>Validates the REAL bytes (magic %PDF- read from the stream —
    /// the declared content type is ignored), stores the file under a random
    /// GUID name in App_Data/research and returns just the file name — or
    /// null when the content is not a PDF.</summary>
    private async Task<string?> StorePdfAsync(IFormFile file)
    {
        var header = new byte[5];
        var read = 0;
        await using (var open = file.OpenReadStream())
        {
            while (read < header.Length)
            {
                var n = await open.ReadAsync(header.AsMemory(read, header.Length - read));
                if (n == 0) break;
                read += n;
            }
        }
        if (read != header.Length || Encoding.ASCII.GetString(header) != PdfMagic)
            return null;

        Directory.CreateDirectory(ResearchDir);
        var storeName = Guid.NewGuid().ToString("N") + ".pdf";
        var physicalPath = Path.Combine(ResearchDir, storeName);
        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await file.CopyToAsync(stream);
        }
        return storeName;
    }

    /// <summary>Deletes a stored PDF by its server-generated GUID name. The
    /// name always originates from our own rows (never client input) and is
    /// re-validated as a GUID+pdf file before the physical delete.</summary>
    private void DeleteStoredPdf(string storeName)
    {
        try
        {
            var name = Path.GetFileName(storeName);
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _)
                && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(ResearchDir, name);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best-effort; never break the API over a file delete.
        }
    }

    private static string BuildDownloadName(DoctorResearch paper)
    {
        var safe = new string(paper.Title
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
            .ToArray()).Trim();
        if (safe.Length > 60) safe = safe[..60].Trim();
        var name = string.IsNullOrWhiteSpace(safe) ? $"research-{paper.Id}.pdf" : $"research-{safe}.pdf";
        return name.Replace(' ', '-');
    }
}

// ---- Multipart form model for the upload/edit endpoints. Note that
// DoctorUserId and UniversityId are deliberately absent: the server always
// stamps them from the authenticated user. ----
public sealed class ResearchUploadForm
{
    public string Title { get; set; } = "";
    public string Abstract { get; set; } = "";
    public string Authors { get; set; } = "";
    public string Keywords { get; set; } = "";
    public int Year { get; set; }
    public string Field { get; set; } = "";
    public string? DoiOrUrl { get; set; }
    public bool IsPublished { get; set; }
    public IFormFile? PdfFile { get; set; }
}
