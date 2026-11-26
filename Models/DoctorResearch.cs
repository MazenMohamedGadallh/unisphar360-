namespace UniSphere.Api.Models;

public class DoctorResearch
{
    public int Id { get; set; }
    public int DoctorUserId { get; set; }
    public User? DoctorUser { get; set; }        // author navigation (loaded when needed)
    public int UniversityId { get; set; }
    public string Title { get; set; } = "";
    public string Abstract { get; set; } = "";
    public string Authors { get; set; } = "";
    public string Keywords { get; set; } = "";
    public int Year { get; set; }
    public string Field { get; set; } = "";      // field / department of the research
    public string? PdfPath { get; set; }         // GUID file name only, no directories
    public string? DoiOrUrl { get; set; }        // optional DOI or link
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsPublished { get; set; }
}
