using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public sealed record UniversityScopeSnapshot(int UserId, UserRole? Role, int? UniversityId)
{
    public bool IsAuthenticated => UserId > 0 && Role.HasValue;
    public bool IsAdmin => Role == UserRole.Admin;
    public bool HasUniversity => UniversityId.HasValue;
}

public interface IUniversityScope
{
    Task<UniversityScopeSnapshot> GetAsync(int? userId = null, CancellationToken ct = default);
}

public sealed class UniversityScope : IUniversityScope
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private UniversityScopeSnapshot? _cached;
    private int? _cachedUserId;

    public UniversityScope(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task<UniversityScopeSnapshot> GetAsync(int? userId = null, CancellationToken ct = default)
    {
        var id = userId ?? _http.HttpContext?.User.GetUserId() ?? 0;

        if (_cached is not null && _cachedUserId == id)
            return _cached;

        if (id <= 0)
            return _cached = new UniversityScopeSnapshot(0, null, null);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == id && !u.IsDeleted && u.IsActive)
            .Select(u => new { u.Id, u.Role, u.UniversityId })
            .FirstOrDefaultAsync(ct);

        _cachedUserId = id;
        _cached = user is null
            ? new UniversityScopeSnapshot(0, null, null)
            : new UniversityScopeSnapshot(user.Id, user.Role, user.UniversityId);
        return _cached;
    }
}

public static class UniversityScopeExtensions
{
    public static IQueryable<Course> VisibleToCurrentUser(
        this IQueryable<Course> query, UniversityScopeSnapshot scope)
    {
        if (scope.IsAdmin) return query;
        if (!scope.UniversityId.HasValue) return query.Where(_ => false);
        var universityId = scope.UniversityId.Value;
        return query.Where(c => c.UniversityId == universityId || c.UniversityId == null);
    }

    public static IQueryable<EventItem> VisibleToCurrentUser(
        this IQueryable<EventItem> query, UniversityScopeSnapshot scope)
    {
        if (scope.IsAdmin) return query;
        if (!scope.UniversityId.HasValue) return query.Where(_ => false);
        var universityId = scope.UniversityId.Value;
        return query.Where(e => e.UniversityId == universityId || e.UniversityId == null);
    }

    public static IQueryable<Announcement> VisibleToCurrentUser(
        this IQueryable<Announcement> query, UniversityScopeSnapshot scope)
    {
        if (scope.IsAdmin) return query;
        if (!scope.UniversityId.HasValue) return query.Where(_ => false);
        var universityId = scope.UniversityId.Value;
        return query.Where(a => a.UniversityId == universityId || a.UniversityId == null);
    }

    public static IQueryable<CourseMaterial> VisibleToCurrentUser(
        this IQueryable<CourseMaterial> query, UniversityScopeSnapshot scope)
    {
        if (scope.IsAdmin) return query;
        if (!scope.UniversityId.HasValue) return query.Where(_ => false);
        var universityId = scope.UniversityId.Value;
        return query.Where(m => m.Course != null && m.Course.UniversityId == universityId);
    }

    public static IQueryable<Enrollment> VisibleToCurrentUser(
        this IQueryable<Enrollment> query, UniversityScopeSnapshot scope)
    {
        if (scope.IsAdmin) return query;
        if (!scope.UniversityId.HasValue) return query.Where(_ => false);
        var universityId = scope.UniversityId.Value;
        return query.Where(e => e.Course != null && e.Course.UniversityId == universityId);
    }

    public static IQueryable<Course> EligibleForStudent(this IQueryable<Course> query, User user) =>
        query.Where(c =>
            (string.IsNullOrEmpty(user.Department) || c.Department == "" || c.Department == user.Department)
            && (!user.AcademicYear.HasValue || !c.AcademicYear.HasValue || c.AcademicYear == user.AcademicYear));

    public static IQueryable<Course> AssignedToDoctor(
        this IQueryable<Course> query, AppDbContext db, int doctorUserId) =>
        query.Where(c =>
            db.CourseAssignments.Any(a => a.CourseId == c.Id && a.DoctorUserId == doctorUserId)
            || db.CourseInstructors.Any(ci => ci.CourseId == c.Id && ci.DoctorUserId == doctorUserId));
}
