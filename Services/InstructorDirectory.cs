using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;

namespace UniSphere.Api.Services;

/// <summary>Read-only helper that resolves instructor display names for a set
/// of courses. Course ids always come from scoped server-side queries; the
/// result is what students see as "Instructor(s)" on a course.</summary>
public static class InstructorDirectory
{
    public static async Task<Dictionary<int, string>> NamesByCourseAsync(
        AppDbContext db, IReadOnlyCollection<int> courseIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        if (courseIds.Count == 0) return result;

        var ids = courseIds.ToList();
        // A doctor can be linked through either the legacy per-term
        // CourseInstructors row or the doctor-module CourseAssignments row
        // (imported data and the seeder write both, but older data may only
        // have one). Students must see the instructor regardless of source.
        var legacy = await db.CourseInstructors.AsNoTracking()
            .Where(ci => ids.Contains(ci.CourseId))
            .Select(ci => new { ci.CourseId, ci.DoctorUserId })
            .ToListAsync(ct);
        var assignments = await db.CourseAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.CourseId))
            .Select(a => new { a.CourseId, a.DoctorUserId })
            .ToListAsync(ct);

        var rows = legacy.Concat(assignments)
            .Distinct()
            .ToList();
        if (rows.Count == 0) return result;

        var doctorIds = rows.Select(r => r.DoctorUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => doctorIds.Contains(u.Id) && !u.IsDeleted)
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(ct);
        var nameById = names.ToDictionary(n => n.Id, n => n.FullName);

        foreach (var group in rows.GroupBy(r => r.CourseId))
        {
            var display = string.Join(", ", group
                .Select(r => nameById.GetValueOrDefault(r.DoctorUserId, ""))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct());
            result[group.Key] = display;
        }
        return result;
    }

    /// <summary>The doctor-editable display title per course, if one was set.
    /// Course ids always come from a scoped server-side query. The catalog
    /// Course.Title and Course.Code are never modified. Doctor-module
    /// CourseAssignment rows take precedence over the legacy per-term
    /// CourseInstructor rows so students always see the doctor's current
    /// title from either source.</summary>
    public static async Task<Dictionary<int, string>> DisplayTitlesByCourseAsync(
        AppDbContext db, IReadOnlyCollection<int> courseIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        if (courseIds.Count == 0) return result;

        var ids = courseIds.ToList();
        var rows = await db.CourseInstructors.AsNoTracking()
            .Where(ci => ids.Contains(ci.CourseId)
                && ci.DisplayTitle != null && ci.DisplayTitle != "")
            .OrderBy(ci => ci.Id)
            .Select(ci => new { ci.CourseId, ci.DisplayTitle })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            // First non-empty display title wins when a course has more than
            // one instructor; the catalog title remains the fallback.
            result.TryAdd(row.CourseId, row.DisplayTitle!.Trim());
        }

        // Doctor-module assignments win over legacy rows (latest first).
        var assignments = await db.CourseAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.CourseId) && a.DisplayTitle != null && a.DisplayTitle != "")
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new { a.CourseId, a.DisplayTitle })
            .ToListAsync(ct);
        foreach (var a in assignments)
            result[a.CourseId] = a.DisplayTitle!.Trim();

        return result;
    }
}
