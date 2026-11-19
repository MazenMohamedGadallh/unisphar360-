using UniSphere.Api.Dtos;

namespace UniSphere.Api.Services;

/// <summary>All knobs of the performance model in one place.</summary>
public class PerformanceModelWeights
{
    public double GradeWeight { get; set; } = 0.40;
    public double AttendanceWeight { get; set; } = 0.25;
    public double PlanCompletionWeight { get; set; } = 0.20;
    public double StudyHoursWeight { get; set; } = 0.10;
    public double TrendWeight { get; set; } = 0.05;
    public double AtRiskThreshold { get; set; } = 45;
    public double NeedsAttentionThreshold { get; set; } = 70;
    public double TargetStudyHoursPerWeek { get; set; } = 14;
    public double ExcellentGradePoints { get; set; } = 3.7;
}

/// <summary>Normalized inputs for the model (all real student data).</summary>
public record PerformanceModelInput(
    double AvgGradePoints,
    double AvgAttendance,
    double PlanCompletionRate,
    double StudyHoursPerWeek,
    double GpaTrend,
    int CourseCount,
    bool HasTrend,
    bool HasPlan);

/// <summary>Transparent model output: score, label and what drove it.</summary>
public record PerformanceModelOutput(
    string RiskLevel,
    double Score,
    double PredictedGpaDelta,
    double Confidence,
    IReadOnlyList<FactorContributionDto> Factors);

/// <summary>Implemented today by a weighted explainable model; a trained
/// ML.NET model can replace it later without touching callers.</summary>
public interface IPerformanceModel
{
    PerformanceModelOutput Analyze(PerformanceModelInput input, double currentGpa);
}

/// <summary>Weighted scoring + trend-adjusted GPA projection. Every factor is
/// normalized to 0-100 first, so contributions are directly comparable.</summary>
public class WeightedPerformanceModel : IPerformanceModel
{
    private readonly PerformanceModelWeights _w;
    public WeightedPerformanceModel(PerformanceModelWeights weights) => _w = weights;

    public PerformanceModelOutput Analyze(PerformanceModelInput input, double currentGpa)
    {
        var grade = Math.Clamp(input.AvgGradePoints / 4.0 * 100.0, 0, 100);
        var attendance = Math.Clamp(input.AvgAttendance, 0, 100);
        var plan = Math.Clamp(input.PlanCompletionRate * 100.0, 0, 100);
        var hours = Math.Clamp(input.StudyHoursPerWeek / _w.TargetStudyHoursPerWeek * 100.0, 0, 100);
        var trend = Math.Clamp(50 + input.GpaTrend * 25.0, 0, 100);

        var factors = new List<FactorContributionDto>
        {
            new("Grades", _w.GradeWeight, Math.Round(grade, 1), Math.Round(_w.GradeWeight * grade, 1),
                $"Average {input.AvgGradePoints:0.00} grade points across {input.CourseCount} courses."),
            new("Attendance", _w.AttendanceWeight, Math.Round(attendance, 1), Math.Round(_w.AttendanceWeight * attendance, 1),
                $"Average attendance of {input.AvgAttendance:0}% this semester."),
            new("Study-plan completion", _w.PlanCompletionWeight, Math.Round(plan, 1), Math.Round(_w.PlanCompletionWeight * plan, 1),
                input.HasPlan ? $"{plan:0}% of this week's plan is done." : "No study-plan items this week yet."),
            new("Study hours", _w.StudyHoursWeight, Math.Round(hours, 1), Math.Round(_w.StudyHoursWeight * hours, 1),
                $"{input.StudyHoursPerWeek:0.#} of {_w.TargetStudyHoursPerWeek:0.#} target hours planned this week."),
            new("GPA trend", _w.TrendWeight, Math.Round(trend, 1), Math.Round(_w.TrendWeight * trend, 1),
                input.HasTrend ? $"GPA moved {(input.GpaTrend >= 0 ? "+" : "")}{input.GpaTrend:0.00} vs last semester." : "Only one semester on record so far.")
        };

        var score = Math.Round(factors.Sum(f => f.Contribution), 1);
        var risk = score < _w.AtRiskThreshold ? "AtRisk"
            : score < _w.NeedsAttentionThreshold ? "NeedsAttention" : "OnTrack";
        var predicted = Math.Clamp(currentGpa + input.GpaTrend * 0.5 + (score - 70) / 200.0, 0, 4.0);
        var confidence = Math.Min(0.95, 0.50 + 0.05 * input.CourseCount + (input.HasTrend ? 0.15 : 0) + (input.HasPlan ? 0.10 : 0));

        return new PerformanceModelOutput(risk, score, Math.Round(predicted - currentGpa, 2), Math.Round(confidence, 2), factors);
    }
}
