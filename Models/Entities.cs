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
    public string Address { get; set; } = "";
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
    /// <summary>University that owns this event. Null only on legacy rows
    /// before the scope backfill; scoped users never see those.</summary>
    public int? UniversityId { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    // Career notification fields (added post-launch; see DbSchemaPatcher).
    // "System" covers every row created before career notifications existed
    // and all non-career notifications (welcome, announcements, graduation).
    public string Category { get; set; } = "System";
    /// <summary>Stable per-user key that makes regenerated career
    /// notifications idempotent (e.g. "match:42"). Null for rows that are
    /// inserted once by explicit feature writes.</summary>
    public string? DedupeKey { get; set; }
}

public class Course
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Track { get; set; } = "";      // used by the simple recommender
    public int Credits { get; set; } = 3;
    public int? UniversityId { get; set; }       // null = legacy/unscoped
    public string Department { get; set; } = ""; // "" = all departments
    public int? AcademicYear { get; set; }       // null = all years
    public string Semester { get; set; } = "";   // e.g. "Fall 2025"
    /// <summary>Comma-separated prerequisite course codes. The importer
    /// only keeps codes that resolve inside the same university.</summary>
    public string Prerequisites { get; set; } = "";
}

/// <summary>Assignment of a doctor to teach a course in a term. Unique per
/// course + doctor + term. Managed by admins; doctors only ever see and
/// write to courses they are assigned to.</summary>
public class CourseInstructor
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int DoctorUserId { get; set; }
    public string Term { get; set; } = "";   // e.g. "Fall 2025"
    /// <summary>Doctor-editable title shown to students. The catalog
    /// Course.Title and Course.Code are never changed.</summary>
    public string? DisplayTitle { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
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
    /// <summary>Server-generated GUID + allowed extension for an uploaded
    /// attachment. Only this bare file name is persisted; no absolute path is
    /// ever stored in the database.</summary>
    public string? StoredFileName { get; set; }
    /// <summary>Sanitized client-facing name of the uploaded attachment.</summary>
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileContentType { get; set; }
    /// <summary>Assignment-style submission deadline (doctor module).</summary>
    public DateTime? DeadlineUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Roles table (1 Student, 2 Graduate, 3 Doctor, 4 Admin) so roles can be looked up in the database.</summary>
public class RoleItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Lifecycle of a graduation application.</summary>
public static class GraduationStatus
{
    public const string Pending = "Pending";
    public const string InReview = "InReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";

    /// <summary>Statuses that block a second application for the same student.</summary>
    public static readonly string[] ActiveStatuses = { Pending, InReview };

    /// <summary>Statuses an application cannot leave (a new one must be filed instead).</summary>
    public static bool IsFinal(string status) =>
        status is Approved or Rejected or Withdrawn;
}

/// <summary>
/// A student's graduation application (degree audit). Submitted once the student
/// believes they are done; the registrar approves or rejects it, and an approval
/// is the conferral — the user's GraduationYear/GraduatedFrom are stamped from it.
/// </summary>
public class GraduationApplication
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Status { get; set; } = GraduationStatus.Pending;

    // Snapshot of the student's record at submission time (audit trail).
    public string Degree { get; set; } = "";        // Major/program at submission
    public string Department { get; set; } = "";
    public double GpaAtApplication { get; set; }
    public int CoursesCompleted { get; set; }
    public int CoursesRequired { get; set; }

    public string? Note { get; set; }               // student's message to the registrar
    public int? ExpectedGraduationYear { get; set; }

    public string? DecisionNote { get; set; }       // registrar's message
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? ConferredGraduationYear { get; set; } // set when approved
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One generated exam owned by a doctor for one of their assigned
/// courses. Contains the generation settings and a snapshot of which
/// source materials were used.</summary>
public class Exam
{
    public int Id { get; set; }
    public int DoctorUserId { get; set; }
    public User? DoctorUser { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public string Title { get; set; } = "";
    public int QuestionCount { get; set; }
    public string QuestionTypes { get; set; } = "";   // comma-separated: MCQ, TrueFalse, ShortAnswer
    public string Difficulty { get; set; } = "mixed"; // easy, medium, hard, mixed
    public string Language { get; set; } = "en";      // en or ar
    public string? MaterialsUsed { get; set; }        // JSON list of material titles used
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Draft";     // Draft | Published
    public DateTime? PublishedAtUtc { get; set; }
    public int? Marks { get; set; }                   // total marks for the exam
    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
}

/// <summary>One question inside an exam, with its answer hidden behind a
/// "Show answers" toggle until the doctor or student chooses to reveal it.</summary>
public class ExamQuestion
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    public string QuestionText { get; set; } = "";
    public string QuestionType { get; set; } = "MCQ";   // MCQ, TrueFalse, ShortAnswer
    public string? OptionsJson { get; set; }            // JSON array of option strings for MCQ
    public string CorrectAnswer { get; set; } = "";
    public string? Explanation { get; set; }
    public string? SourceMaterial { get; set; }         // title of the source material this came from
    public int OrderIndex { get; set; }
    public int? Marks { get; set; }                     // marks for this question
}
