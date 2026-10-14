using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;

namespace UniSphere.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModulesController : ControllerBase
{
    private readonly AppDbContext _db;
    public ModulesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IReadOnlyList<ModuleDto>> GetAll() =>
        await _db.Modules.AsNoTracking()
            .OrderBy(m => m.SortOrder)
            .Select(m => new ModuleDto(m.Key, m.Title, m.Subtitle, m.Icon, m.BackgroundColor, m.AccentColor))
            .ToListAsync();
}
