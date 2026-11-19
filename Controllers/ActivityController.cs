using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ActivityController : ControllerBase
{
    private readonly AcademicService _academic;
    public ActivityController(AcademicService academic) => _academic = academic;

    [HttpGet]
    public async Task<IReadOnlyList<ActivityDto>> Get([FromQuery] int take = 10) =>
        await _academic.GetActivityAsync(User.GetUserId(), Math.Clamp(take, 1, 50));
}
