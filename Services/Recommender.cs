using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public static class Recommender
{
    public static string TrackFor(string major)
    {
        var m = major.ToLowerInvariant();
        if (m.Contains("ai") || m.Contains("data") || m.Contains("machine") || m.Contains("computer engineering"))
            return "AI";
        return "Software";
    }

    public static IEnumerable<Course> Rank(IEnumerable<Course> courses, User user)
    {
        var track = TrackFor(user.Major);
        return courses.OrderByDescending(c => c.Track == track).ThenBy(c => c.Code);
    }
}
