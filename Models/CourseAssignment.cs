namespace UniSphere.Api.Models;

public class CourseAssignment
{
    public int Id { get; set; }
    public int DoctorUserId { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public string? DisplayTitle { get; set; }
}
