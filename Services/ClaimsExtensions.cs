using System.Security.Claims;

namespace UniSphere.Api.Services;

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : 0;
    }
}
