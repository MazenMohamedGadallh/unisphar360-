using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// Read-only aggregation for the Doctor Module. Every metric is derived from
/// real, course-scoped database rows; nothing is fabricated. Missing grades
/// are handled explicitly: averages and distributions only consider
/// enrollments that actually have a grade letter, and return null / an
/// "Ungraded" bucket otherwise.
/// </summary>
public class DoctorService
{
    private readonly AppDbContext _db;
    public DoctorService(AppDbContext db) => _db = db;

    public async Task<DoctorDashboardDto> GetDashboardAsync(User doctor)
    {
        var moduleIds = await _db.CourseAssignments.AsNoTracking()
            .Where(a => a.DoctorUserId == doctor.Id)
            .Select(a => a.CourseId)
            .ToListAsync();
        var legacyIds = await _db.CourseInstructors.AsNoTracking()
            .Where(ci => ci.DoctorUserId == doctor.Id)
            .Select(ci => ci.CourseId)
            .ToListAsync();
        var courseIds = moduleIds.Concat(legacyIds).Distinct().ToList();

        var courses = await _db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .OrderBy(c => c.Code)
            .ToListAsync();

        var enrollments = await _db.Enrollments.AsNoTracking()
            .Where(e => courseIds.Contains(e.CourseId))
            .ToListAsync();

        var courseSummaries = courses
            .Select(c => BuildCourseSummary(c, enrollments.Where(e => e.CourseId == c.Id).ToList()))
            .ToList();

        var graded = enrollments.Where(HasGrade).ToList();
        double? avgPoints = graded.Count == 0 ? null : Math.Round(graded.Average(e => e.GradePoints), 2);
        double? avgAtt = enrollments.Count == 0 ? null : Math.Round(enrollments.Average(e => (double)e.AttendancePercent), 1);

        var alerts = await BuildAlertsAsync(enrollments);

        var announcements = await _db.Announcements.AsNoTracking()
            .Include(a => a.Course)
            .Where(a => a.CourseId != null && courseIds.Contains(a.CourseId.Value))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(5)
            .Select(a => new DoctorAnnouncementDto(
                a.Id, a.CourseId, a.Course != null ? a.Course.Title : null,
                a.Title, a.Body, a.Category, a.CreatedAtUtc))
            .ToListAsync();

        return new DoctorDashboardDto(
            doctor.FullName,
            courses.Count,
            enrollments.Select(e => e.UserId).Distinct().Count(),
            avgPoints,
            avgAtt,
            courseSummaries,
            alerts,
            announcements);
    }

    private static DoctorCourseSummaryDto BuildCourseSummary(Course course, List<Enrollment> rows)
    {
        var graded = rows.Where(HasGrade).ToList();
        return new DoctorCourseSummaryDto(
            course.Id, course.Code, course.Title, course.Track, course.Credits, course.Semester,
            rows.Select(e => e.UserId).Distinct().Count(),
            graded.Count == 0 ? null : Math.Round(graded.Average(e => e.GradePoints), 2),
            rows.Count == 0 ? null : Math.Round(rows.Average(e => (double)e.AttendancePercent), 1));
    }

    public async Task<IReadOnlyList<DoctorStudentDto>> GetStudentsAsync(int courseId)
    {
        var rows = await _db.Enrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId)
            .Join(_db.Users.AsNoTracking(), e => e.UserId, u => u.Id, (e, u) => new { e, u })
            .OrderBy(x => x.u.FullName)
            .Select(x => new DoctorStudentDto(
                x.e.Id, x.u.Id, x.u.FullName, x.u.Email, x.u.Department, x.u.AcademicYear,
                x.e.GradeLetter, x.e.GradePoints, x.e.AttendancePercent, x.e.Status,
                x.u.Major ?? ""))
            .ToListAsync();
        return rows;
    }

    public async Task<DoctorCourseAnalyticsDto> GetCourseAnalyticsAsync(Course course)
    {
        var rows = await _db.Enrollments.AsNoTracking()
            .Where(e => e.CourseId == course.Id)
            .ToListAsync();

        var graded = rows.Where(HasGrade).ToList();

        var gradeBuckets = new List<GradeBucketDto>
        {
            new("A (3.7-4.0)", graded.Count(e => e.GradePoints >= 3.7)),
            new("B (3.0-3.69)", graded.Count(e => e.GradePoints >= 3.0 && e.GradePoints < 3.7)),
            new("C (2.0-2.99)", graded.Count(e => e.GradePoints >= 2.0 && e.GradePoints < 3.0)),
            new("D/F (<2.0)", graded.Count(e => e.GradePoints < 2.0)),
            new("Ungraded", rows.Count - graded.Count),
        };

        var attendanceBuckets = new List<AttendanceBucketDto>
        {
            new("90-100%", rows.Count(e => e.AttendancePercent >= 90)),
            new("80-89%", rows.Count(e => e.AttendancePercent >= 80 && e.AttendancePercent < 90)),
            new("70-79%", rows.Count(e => e.AttendancePercent >= 70 && e.AttendancePercent < 80)),
            new("Below 70%", rows.Count(e => e.AttendancePercent < 70)),
        };

        var alerts = await BuildAlertsAsync(rows);

        return new DoctorCourseAnalyticsDto(
            course.Id, course.Code, course.Title,
            rows.Count, graded.Count,
            graded.Count == 0 ? null : Math.Round(graded.Average(e => e.GradePoints), 2),
            rows.Count == 0 ? null : Math.Round(rows.Average(e => (double)e.AttendancePercent), 1),
            gradeBuckets.Select(b => b with { Count = b.Count }).ToList(),
            attendanceBuckets,
            alerts);
    }

    /// <summary>Advisory, explainable alerts. Never punitive: they only flag
    /// students whose real grade/attendance data suggests extra support.</summary>
    private async Task<IReadOnlyList<DoctorAlertDto>> BuildAlertsAsync(IReadOnlyCollection<Enrollment> rows)
    {
        var alerts = new List<DoctorAlertDto>();
        if (rows.Count == 0) return alerts;

        var userIds = rows.Select(e => e.UserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        foreach (var e in rows)
        {
            var name = names.TryGetValue(e.UserId, out var n) ? n : $"Student {e.UserId}";
            string? reason = null;
            var severity = "Medium";

            if (e.AttendancePercent < 70)
            {
                reason = $"Attendance {e.AttendancePercent}% is critically low.";
                severity = "High";
            }
            else if (HasGrade(e) && e.GradePoints < 2.0)
            {
                reason = $"Grade {e.GradeLetter} is below a passing threshold.";
                severity = "High";
            }
            else if (e.AttendancePercent < 85)
            {
                reason = $"Attendance {e.AttendancePercent}% is below 85%.";
            }
            else if (HasGrade(e) && e.GradePoints < 3.0)
            {
                reason = $"Grade {e.GradeLetter} is below B — may benefit from support.";
            }

            if (reason is not null)
                alerts.Add(new DoctorAlertDto(e.UserId, e.Id, name, reason, severity));
        }

        return alerts
            .OrderByDescending(a => a.Severity == "High")
            .ThenBy(a => a.StudentName)
            .ToList();
    }

    private static bool HasGrade(Enrollment e) => !string.IsNullOrWhiteSpace(e.GradeLetter);
}
