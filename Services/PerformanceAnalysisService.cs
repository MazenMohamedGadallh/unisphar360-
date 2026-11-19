using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>Builds the student performance analysis from real academic data
/// and the rule-based at-risk list used by the admin panel.</summary>
public class PerformanceAnalysisService
{
    private readonly AppDbContext _db;
    private readonly IPerformanceModel _model;
    private readonly PerformanceModelWeights _weights;
    public PerformanceAnalysisService(AppDbContext db, IPerformanceModel model, PerformanceModelWeights weights)
    {
        _db = db;
        _model = model;
        _weights = weights;
    }

    public async Task<PerformanceAnalysisDto?> AnalyzeAsync(int userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.IsDeleted) return null;

        var enrollments = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Course)
            .Where(e => e.UserId == userId && e.Semester == "Current")
            .OrderBy(e => e.Id)
            .ToListAsync();

        var records = await _db.SemesterRecords.AsNoTracking()
            .Where(r => r.UserId == userId).ToListAsync();
        var current = records.FirstOrDefault(r => r.IsCurrent);
        var previous = records.Where(r => !r.IsCurrent).OrderByDescending(r => r.Id).FirstOrDefault();

        var weekStart = AcademicService.StartOfWeek(DateTime.Today);
        var planItems = await _db.StudyPlanItems.AsNoTracking()
            .Where(i => i.UserId == userId && i.Date >= weekStart && i.Date < weekStart.AddDays(7))
            .ToListAsync();

        var avgPoints = enrollments.Count == 0 ? 0 : enrollments.Average(e => e.GradePoints);
        var avgAtt = enrollments.Count == 0 ? 0 : enrollments.Average(e => (double)e.AttendancePercent);
        var completion = planItems.Count == 0 ? 0 : (double)planItems.Count(i => i.IsDone) / planItems.Count;
        var hours = planItems.Sum(i => (double)i.Hours);
        var trend = current is null || previous is null ? 0 : Math.Round(current.Gpa - previous.Gpa, 2);
        var currentGpa = current?.Gpa ?? Math.Round(avgPoints, 2);

        var output = _model.Analyze(new PerformanceModelInput(
            Math.Round(avgPoints, 2), Math.Round(avgAtt, 1), Math.Round(completion, 3),
            hours, trend, enrollments.Count, previous is not null, planItems.Count > 0), currentGpa);

        var strengths = new List<string>();
        var weaknesses = new List<string>();
        var recommendations = new List<string>();
        var courseRisks = new List<PerformanceCourseRiskDto>();

        foreach (var e in enrollments)
        {
            var title = e.Course?.Title ?? $"Course {e.CourseId}";
            var status = AcademicService.ComputeStatus(e.GradePoints, e.AttendancePercent);
            var courseRisk = status == "Excellent" ? "OnTrack" : status == "Good" ? "NeedsAttention" : "AtRisk";
            var reason = status == "Excellent"
                ? $"{e.GradeLetter} with {e.AttendancePercent}% attendance."
                : status == "Good" && e.AttendancePercent < 90
                    ? $"Solid {e.GradeLetter}, room to push attendance from {e.AttendancePercent}%."
                    : status == "Good"
                        ? $"Solid {e.GradeLetter} — push toward an A- with focused revision."
                        : e.GradePoints < 3.0 && e.AttendancePercent < 90
                        ? $"Grade {e.GradeLetter} below B and attendance {e.AttendancePercent}% below 90%."
                        : e.GradePoints < 3.0
                            ? $"Grade {e.GradeLetter} below B."
                            : $"Attendance {e.AttendancePercent}% below 90%.";
            courseRisks.Add(new PerformanceCourseRiskDto(title, courseRisk, reason));

            if (status == "Excellent")
                strengths.Add($"{title}: {e.GradeLetter} with {e.AttendancePercent}% attendance.");
            else if (e.AttendancePercent < 90)
                weaknesses.Add($"{title}: Attendance {e.AttendancePercent}% below 90%.");
            else if (e.GradePoints < 3.0)
                weaknesses.Add($"{title}: Grade {e.GradeLetter} below B.");
            else
                weaknesses.Add($"{title}: {reason}");

            if (status != "Excellent" && recommendations.Count < 4)
            {
                if (e.GradePoints < 3.0)
                    recommendations.Add($"Add 2 hours/week for {title} (currently {e.GradeLetter}).");
                else if (e.AttendancePercent < 90)
                    recommendations.Add($"Raise attendance in {title} — every session counts toward your grade.");
                else
                    recommendations.Add($"Push {title} from {e.GradeLetter} toward an A- with focused revision.");
            }
        }

        if (completion < 0.7 && planItems.Count > 0)
        {
            var pending = planItems.Count(i => !i.IsDone);
            recommendations.Insert(0, $"Finish {pending} pending study-plan items to lift completion above 70%.");
        }
        if (hours < _weights.TargetStudyHoursPerWeek)
            recommendations.Add($"Block {Math.Ceiling(_weights.TargetStudyHoursPerWeek - hours)} more hours this week to hit the {_weights.TargetStudyHoursPerWeek:0.#}h target.");

        var predictedGpa = Math.Round(currentGpa + output.PredictedGpaDelta, 2);
        return new PerformanceAnalysisDto(
            output.RiskLevel, predictedGpa, output.Confidence, output.Score,
            strengths, weaknesses,
            recommendations.Take(4).ToList(),
            courseRisks, output.Factors);
    }

    /// <summary>Whether the user has any enrollments to score. Users without
    /// courses carry no signal, so they are left out of risk listings
    /// instead of being flagged on empty data.</summary>
    public async Task<bool> HasEnrollmentsAsync(int userId) =>
        await _db.Enrollments.AnyAsync(e => e.UserId == userId);

    /// <summary>Every active student/graduate with enrollment data, scored by
    /// the model and filtered to AtRisk, worst first.</summary>
    public async Task<IReadOnlyList<AtRiskStudentDto>> GetAtRiskStudentsAsync()
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && (u.Role == UserRole.Student || u.Role == UserRole.Graduate))
            .OrderBy(u => u.Id)
            .ToListAsync();

        var result = new List<AtRiskStudentDto>();
        foreach (var u in users)
        {
            if (!await HasEnrollmentsAsync(u.Id)) continue;
            var analysis = await AnalyzeAsync(u.Id);
            if (analysis is null || analysis.RiskLevel != "AtRisk") continue;
            result.Add(new AtRiskStudentDto(u.Id, u.FullName, u.Email,
                string.IsNullOrWhiteSpace(u.Department) ? u.Major : u.Department,
                analysis.RiskLevel, analysis.Score, analysis.PredictedGpa));
        }
        return result.OrderBy(r => r.Score).ToList();
    }

    public async Task<IReadOnlyList<GpaTrendPointDto>> GetGpaTrendAsync(int userId)
    {
        var records = await _db.SemesterRecords.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.Id)
            .ToListAsync();
        return records.Select(r => new GpaTrendPointDto(r.Label, r.Gpa)).ToList();
    }
}
