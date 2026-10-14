using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>Simple rule-based recommender. Replace with the AI team's service later.</summary>
public static class Recommender
{
    public static string TrackFor(string major)
    {
        var m = major.ToLowerInvariant();
        if (m.Contains("ai") || m.Contains("data") || m.Contains("machine") || m.Contains("computer engineering"))
            return "AI";
        return "Software";
    }

    /// <summary>Courses in the user's track first, then the rest.</summary>
    public static IEnumerable<Course> Rank(IEnumerable<Course> courses, User user)
    {
        var track = TrackFor(user.Major);
        return courses.OrderByDescending(c => c.Track == track).ThenBy(c => c.Code);
    }
}
