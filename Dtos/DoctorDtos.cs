namespace UniSphere.Api.Dtos;

// ---- Doctor profile (/api/doctor/profile)

/// <summary>The doctor's own profile. FirstName/MiddleName/LastName/Email/
/// Address/PhoneNumber/UniversityName/Department are LOCKED (read-only,
/// editable only by an admin); AvatarUrl and the per-course display titles
/// are editable by the doctor. Identity always resolves from the JWT claims.</summary>
public record DoctorProfileDto(
    int Id,
    string FirstName, string? MiddleName, string LastName,
    string Email, string? Address, string PhoneNumber,
    string? UniversityName, string Department,
    string? AvatarUrl,
    IReadOnlyList<DoctorProfileCourseDto> Courses);

/// <summary>One course assigned to the doctor with its editable display title
/// (catalog title/code stay read-only) and the server-computed number of
/// enrolled students in the doctor's university.</summary>
public record DoctorProfileCourseDto(
    int CourseId, string Code, string CatalogTitle,
    string? DisplayTitle, string Semester, int EnrolledCount);

/// <summary>PUT /api/doctor/profile body. Deliberately contains ONLY the
/// editable fields. Any locked field (name, email, phone, address,
/// university, department, ...) in the JSON is rejected with 400 by the
/// controller before binding; the User entity is never model-bound.</summary>
public record UpdateDoctorProfileRequest(string? AvatarUrl);

/// <summary>POST /api/doctor/profile/avatar result (same shape as material uploads).</summary>
/// <summary>PUT /api/doctor/courses/{id}/display-title body.</summary>
public record SetCourseDisplayTitleRequest(string? DisplayTitle);

// ---- Doctor dashboard (/api/doctor/dashboard)

public record DoctorDashboardDto(
    string DoctorName,
    int CourseCount,
    int StudentCount,
    double? AverageGradePoints,
    double? AverageAttendance,
    IReadOnlyList<DoctorCourseSummaryDto> Courses,
    IReadOnlyList<DoctorAlertDto> Alerts,
    IReadOnlyList<DoctorAnnouncementDto> RecentAnnouncements);

public record DoctorCourseSummaryDto(
    int CourseId, string Code, string Title, string Track, int Credits, string Semester,
    int StudentCount, double? AverageGradePoints, double? AverageAttendance);

public record DoctorCourseDetailDto(
    int Id, string Code, string Title, string Description,
    string Track, int Credits, string Semester, string Department, int? AcademicYear);

// ---- Doctor course students (/api/doctor/courses/{id}/students)

public record DoctorStudentDto(
    int EnrollmentId, int StudentId, string FullName, string Email,
    string Department, int? AcademicYear,
    string GradeLetter, double GradePoints, int AttendancePercent, string Status);

// ---- Doctor course analytics (/api/doctor/courses/{id}/analytics)

public record GradeBucketDto(string Label, int Count);

public record AttendanceBucketDto(string Label, int Count);

public record DoctorAlertDto(
    int StudentId, int? EnrollmentId, string StudentName, string Reason, string Severity);

public record DoctorCourseAnalyticsDto(
    int CourseId, string Code, string Title,
    int EnrollmentCount, int GradedCount,
    double? AverageGradePoints, double? AverageAttendance,
    IReadOnlyList<GradeBucketDto> GradeDistribution,
    IReadOnlyList<AttendanceBucketDto> AttendanceDistribution,
    IReadOnlyList<DoctorAlertDto> Alerts);

// ---- Doctor materials (/api/doctor/courses/{id}/materials)

public record DoctorMaterialDto(
    int Id, int CourseId, string Title, string Type, string Description,
    string Content, string? FileUrl, DateTime? DeadlineUtc, DateTime CreatedAtUtc);

public record CreateDoctorMaterialRequest(
    string Title, string Type, string? Description, string? Content,
    string? FileUrl, DateTime? DeadlineUtc);

public record UpdateDoctorMaterialRequest(
    string Title, string Type, string? Description, string? Content,
    string? FileUrl, DateTime? DeadlineUtc);

public record MaterialUploadResultDto(string FileUrl, string FileName, long SizeBytes);

// ---- Doctor announcements (/api/doctor/courses/{id}/announcements)

public record DoctorAnnouncementDto(
    int Id, int? CourseId, string? CourseName, string Title, string Body,
    string? Category, DateTime CreatedAtUtc);

public record CreateCourseAnnouncementRequest(string Title, string Body, string? Category);

// ---- Admin: doctor <-> course assignments (/api/course-assignments)

public record CourseAssignmentDto(
    int Id, int CourseId, string CourseCode, string CourseTitle,
    int DoctorUserId, string DoctorName, DateTime AssignedAtUtc);

public record CreateCourseAssignmentRequest(int DoctorUserId, int CourseId);

// ---- AI teaching assistant (/api/doctor/ai)

public record QuizGenerationRequest(int CourseId, int? MaterialId, int QuestionCount = 5);

public record GeneratedQuestionDto(
    string Question, IReadOnlyList<string> Options, int CorrectIndex,
    string Explanation, string? Reference);

public record QuizGenerationResponse(
    string Provider, string Model, IReadOnlyList<GeneratedQuestionDto> Questions, bool IsDraft);

public record SummarizeRequest(int CourseId, int MaterialId);

public record LectureSummaryDto(
    int MaterialId, string Title, string Summary, string Provider, string Model);

public record LectureQuestionRequest(int CourseId, string Question);

public record LectureAnswerDto(
    string Answer, IReadOnlyList<string> Sources, bool Grounded, string Provider, string Model);
