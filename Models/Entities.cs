namespace UniSphere.Api.Models;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Gender { get; set; } = "";      // Male | Female
    public int Age { get; set; }
    public string? AvatarUrl { get; set; }
    public string Major { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Student;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
    // Role-specific profile fields (only the ones matching Role are used)
    public string Department { get; set; } = "";  // student / doctor / graduate department
    public int? UniversityId { get; set; }        // home university (null = none assigned)
    // Locked profile field: set at registration or by an admin only
    // (administrators manage it via PUT /api/admin/users/{id}).
    public string? Address { get; set; }
    public int? AcademicYear { get; set; }        // student: 1-5
    public string? Specialization { get; set; }   // doctor: field they teach
    public string? GraduatedFrom { get; set; }    // graduate: university/faculty
    public int? GraduationYear { get; set; }      // graduate
}

public class ModuleItem
{
    public int Id { get; set; }
    public string Key { get; set; } = "";        // e.g. "academic-advisor"
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "";       // icon name the Flutter app maps to an IconData
    public string BackgroundColor { get; set; } = ""; // hex, e.g. #EDEBFF
    public string AccentColor { get; set; } = "";
    public int SortOrder { get; set; }
}

public class EventItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}

public class Course
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Track { get; set; } = "";      // used by the simple recommender
    public int Credits { get; set; } = 3;
    public int? UniversityId { get; set; }       // null = available everywhere
    public string Department { get; set; } = ""; // "" = all departments
    public int? AcademicYear { get; set; }       // null = all years
    public string Semester { get; set; } = "";   // e.g. "Fall 2025"
}

/// <summary>A course the student takes in the current semester, with grade and attendance.</summary>
public class Enrollment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public string Semester { get; set; } = "Current";
    public string GradeLetter { get; set; } = "";   // e.g. "B+"
    public double GradePoints { get; set; }          // e.g. 3.3
    public int AttendancePercent { get; set; }
    public string Status { get; set; } = "Good";     // Excellent | Good | Needs Attention
}

/// <summary>GPA / attendance snapshot per semester (used for "vs. last semester").</summary>
public class SemesterRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Label { get; set; } = "";
    public bool IsCurrent { get; set; }
    public double Gpa { get; set; }
    public int AttendanceRate { get; set; }
    public int CompletedCourses { get; set; }
    public int TotalCourses { get; set; }
}

public class StudyPlanItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Date { get; set; }
    public string Title { get; set; } = "";
    public int Hours { get; set; }
    public string? Note { get; set; }
    public bool IsDone { get; set; }
}

public class ActivityItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = "";   // quiz | recommendation | attendance | announcement
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One row per successful login — powers "active users" and "logins per day" admin stats.</summary>
public class LoginRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>One AI chat thread owned by a user.</summary>
public class ChatConversation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "New chat";
    public string Provider { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public ChatConversation? Conversation { get; set; }
    public string Role { get; set; } = "user"; // user | assistant
    public string Content { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-user daily chat usage for the daily message limit.</summary>
public class ChatUsage
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

/// <summary>Role numbers: 1 Student, 2 Graduate, 3 Doctor, 4 Admin.</summary>
public enum UserRole
{
    Student = 1,
    Graduate = 2,
    Doctor = 3,
    Admin = 4
}

public class University
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Logo { get; set; }
}

public class CourseMaterial
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "Summary"; // Summary | Lecture | Assignment | Exam | Announcement
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public string? FileUrl { get; set; }
    public int? UploadedByUserId { get; set; }
    public DateTime? DeadlineUtc { get; set; }  // assignments: submission deadline
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Roles table (1 Student, 2 Graduate, 3 Doctor, 4 Admin) so roles can be looked up in the database.</summary>
public class RoleItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
