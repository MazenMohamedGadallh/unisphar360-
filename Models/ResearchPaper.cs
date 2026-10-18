namespace UniSphere.Api.Models;

/// <summary>Research paper metadata for discovery and summarization.</summary>
public class ResearchPaper
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? Abstract { get; set; }
    public string? Url { get; set; }
    public string? Source { get; set; } // arxiv | ieee | acm | other
    public DateTime? PublishedDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
