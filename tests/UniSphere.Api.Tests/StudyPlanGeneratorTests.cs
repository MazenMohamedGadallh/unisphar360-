using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

public class StudyPlanGeneratorTests
{
    private static readonly StudyPlanCourseInput[] Courses =
    {
        new("Strong & easy", 3.9, 98, 3, 3.6),
        new("Weak & hard", 2.0, 75, 3, 2.3),
        new("Medium", 3.0, 90, 3, 3.0),
    };

    [Fact]
    public void Allocate_GivesMostHoursToWeakHardCourse_AndKeepsTotal()
    {
        var plan = StudyPlanGenerator.Allocate(Courses, 14);

        Assert.Equal(14, plan.Sum(c => c.WeeklyHours));
        Assert.All(plan, c => Assert.True(c.WeeklyHours >= 1));
        Assert.Equal("Weak & hard", plan[0].Course);
        Assert.Equal("High", plan[0].Priority);
        Assert.Equal("Low", plan.Single(c => c.Course == "Strong & easy").Priority);
    }

    [Fact]
    public void Schedule_UsesAllHours_AndCapsEachDay()
    {
        var plan = StudyPlanGenerator.Allocate(Courses, 14);
        var start = new DateTime(2026, 11, 15);

        var sessions = StudyPlanGenerator.Schedule(plan, start);

        Assert.Equal(14, sessions.Sum(s => s.Hours));
        Assert.All(sessions.GroupBy(s => s.Date), g => Assert.True(g.Sum(s => s.Hours) <= 4));
        Assert.All(sessions, s => Assert.InRange(s.Date, start, start.AddDays(5)));
    }

    [Fact]
    public void Allocate_NoCourses_ReturnsEmpty() =>
        Assert.Empty(StudyPlanGenerator.Allocate(Array.Empty<StudyPlanCourseInput>(), 14));
}
