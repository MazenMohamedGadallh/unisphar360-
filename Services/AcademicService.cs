using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>Builds every block of the Academic Advisor screen.</summary>
public class AcademicService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly AppDbContext _db;
    public AcademicService(AppDbContext db) => _db = db;

    public static readonly IReadOnlyList<QuickActionDto> QuickActions = new List<QuickActionDto>
    {
        new("check-grades", "Check My Grades", "fact_check", "/academic/grades", "#EDEBFF", "#4A3FD0"),
        new("plan-study", "Plan My Study", "event_note", "/study-planner", "#FFE8E6", "#E0524A"),
        new("course-recommendations", "Get Course Recommendations", "school", "/courses/recommended", "#E6F8EE", "#1E9E5E"),
        new("academic-insights", "View Academic Insights", "lightbulb", "/academic/insights", "#FFF4D9", "#E09A1A"),
    };

    /// <summary>Weeks start on Sunday (matches the design).</summary>
    public static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-(int)d.DayOfWeek);

    public static string TimeAgo(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} minutes ago";
        if (span.TotalHours < 24)
        {
            var h = (int)span.TotalHours;
            return h == 1 ? "1 hour ago" : $"{h} hours ago";
        }
        var days = (int)span.TotalDays;
        return days == 1 ? "Yesterday" : $"{days} days ago";
    }

    public static StudyDayDto ToDto(StudyPlanItem i) =>
        new(i.Id, i.Date, i.Date.ToString("ddd", Inv), i.Date.Day, i.Title, i.Hours, i.Note, i.IsDone);

    public static string GreetingFor(int hour) =>
        hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";

    /// <summary>Status rule for the dashboard performance table:
    /// attendance below 90% or grade below B (3.0) needs attention;
    /// A- or better (3.7+) with attendance 90%+ is excellent.</summary>
    public static string ComputeStatus(double gradePoints, int attendancePercent)
    {
        if (attendancePercent < 90 || gradePoints < 3.0) return "Needs Attention";
        if (gradePoints >= 3.7 && attendancePercent >= 90) return "Excellent";
        return "Good";
    }

    // Semester value stored on every Enrollment ("Current"). Filter
    // case-insensitively so hand-written values like "current" still match.
    private const string CurrentSemester = "current";

    /// <summary>Institutional 4.0 scale (matches the seeded demo records:
    /// B+ = 3.3, A- = 3.7, ...). Used when a letter is assigned without
    /// explicit points so GPA and letter always agree.</summary>
    public static double LetterToPoints(string letter) => letter.Trim().ToUpperInvariant() switch
    {
        "A" => 4.0,
        "A-" => 3.7,
        "B+" => 3.3,
        "B" => 3.0,
        "B-" => 2.7,
        "C+" => 2.3,
        "C" => 2.0,
        "C-" => 1.7,
        "D+" => 1.3,
        "D" => 1.0,
        _ => 0.0
    };

    public async Task<int> GetUnreadCountAsync(int userId) =>
        await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task<DashboardSummaryDto?> GetDashboardSummaryAsync(int userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;
        var stats = await GetStatsAsync(userId);
        if (stats is null) return null;

        return new DashboardSummaryDto(
            user.FullName, user.FirstName, GreetingFor(DateTime.Now.Hour),
            stats.Gpa, stats.GpaDelta,
            stats.CompletedCourses, stats.TotalCourses, stats.CompletedPercent,
            stats.AttendanceRate, stats.AttendanceDelta,
            stats.SubjectsToImprove, stats.RecommendedCourses,
            await GetUnreadCountAsync(userId));
    }

    public async Task<IReadOnlyList<DashboardPerformanceRowDto>> GetDashboardPerformanceAsync(int userId, string? semester)
    {
        var sem = string.IsNullOrWhiteSpace(semester) ? CurrentSemester : semester.Trim().ToLowerInvariant();
        var rows = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Course)
            .Where(e => e.UserId == userId && e.Semester.ToLower() == sem)
            .OrderBy(e => e.Id)
            .ToListAsync();

        return rows.Select(e => new DashboardPerformanceRowDto(
            e.CourseId,
            e.Course?.Title ?? "",
            e.GradeLetter,
            e.GradePoints,
            e.AttendancePercent,
            ComputeStatus(e.GradePoints, e.AttendancePercent))).ToList();
    }

    public static DashboardStudyDayDto ToDashboardDayDto(StudyPlanItem i) =>
        new(i.Id, i.Date, i.Date.ToString("ddd", Inv), i.Title, i.Hours, i.IsDone, i.Hours == 0);

    /// <summary>Always exactly 7 days (Sunday..Saturday). Days without a
    /// stored item come back as free days so the UI never has gaps.</summary>
    public async Task<DashboardStudyPlanDto> GetDashboardStudyPlanAsync(int userId, DateTime? weekStart = null)
    {
        var start = StartOfWeek(weekStart ?? DateTime.Today);
        var end = start.AddDays(7);

        var items = await _db.StudyPlanItems.AsNoTracking()
            .Where(i => i.UserId == userId && i.Date >= start && i.Date < end)
            .OrderBy(i => i.Date).ThenBy(i => i.Id)
            .ToListAsync();

        var days = new List<DashboardStudyDayDto>(7);
        for (var d = 0; d < 7; d++)
        {
            var date = start.AddDays(d);
            var item = items.FirstOrDefault(i => i.Date.Date == date);
            days.Add(item is not null
                ? ToDashboardDayDto(item)
                : new DashboardStudyDayDto(0, date, date.ToString("ddd", Inv), "Free day", 0, false, true));
        }

        var from = start.ToString("MMM d", Inv);
        var to = end.AddDays(-1).ToString("MMM d", Inv);
        return new DashboardStudyPlanDto($"{from} - {to}", start, days);
    }

    public async Task<DashboardStudyDayDto?> ToggleDashboardStudyDayAsync(int userId, int id)
    {
        var item = await _db.StudyPlanItems.FindAsync(id);
        if (item is null || item.UserId != userId) return null;
        item.IsDone = !item.IsDone;
        await _db.SaveChangesAsync();
        return ToDashboardDayDto(item);
    }

    /// <summary>Three rule-based cards generated from the student's real
    /// grades and attendance: a warning, a progress note, a recommendation.</summary>
    public async Task<IReadOnlyList<DashboardInsightDto>> GetDashboardInsightsAsync(int userId)
    {
        var insights = new List<DashboardInsightDto>();

        var rows = await GetDashboardPerformanceAsync(userId, "Current");
        // Focus on graded courses only: an ungraded (in-progress) course has
        // no grade yet, so "grade ()" warnings would be nonsense.
        var weakest = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.GradeLetter))
            .OrderBy(r => r.GradePoints).ThenBy(r => r.AttendancePercent)
            .FirstOrDefault()
            ?? rows.OrderBy(r => r.AttendancePercent).FirstOrDefault();
        if (weakest is not null && (weakest.GradePoints < 3.0 || weakest.AttendancePercent < 90))
        {
            var gradePart = string.IsNullOrWhiteSpace(weakest.GradeLetter)
                ? "in progress"
                : $"grade ({weakest.GradeLetter})";
            insights.Add(new DashboardInsightDto("Warning", $"{weakest.CourseName} needs attention",
                $"Your {gradePart} and attendance ({weakest.AttendancePercent}%) in {weakest.CourseName} are below target. A focused study block this week can help.",
                "View study plan", "/study-planner"));
        }

        var stats = await GetStatsAsync(userId);
        if (stats is not null && stats.AttendanceDelta > 0)
        {
            insights.Add(new DashboardInsightDto("Progress", "Attendance improving",
                $"Your attendance is up {stats.AttendanceDelta}% vs last semester ({stats.AttendanceRate}% overall). Keep the streak going.",
                null, null));
        }
        else if (stats is not null && stats.GpaDelta > 0)
        {
            insights.Add(new DashboardInsightDto("Progress", "GPA on the rise",
                $"Your GPA improved by {stats.GpaDelta:0.00} vs last semester and now stands at {stats.Gpa:0.00}.",
                null, null));
        }

        var top = (await GetRecommendedAsync(userId, 1)).FirstOrDefault();
        if (top is not null)
        {
            insights.Add(new DashboardInsightDto("Recommendation", $"Try {top.Title}",
                $"{top.Title} ({top.Code}) matches your {top.Track} track and fits your current progress.",
                "View course", $"/courses/{top.Id}"));
        }

        return insights;
    }

    public async Task<IReadOnlyList<DashboardRecommendedCourseDto>> GetDashboardRecommendedAsync(int userId, int take = 4)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new List<DashboardRecommendedCourseDto>();

        var track = Recommender.TrackFor(user.Major);
        var courses = await GetRecommendedAsync(userId, take);
        return courses.Select(c => new DashboardRecommendedCourseDto(
            c.Id, c.Title, c.Track, c.Credits,
            c.Track == track ? "Matches your major" : "Trending")).ToList();
    }

    public async Task<IReadOnlyList<DashboardActivityDto>> GetDashboardActivityAsync(int userId, int take = 4)
    {
        var items = await _db.Activities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync();

        return items.Select(a => new DashboardActivityDto(a.Id, a.Message, a.CreatedAtUtc)).ToList();
    }

    public async Task<AcademicStats?> GetStatsAsync(int userId)
    {
        var records = await _db.SemesterRecords.AsNoTracking().Where(r => r.UserId == userId).ToListAsync();
        var current = records.FirstOrDefault(r => r.IsCurrent);
        if (current is null) return null;
        var previous = records.Where(r => !r.IsCurrent).OrderByDescending(r => r.Id).FirstOrDefault();

        var enrollments = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId && e.Semester.ToLower() == CurrentSemester).ToListAsync();
        var toImprove = enrollments.Count(e => e.GradePoints <= 3.0);
        var recommended = await GetRecommendedAsync(userId, 4);

        var percent = current.TotalCourses == 0
            ? 0
            : (int)Math.Round(100.0 * current.CompletedCourses / current.TotalCourses);

        var gpaDelta = Math.Round(current.Gpa - (previous?.Gpa ?? current.Gpa), 2);
        var attDelta = current.AttendanceRate - (previous?.AttendanceRate ?? current.AttendanceRate);

        return new AcademicStats(
            current.Gpa, gpaDelta, 4.0,
            current.CompletedCourses, current.TotalCourses, percent,
            current.AttendanceRate, attDelta,
            toImprove, recommended.Count);
    }

    public async Task<IReadOnlyList<GradeRowDto>> GetPerformanceAsync(int userId)
    {
        var rows = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Course)
            .Where(e => e.UserId == userId && e.Semester.ToLower() == CurrentSemester)
            .OrderBy(e => e.Id)
            .ToListAsync();

        return rows.Select(e => new GradeRowDto(
            e.CourseId,
            e.Course?.Title ?? "",
            e.GradeLetter,
            e.GradePoints,
            $"{e.GradeLetter} ({e.GradePoints.ToString("0.0", Inv)})",
            e.AttendancePercent,
            ComputeStatus(e.GradePoints, e.AttendancePercent))).ToList();
    }

    /// <summary>Courses the student is not enrolled in, ranked by their track.</summary>
    public async Task<IReadOnlyList<CourseDto>> GetRecommendedAsync(int userId, int take = 4)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new List<CourseDto>();

        var enrolledIds = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId).Select(e => e.CourseId).ToListAsync();

        var courses = await _db.Courses.AsNoTracking()
            .Where(c => !enrolledIds.Contains(c.Id)).ToListAsync();

        return Recommender.Rank(courses, user)
            .Take(take)
            .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.Description, c.Track, c.Credits))
            .ToList();
    }

    public async Task<IReadOnlyList<InsightDto>> GetInsightsAsync(int userId)
    {
        var insights = new List<InsightDto>();

        var rows = await GetPerformanceAsync(userId);
        var weakest = rows.OrderBy(r => r.GradePoints).ThenBy(r => r.Attendance).FirstOrDefault();
        if (weakest is not null && weakest.GradePoints <= 3.0)
        {
            insights.Add(new InsightDto("focus", $"Focus on {weakest.Course}",
                $"Your grade in {weakest.Course} is below average. Consider dedicating more time to practice and revision.",
                "View Study Plan", "/study-planner"));
        }

        var stats = await GetStatsAsync(userId);
        if (stats is not null && stats.AttendanceDelta > 0)
        {
            insights.Add(new InsightDto("progress", "Good Progress!",
                $"Your attendance rate has improved by {stats.AttendanceDelta}% compared to last semester. Keep it up!",
                null, null));
        }

        var top = (await GetRecommendedAsync(userId, 1)).FirstOrDefault();
        if (top is not null)
        {
            insights.Add(new InsightDto("recommendation", "Recommended Course",
                $"Based on your performance, you may benefit from {top.Title}, which matches your interests and progress.",
                "View Details", $"/courses/{top.Id}"));
        }

        return insights;
    }

    public async Task<StudyPlannerResponse> GetPlannerAsync(int userId, DateTime? weekStart = null)
    {
        var start = StartOfWeek(weekStart ?? DateTime.Today);
        var end = start.AddDays(7);

        var items = await _db.StudyPlanItems.AsNoTracking()
            .Where(i => i.UserId == userId && i.Date >= start && i.Date < end)
            .OrderBy(i => i.Date).ThenBy(i => i.Id)
            .ToListAsync();

        var from = start.ToString("MMM d", Inv);
        var to = end.AddDays(-1).ToString("MMM d", Inv);
        return new StudyPlannerResponse($"{from} - {to}", start, items.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<ActivityDto>> GetActivityAsync(int userId, int take = 10)
    {
        var items = await _db.Activities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync();

        return items.Select(a => new ActivityDto(a.Id, a.Kind, a.Message, a.CreatedAtUtc, TimeAgo(a.CreatedAtUtc))).ToList();
    }
}
