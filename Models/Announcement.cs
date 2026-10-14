namespace UniSphere.Api.Models;

/// <summary>University-wide or course-specific announcements.</summary>
public class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Category { get; set; } // academic | event | career | general
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
}
