using System.Security.Claims;

namespace UniSphere.Api.Services;

public static class ClaimsExtensions
{
    /// <summary>The logged-in user's Id from the JWT (0 if missing).</summary>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : 0;
    }
}
