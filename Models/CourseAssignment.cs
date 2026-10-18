namespace UniSphere.Api.Models;

/// <summary>Join row linking a doctor (User with Role=Doctor) to a course
/// they teach. Admins manage assignments; doctors can never self-assign.
/// Unique (DoctorUserId, CourseId) prevents duplicate assignments.</summary>
public class CourseAssignment
{
    public int Id { get; set; }
    public int DoctorUserId { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
