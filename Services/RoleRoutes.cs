using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>Single source of truth for each role's home route.</summary>
public static class RoleRoutes
{
    public static string HomeFor(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "student" => "/dashboard",
        "doctor" => "/doctor",
        "graduate" => "/graduate",
        "admin" => "/admin",
        _ => "/login"
    };

    public static string HomeFor(UserRole role) => HomeFor(role.ToString());

    public static bool RoleAllowed(string? currentRole, string? allowedRoles)
    {
        if (string.IsNullOrWhiteSpace(allowedRoles)) return true;
        if (string.IsNullOrWhiteSpace(currentRole)) return false;
        return allowedRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(r => string.Equals(r, currentRole, StringComparison.OrdinalIgnoreCase));
    }
}
