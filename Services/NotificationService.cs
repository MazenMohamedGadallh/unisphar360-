using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    public async Task<int> NotifyEnrolledStudentsAsync(
        int courseId, string title, string body, CancellationToken ct = default)
    {
        var userIds = await _db.Enrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId)
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync(ct);
        return await NotifyUsersAsync(userIds, title, body, ct);
    }

    public Task<int> NotifyUsersAsync(
        IEnumerable<int> userIds, string title, string body, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var userId in userIds.Distinct())
        {
            _db.Notifications.Add(new Notification { UserId = userId, Title = title, Body = body });
            count++;
        }
        return Task.FromResult(count);
    }
}
