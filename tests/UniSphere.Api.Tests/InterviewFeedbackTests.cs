using System.Text.Json;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

public class InterviewFeedbackTests
{
    [Fact]
    public void ParseAndMap_KeepStrengthsAndRubric_AndTolerateMissingOnes()
    {
        var parsed = GraduateAiService.ParseFeedback(JsonDocument.Parse(
            """{"feedback":"ok","score":14,"strengths":["clear",3],"rubric":[{"criterion":"Depth","score":99},{"score":1}]}""").RootElement);
        Assert.Equal(10, parsed.Score);
        Assert.Equal(["clear"], parsed.Strengths);
        Assert.Empty(parsed.Improvements);
        Assert.Equal(10, Assert.Single(parsed.Rubric).Score);

        // Rows graded before FeedbackDetailsJson existed still map.
        Assert.Empty(InterviewService.MapQuestion(new InterviewQuestion { Feedback = "old", FeedbackScore = 6 }).Rubric);
    }
}
