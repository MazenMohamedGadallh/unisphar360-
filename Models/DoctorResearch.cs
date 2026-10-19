namespace UniSphere.Api.Models;

/// <summary>A research paper uploaded by a doctor. DoctorUserId and
/// UniversityId are always set on the server from the authenticated user —
/// never accepted from the client. PdfPath is a server-generated GUID file
/// name stored under App_Data/research (outside wwwroot, so it is never
/// served statically and the physical path is never exposed); downloads go
/// through the authorized ResearchHubController instead.
/// Cross-university Browse is by design the exception to isolation: any
/// doctor/admin can list PUBLISHED papers; private data (email/phone) of
/// the author is never included in browse/detail output.</summary>
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
