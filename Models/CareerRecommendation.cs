namespace UniSphere.Api.Models;

/// <summary>Career path recommendation for a student.</summary>
public class CareerRecommendation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? RequiredSkills { get; set; }
    public string? RecommendedCourses { get; set; }
    public string? InternshipTypes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
