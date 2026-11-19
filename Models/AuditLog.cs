namespace UniSphere.Api.Models;

/// <summary>Audit trail for every authenticated API call.</summary>
public class AuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
}
