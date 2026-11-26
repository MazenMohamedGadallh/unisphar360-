using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class DoctorAccessService
{
    private readonly AppDbContext _db;
    public DoctorAccessService(AppDbContext db) => _db = db;

    public Task<User?> GetUserAsync(int userId) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

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

    public async Task<bool> IsAssignedToCourseAsync(int doctorUserId, int courseId) =>
        await _db.CourseAssignments.AsNoTracking()
            .AnyAsync(a => a.DoctorUserId == doctorUserId && a.CourseId == courseId)
        || await _db.CourseInstructors.AsNoTracking()
            .AnyAsync(ci => ci.DoctorUserId == doctorUserId && ci.CourseId == courseId);

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
