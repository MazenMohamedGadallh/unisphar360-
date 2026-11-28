using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public static class PortfolioRules
{
    /// <summary>Absolute http(s) only, so a stored link can never run script when clicked.</summary>
    public static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    public static bool IsGitHubUrl(string? url) =>
        IsWebUrl(url) && new Uri(url!).Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Check, bool Ok, string Tip)> Checks(PortfolioProject p) =>
    [
        ("Description", p.Description.Trim().Length >= 80, "Describe the project in at least 80 characters."),
        ("Your role", !string.IsNullOrWhiteSpace(p.Role), "Say what you personally did."),
        ("Technologies", !string.IsNullOrWhiteSpace(p.Technologies), "List the technologies used."),
        ("Evidence", p.GitHubUrl != null || p.DemoUrl != null || (p.Outcomes?.Trim().Length ?? 0) >= 40,
            "Add a GitHub or demo link, or describe the outcome in at least 40 characters."),
        ("Problem solved", !string.IsNullOrWhiteSpace(p.Problem), "Explain the problem it solves."),
        ("Skills shown", !string.IsNullOrWhiteSpace(p.Skills), "Name the skills a reviewer should notice.")
    ];

    /// <summary>The first four checks are required; the rest are suggestions.</summary>
    public static bool IsValid(PortfolioProject p) => Checks(p).Take(4).All(c => c.Ok);
}
