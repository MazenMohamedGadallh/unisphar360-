namespace UniSphere.Api.Models;

public class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Category { get; set; } // academic | event | career | general
    public int? CourseId { get; set; }     // null when it is not course-specific
    public Course? Course { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public int? UniversityId { get; set; }
}
