using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Models;

namespace UniSphere.Api.Data;

/// <summary>
/// Imports Data/courses-&lt;university-slug&gt;.csv. The university comes from
/// the file name, never from a CSV cell or the browser. Records are upserted
/// by (UniversityId, Code), so running the importer repeatedly is safe.
/// Prerequisite codes are kept only when they resolve to a course in the
/// same university.
/// </summary>
public static class CatalogImporter
{
    public static CatalogImportResult ImportAll(
        AppDbContext db, string contentRoot, ILogger? logger = null)
    {
        var dataDirectory = Path.Combine(contentRoot, "Data");
        if (!Directory.Exists(dataDirectory))
            return new CatalogImportResult(0, 0, 0);

        var files = Directory.GetFiles(dataDirectory, "courses-*.csv")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inserted = 0;
        var updated = 0;
        var universities = 0;

        foreach (var file in files)
        {
            var slug = Path.GetFileNameWithoutExtension(file)["courses-".Length..];
            var universityName = CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(slug.Replace('-', ' ').Trim());
            if (string.IsNullOrWhiteSpace(universityName)) continue;

            var university = db.Universities.FirstOrDefault(u => u.Name == universityName);
            if (university is null)
            {
                university = new University { Name = universityName };
                db.Universities.Add(university);
                db.SaveChanges();
                universities++;
            }

            var rows = ParseFile(file);
            if (rows.Count == 0) continue;

            var existing = db.Courses
                .Where(c => c.UniversityId == university.Id)
                .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var code = Get(row, "Code").Trim();
                if (code.Length == 0) continue;

                if (!existing.TryGetValue(code, out var course))
                {
                    course = new Course
                    {
                        Code = code,
                        UniversityId = university.Id
                    };
                    db.Courses.Add(course);
                    existing[code] = course;
                    inserted++;
                }
                else
                {
                    updated++;
                }

                course.Title = Get(row, "Title").Trim();
                course.Description = Get(row, "Description").Trim();
                course.Track = Get(row, "Track").Trim();
                course.Credits = ParseInt(Get(row, "Credits"), 3);
                course.Department = FullDepartment(Get(row, "Department"));
                course.AcademicYear = ParseNullableInt(Get(row, "AcademicYear"));
                course.Semester = Get(row, "Semester").Trim();
                // The UniversityId column in the CSV is deliberately ignored.
            }

            db.SaveChanges();

            // Prerequisites after all codes are in the dictionary. Only a
            // course from the same university is ever stored.
            var byCode = db.Courses
                .Where(c => c.UniversityId == university.Id)
                .ToList()
                .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var code = Get(row, "Code").Trim();
                if (!byCode.TryGetValue(code, out var course)) continue;

                var prerequisites = SplitCodes(Get(row, "Prerequisites"))
                    .Select(p => byCode.TryGetValue(p, out var match) ? match.Code : null)
                    .Where(p => p is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                course.Prerequisites = string.Join(", ", prerequisites!);
            }

            db.SaveChanges();
            logger?.LogInformation(
                "Imported catalog {File}: {Courses} courses for {University}",
                Path.GetFileName(file), rows.Count, universityName);
        }

        return new CatalogImportResult(inserted, updated, universities);
    }

    private static List<Dictionary<string, string>> ParseFile(string path)
    {
        // WPS/Excel may keep the catalog open while the app starts.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        if (lines.Count == 0) return new();

        var headers = ParseCsvLine(lines[0].TrimStart('\uFEFF'))
            .Select(h => h.Trim())
            .ToList();
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
                row[headers[c]] = c < values.Count ? values[c] : "";
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Minimal RFC4180 parser: quoted fields, escaped double quotes,
    /// and commas inside quotes (the Helwan track column uses them).</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : "";

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : fallback;

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : null;

    private static IEnumerable<string> SplitCodes(string value) =>
        value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0);

    private static string FullDepartment(string code) => code.Trim().ToUpperInvariant() switch
    {
        "CS" => "Computer Science",
        "IS" => "Information Systems",
        "IT" => "Information Technology",
        "AI" => "Artificial Intelligence",
        "HU" => "Humanities",
        "MA" => "Mathematics",
        "PH" => "Physics",
        "ST" => "Statistics",
        var other => other
    };
}

public sealed record CatalogImportResult(int Inserted, int Updated, int NewUniversities);
