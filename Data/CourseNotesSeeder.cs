using System.Text.Json;
using UniSphere.Api.Models;

namespace UniSphere.Api.Data;

public static class CourseNotesSeeder
{
    private sealed record Note(string Title, string[] Courses, string Content,
        string? Type = null, string? Description = null, int? DueInDays = null);

    // Study notes live in Data/course-notes, lectures and assignments in Data/course-materials.
    private static readonly string[] Folders = { "course-notes", "course-materials" };

    public static int Seed(AppDbContext db, string contentRoot)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var byCourseTitle = new Dictionary<string, List<Note>>();
        foreach (var folder in Folders)
        {
            var dir = Path.Combine(contentRoot, "Data", folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json").Order())
                foreach (var note in JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(file), options) ?? new())
                    foreach (var course in note.Courses)
                    {
                        var key = Normalize(course);
                        if (!byCourseTitle.TryGetValue(key, out var list)) byCourseTitle[key] = list = new();
                        if (list.All(n => n.Title != note.Title)) list.Add(note);
                    }
        }
        if (byCourseTitle.Count == 0) return 0;

        var existing = db.CourseMaterials.Select(m => new { m.CourseId, m.Title }).ToList()
            .Select(m => (m.CourseId, m.Title)).ToHashSet();
        var added = 0;
        var now = DateTime.UtcNow;
        foreach (var course in db.Courses.ToList())
        {
            if (!byCourseTitle.TryGetValue(Normalize(course.Title), out var notes)) continue;
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (!existing.Add((course.Id, note.Title))) continue;
                db.CourseMaterials.Add(new CourseMaterial
                {
                    CourseId = course.Id,
                    Title = note.Title,
                    Type = note.Type ?? "Summary",
                    Description = note.Description
                        ?? $"Study notes for {course.Title}: key topics, examples and review questions.",
                    Content = note.Content,
                    DeadlineUtc = note.DueInDays is int days ? now.Date.AddDays(days) : null,
                    // Keeps lectures in order (newest-first lists show Lecture 1 last otherwise).
                    CreatedAtUtc = now.AddMinutes(-i)
                });
                added++;
            }
        }
        if (added > 0) db.SaveChanges();
        return added;
    }

    private static string Normalize(string title) =>
        string.Join(' ', title.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
