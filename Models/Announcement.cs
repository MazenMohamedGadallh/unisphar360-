namespace UniSphere.Api.Models;

/// <summary>University-scoped (UniversityId, null = none assigned) OR
/// course-specific announcements (CourseId set by the doctor module).</summary>
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
    /// <summary>University this announcement belongs to. Scoped users only
    /// see announcements for their own university.</summary>
    public int? UniversityId { get; set; }
}
