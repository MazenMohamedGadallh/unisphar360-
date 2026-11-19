using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Central authorization checks for course-scoped data. A Doctor role check
/// alone is not enough: every course-specific read/write must confirm the
/// authenticated doctor is actually assigned to the requested course.
/// Admins bypass assignment checks (they retain their existing permissions).
/// The doctor identity is always resolved from the authenticated user id,
/// never from a client-supplied doctor id.
/// </summary>
public class DoctorAccessService
{
    private readonly AppDbContext _db;
    public DoctorAccessService(AppDbContext db) => _db = db;

    public Task<User?> GetUserAsync(int userId) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

    /// <summary>All course ids taught by the given doctor, from both the
    /// legacy per-term CourseInstructors rows and the doctor-module
    /// CourseAssignments rows. Imported/older installations may only have
    /// one of the two, so access checks must consider the union.</summary>
    public async Task<HashSet<int>> GetAssignedCourseIdsAsync(int doctorUserId)
    {
        var legacy = await _db.CourseInstructors.AsNoTracking()
            .Where(a => a.DoctorUserId == doctorUserId)
            .Select(a => a.CourseId)
            .ToListAsync();
        var module = await _db.CourseAssignments.AsNoTracking()
            .Where(a => a.DoctorUserId == doctorUserId)
            .Select(a => a.CourseId)
            .ToListAsync();
        return legacy.Concat(module).ToHashSet();
    }

    /// <summary>True when a course assignment exists for (doctorUserId, courseId)
    /// in either assignment source.</summary>
    public async Task<bool> IsAssignedToCourseAsync(int doctorUserId, int courseId) =>
        await _db.CourseAssignments.AsNoTracking()
            .AnyAsync(a => a.DoctorUserId == doctorUserId && a.CourseId == courseId)
        || await _db.CourseInstructors.AsNoTracking()
            .AnyAsync(ci => ci.DoctorUserId == doctorUserId && ci.CourseId == courseId);

    /// <summary>
    /// Resolves the authenticated caller and authorizes access to a course.
    /// Returns:
    ///  - Allowed: true for Admins (any course) and for Doctors assigned to it.
    ///  - NotFound: the course itself does not exist, OR the course belongs to
    ///    another university (university isolation: foreign ids stay
    ///    indistinguishable from missing ids, so never leak existence).
    ///  - Forbidden: the caller has no access (student or non-assigned doctor
    ///    of the same university).
    ///  - IsDoctor: whether the caller is a Doctor (so callers can 403 vs 404).
    /// </summary>
    public async Task<CourseAccessResult> AuthorizeCourseAsync(int userId, int courseId)
    {
        var user = await GetUserAsync(userId);
        if (user is null || user.IsDeleted) return CourseAccessResult.Unauthenticated();

        var course = await _db.Courses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return CourseAccessResult.NotFound(user);

        if (user.Role == UserRole.Admin)
            return CourseAccessResult.Allowed(user);

        if (user.Role != UserRole.Doctor)
            return CourseAccessResult.Forbidden(user);

        // University isolation (this project's rule): the doctor module's
        // assignment network never crosses universities. A course owned by a
        // different university resolves as 404, exactly like a missing id.
        // Legacy/unscoped rows (UniversityId null) stay reachable.
        if (course.UniversityId is not null
            && user.UniversityId is not null
            && course.UniversityId != user.UniversityId)
        {
            return CourseAccessResult.NotFound(user);
        }

        var assigned = await IsAssignedToCourseAsync(user.Id, courseId);
        return assigned ? CourseAccessResult.Allowed(user) : CourseAccessResult.Forbidden(user);
    }
}

public sealed record CourseAccessResult(string Status, User? User)
{
    public bool IsAllowed => Status == "Allowed";
    public bool IsNotFound => Status == "NotFound";
    public bool IsForbidden => Status == "Forbidden";
    public bool IsUnauthenticated => Status == "Unauthenticated";
    public bool IsAdmin => User?.Role == UserRole.Admin;
    public bool IsDoctor => User?.Role == UserRole.Doctor;

    public static CourseAccessResult Allowed(User user) => new("Allowed", user);
    public static CourseAccessResult NotFound(User? user) => new("NotFound", user);
    public static CourseAccessResult Forbidden(User? user) => new("Forbidden", user);
    public static CourseAccessResult Unauthenticated() => new("Unauthenticated", null);
}
