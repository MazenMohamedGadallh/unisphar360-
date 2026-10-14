namespace UniSphere.Api;

// DTOs used by Blazor pages — kept here so generic types like List<T>
// don't get interpreted as HTML tags by the Razor compiler.

public class AnalyticsResponse
{
    public int TotalStudents { get; set; }
    public int TotalGraduates { get; set; }
    public int TotalDoctors { get; set; }
    public int TotalCourses { get; set; }
    public int TotalEnrollments { get; set; }
    public double AverageGpa { get; set; }
    public double AverageAttendance { get; set; }
    public List<GpaRange> GpaDistribution { get; set; } = new();
    public List<CourseEnrollment> TopCourses { get; set; } = new();
    public List<AuditLogEntry> RecentAuditLogs { get; set; } = new();
}

public class GpaRange
{
    public string Range { get; set; } = "";
    public int Count { get; set; }
}

public class CourseEnrollment
{
    public string Course { get; set; } = "";
    public int Count { get; set; }
}

public class AuditLogEntry
{
    public int UserId { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
}

public class CareerRecommendationDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string RequiredSkills { get; set; } = "";
    public string RecommendedCourses { get; set; } = "";
    public string InternshipTypes { get; set; } = "";
    public string MatchConfidence { get; set; } = "Medium";
}

public class SkillGapAnalysis
{
    public List<string> CompletedCourses { get; set; } = new();
    public List<string> PendingCourses { get; set; } = new();
    public string Recommendation { get; set; } = "";
}

public class EnrollmentDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = "";
    public string CourseTitle { get; set; } = "";
    public string Semester { get; set; } = "";
    public string GradeLetter { get; set; } = "";
    public double GradePoints { get; set; }
    public int AttendancePercent { get; set; }
    public string Status { get; set; } = "";
}

public class ResearchPaperDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? Abstract { get; set; }
    public string? Url { get; set; }
    public string? Source { get; set; }
    public DateTime? PublishedDate { get; set; }
}

public class SummaryResult
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
}

public class ProfileUser
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Major { get; set; } = "";
    public int RoleId { get; set; }
    public string Role { get; set; } = "";
}

public class AnnouncementDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Category { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
