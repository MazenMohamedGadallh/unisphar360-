using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public record StudyPlanCourseInput(
    string Course, double GradePoints, int AttendancePercent, int Credits, double ClassAverageGradePoints);

public class StudyPlanGenerator
{
    public const string PlanNote = "AI study plan";
    private const int MaxHoursPerDay = 4;
    private const int StudyDays = 6; // one free day per week

    private readonly AppDbContext _db;
    private readonly PerformanceModelWeights _weights;

    public StudyPlanGenerator(AppDbContext db, PerformanceModelWeights weights)
    {
        _db = db;
        _weights = weights;
    }

    public static IReadOnlyList<StudyPlanCourseDto> Allocate(IReadOnlyList<StudyPlanCourseInput> courses, int totalHours)
    {
        if (courses.Count == 0) return Array.Empty<StudyPlanCourseDto>();
        totalHours = Math.Max(totalHours, courses.Count);

        var scored = courses.Select(c =>
        {
            var weakness = Math.Clamp((4.0 - c.GradePoints) / 4.0, 0, 1);
            var difficulty = Math.Clamp((4.0 - c.ClassAverageGradePoints) / 4.0, 0, 1);
            var attendanceGap = Math.Clamp((90 - c.AttendancePercent) / 90.0, 0, 1);
            var need = Math.Max(c.Credits, 1) / 3.0 * (0.1 + 0.5 * weakness + 0.3 * difficulty + 0.2 * attendanceGap);
            return (Input: c, Need: need, Weakness: weakness, Difficulty: difficulty, AttendanceGap: attendanceGap);
        }).ToList();

        // 1 hour each, then hand out the rest by largest remaining share.
        var spare = totalHours - scored.Count;
        var totalNeed = scored.Sum(s => s.Need);
        var shares = scored.Select(s => spare * s.Need / totalNeed).ToList();
        var hours = shares.Select(x => 1 + (int)Math.Floor(x)).ToList();
        var left = totalHours - hours.Sum();
        foreach (var i in Enumerable.Range(0, scored.Count)
                     .OrderByDescending(i => shares[i] - Math.Floor(shares[i])).Take(left))
            hours[i]++;

        var maxNeed = scored.Max(s => s.Need);
        return scored.Select((s, i) =>
        {
            var priority = s.Need >= maxNeed * 0.75 ? "High" : s.Need >= maxNeed * 0.4 ? "Medium" : "Low";
            var reasons = new List<string> { $"your grade is {s.Input.GradePoints:0.0}/4" };
            if (s.Difficulty >= 0.25) reasons.Add($"it is hard for the class (average {s.Input.ClassAverageGradePoints:0.0}/4)");
            if (s.AttendanceGap > 0) reasons.Add($"attendance is {s.Input.AttendancePercent}%");
            if (s.Input.Credits > 3) reasons.Add($"{s.Input.Credits} credit hours");
            var reason = priority == "Low"
                ? $"Light weekly review: your grade is {s.Input.GradePoints:0.0}/4 and the course is not hard for the class."
                : "More time because " + string.Join(", ", reasons) + ".";
            return new StudyPlanCourseDto(s.Input.Course, hours[i], priority,
                Math.Round(s.Difficulty * 100), reason);
        })
        .Select((dto, i) => (Dto: dto, Need: scored[i].Need))
        .OrderByDescending(x => x.Dto.WeeklyHours).ThenByDescending(x => x.Need)
        .Select(x => x.Dto)
        .ToList();
    }

    public static IReadOnlyList<StudyPlanSessionDto> Schedule(IReadOnlyList<StudyPlanCourseDto> courses, DateTime weekStart)
    {
        var blocks = new List<(string Course, int Hours)>();
        foreach (var c in courses)
        {
            var remaining = c.WeeklyHours;
            while (remaining > 0)
            {
                var size = remaining >= 2 ? 2 : 1;
                blocks.Add((c.Course, size));
                remaining -= size;
            }
        }

        var load = new int[StudyDays];
        var sessions = new List<StudyPlanSessionDto>();
        var day = 0;
        foreach (var block in blocks)
        {
            // Next day with room, skipping a day that already has this course.
            for (var tries = 0; tries < StudyDays * 2; tries++, day = (day + 1) % StudyDays)
            {
                var sameCourse = sessions.Any(s => s.Course == block.Course && s.Date == weekStart.AddDays(day));
                if (load[day] + block.Hours <= MaxHoursPerDay && (!sameCourse || tries >= StudyDays)) break;
            }
            load[day] += block.Hours;
            sessions.Add(new StudyPlanSessionDto(weekStart.AddDays(day), block.Course, block.Hours));
            day = (day + 1) % StudyDays;
        }
        return sessions.OrderBy(s => s.Date).ThenByDescending(s => s.Hours).ToList();
    }

    public async Task<StudyPlanSuggestionDto> SuggestAsync(int userId)
    {
        var enrollments = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Course)
            .Where(e => e.UserId == userId && e.Semester == "Current")
            .ToListAsync();

        var courseIds = enrollments.Select(e => e.CourseId).ToList();
        var classAverages = await _db.Enrollments.AsNoTracking()
            .Where(e => courseIds.Contains(e.CourseId) && e.GradePoints > 0)
            .GroupBy(e => e.CourseId)
            .Select(g => new { g.Key, Avg = g.Average(e => e.GradePoints) })
            .ToDictionaryAsync(x => x.Key, x => x.Avg);

        var inputs = enrollments.Select(e => new StudyPlanCourseInput(
            e.Course?.Title ?? $"Course {e.CourseId}",
            e.GradePoints,
            e.AttendancePercent,
            e.Course?.Credits ?? 3,
            classAverages.TryGetValue(e.CourseId, out var avg) ? avg : e.GradePoints)).ToList();

        var totalHours = (int)Math.Round(_weights.TargetStudyHoursPerWeek);
        var courses = Allocate(inputs, totalHours);
        // The next 7 days from today, so no session lands on a day that already passed.
        var start = DateTime.Today;
        return new StudyPlanSuggestionDto(start, courses.Sum(c => c.WeeklyHours), courses, Schedule(courses, start));
    }

    public async Task<int> ApplyAsync(int userId)
    {
        var plan = await SuggestAsync(userId);
        var end = plan.WeekStart.AddDays(7);
        var old = await _db.StudyPlanItems
            .Where(i => i.UserId == userId && i.Date >= plan.WeekStart && i.Date < end
                        && i.Note == PlanNote && !i.IsDone)
            .ToListAsync();
        _db.StudyPlanItems.RemoveRange(old);
        _db.StudyPlanItems.AddRange(plan.Sessions.Select(s => new StudyPlanItem
        {
            UserId = userId,
            Date = s.Date,
            Title = $"Study {s.Course}",
            Hours = s.Hours,
            Note = PlanNote
        }));
        await _db.SaveChangesAsync();
        return plan.Sessions.Count;
    }
}
