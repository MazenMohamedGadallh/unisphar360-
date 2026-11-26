using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace UniSphere.Api.Dtos;

public record UserDto(int Id, string FullName, string FirstName, string? AvatarUrl, string Major);

public record MeDto(int Id, string FullName, string Email, string? AvatarUrl, string Major, int RoleId, string Role);

public record ModuleDto(string Key, string Title, string Subtitle, string Icon, string BackgroundColor, string AccentColor);

public record EventDto(int Id, string Title, string Location, DateTime StartsAtUtc, string? ImageUrl, string? Description);

public record NotificationDto(int Id, string Title, string Body, DateTime CreatedAtUtc, bool IsRead, string Category);

public record RecommendationDto(string Headline, string Message, string Route);

public record HomeResponse(
    UserDto User,
    string Greeting,
    int UnreadNotifications,
    IReadOnlyList<ModuleDto> Modules,
    IReadOnlyList<EventDto> UpcomingEvents,
    RecommendationDto Recommendation);

// Search (/api/search)
public record SearchItemDto(int? Id, string Title, string Subtitle, string Route, string Icon);

public record SearchGroupDto(string Type, string Label, IReadOnlyList<SearchItemDto> Items);

public record SearchResponseDto(IReadOnlyList<SearchGroupDto> Groups);

public class SearchQuery
{
    public string? Q { get; set; }
    public string? Type { get; set; }
    public int Take { get; set; } = 20;
}

public record CourseDto(int Id, string Code, string Title, string Description, string Track, int Credits, string Instructors = "");

public record UpdateUserRequest(string FullName, string Major, string? AvatarUrl);

// Academic Advisor screen
public record AcademicStats(
    double Gpa, double GpaDelta, double MaxGpa,
    int CompletedCourses, int TotalCourses, int CompletedPercent,
    int AttendanceRate, int AttendanceDelta,
    int SubjectsToImprove, int RecommendedCourses);

public record GradeRowDto(int CourseId, string Course, string GradeLetter, double GradePoints, string GradeDisplay, int Attendance, string Status);

public record StudyDayDto(int Id, DateTime Date, string DayLabel, int DayNumber, string Title, int Hours, string? Note, bool IsDone);

public record StudyPlannerResponse(string WeekLabel, DateTime WeekStart, IReadOnlyList<StudyDayDto> Days);

public record InsightDto(string Kind, string Title, string Message, string? ActionLabel, string? ActionRoute);

public record QuickActionDto(string Key, string Title, string Icon, string Route, string BackgroundColor, string AccentColor);

public record ActivityDto(int Id, string Kind, string Message, DateTime CreatedAtUtc, string TimeAgo);

public record CreateStudyItemRequest(DateTime Date, string Title, int Hours, string? Note);

public record AcademicDashboardResponse(
    UserDto User,
    string Greeting,
    string Tagline,
    int UnreadNotifications,
    AcademicStats Stats,
    IReadOnlyList<GradeRowDto> Performance,
    StudyPlannerResponse StudyPlanner,
    IReadOnlyList<InsightDto> Insights,
    IReadOnlyList<CourseDto> RecommendedCourses,
    IReadOnlyList<QuickActionDto> QuickActions,
    IReadOnlyList<ActivityDto> RecentActivity);

// Student Home dashboard (/api/dashboard/*)
public record DashboardSummaryDto(
    string FullName, string FirstName, string Greeting,
    double CurrentGpa, double GpaDeltaVsLastSemester,
    int CompletedCourses, int TotalCourses, int CompletionPercent,
    int AttendanceRate, int AttendanceDelta,
    int SubjectsToImprove, int RecommendedCoursesCount, int UnreadNotifications);

public record DashboardPerformanceRowDto(
    int CourseId, string CourseName, string GradeLetter, double GradePoints,
    int AttendancePercent, string Status);

public record DashboardStudyDayDto(
    int Id, DateTime Date, string DayLabel, string Subject, int Hours,
    bool IsCompleted, bool IsFreeDay);

public record DashboardStudyPlanDto(string WeekLabel, DateTime WeekStart, IReadOnlyList<DashboardStudyDayDto> Days);

public record DashboardInsightDto(string Type, string Title, string Message, string? ActionLabel, string? ActionRoute);

public record DashboardRecommendedCourseDto(int Id, string Name, string Track, int Credits, string Tag);

public record DashboardActivityDto(int Id, string Text, DateTime CreatedAt);

public record UnreadCountDto(int UnreadCount);

// Performance analytics (/api/analytics/*)
public record FactorContributionDto(string Factor, double Weight, double Value, double Contribution, string Description);

public record PerformanceCourseRiskDto(string Course, string RiskLevel, string Reason);

public record PerformanceAnalysisDto(
    string RiskLevel, double PredictedGpa, double Confidence, double Score,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<PerformanceCourseRiskDto> CourseRisks,
    IReadOnlyList<FactorContributionDto> Factors);

public record GpaTrendPointDto(string Label, double Gpa);

// Admin panel (/api/admin/*)
public record DailyCountDto(DateTime Date, int Count);

public record AdminOverviewDto(
    int TotalStudents, int TotalDoctors, int TotalGraduates, int TotalAdmins,
    int NewRegistrations7d, int NewRegistrations30d, int ActiveUsersToday,
    IReadOnlyList<DailyCountDto> LoginsPerDay,
    int OnTrackStudents, int NeedsAttentionStudents, int AtRiskStudents,
    double AverageGpa, double AverageAttendance, int CoursesCount);

public record AdminUserDto(
    int Id, string FullName, string Email, string Role, int RoleId,
    bool IsActive, bool IsDeleted, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, string Major);

public record PagedResultDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record PatchUserStatusRequest(bool IsActive);

public record PatchUserRoleRequest(string Role);

public record DepartmentStatsDto(string Department, int Students, double AverageGpa, double AverageAttendance);

public record AtRiskStudentDto(
    int Id, string FullName, string Email, string Department,
    string RiskLevel, double Score, double PredictedGpa);

public record AuditLogDto(
    int Id, int UserId, string Method, string Path, int StatusCode,
    int DurationMs, DateTime Timestamp, string IpAddress);

public record SystemHealthDto(
    string Status, string Database, double UptimeSeconds, string Version, DateTime ServerTimeUtc);

// My University (/api/university/*)
public record UniversityDto(int Id, string Name, string? Logo);

public record MyUniversityDto(
    string? UniversityName, string? Department, int? AcademicYear,
    int CoursesCount, int MaterialsCount);

public record UniversityCourseDto(
    int Id, string Code, string Title, string Department,
    int? AcademicYear, string Semester, int MaterialsCount, DateTime? LatestMaterialDate,
    string Instructors = "", string Status = "", bool MatchesStudentDepartmentYear = false);

public record MaterialDto(
    int Id, int CourseId, string CourseName, string Title, string Type,
    string Description, string Content, string? FileUrl, DateTime CreatedAt,
    string InstructorName = "",
    bool HasUploadedFile = false,
    string? UploadedFileName = null,
    long? UploadedFileSizeBytes = null,
    DateTime? DeadlineUtc = null);

public record UpcomingItemDto(
    int Id, string Title, string CourseName, string Type, DateTime Date, string? Description);

public record CreateMaterialRequest(
    int CourseId, string Title, string Type,
    string? Description, string? Content, string? FileUrl);

// AI Chat (/api/chat)
public record ChatProviderDto(string Name, string DisplayName, string Model);

public record ChatProviderStatusDto(string Name, string DisplayName, string Model, bool Available, string Reason);

public record ChatConversationDto(int Id, string Title, string Provider, DateTime UpdatedAtUtc, int MessageCount);

public record ChatMessageDto(
    int Id, string Role, string Content, string Provider, string Model, DateTime CreatedAtUtc);

public record ChatConversationDetailDto(
    int Id, string Title, string Provider, DateTime UpdatedAtUtc,
    IReadOnlyList<ChatMessageDto> Messages);

public record SendChatMessageRequest(string Provider, string Content);

public record RenameConversationRequest(string Title);

public record RegisterRequest(
    string FirstName, string? MiddleName, string LastName,
    string PhoneNumber, string Email, string Gender, int Age,
    string Role,
    string Password, string ConfirmPassword,
    int? AcademicYear, string? Department,
    string? Specialization,
    string? GraduatedFrom, int? GraduationYear,
    int? UniversityId, string? Address = null);

// RoleId is optional: if sent, it must match the account's role (1 Student, 2 Graduate, 3 Doctor, 4 Admin).
public record LoginRequest(string Email, string Password, int? RoleId = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AuthUserDto(int Id, string FullName, string Email, int RoleId, string Role, string Major, string? AvatarUrl);

public record RoleDto(int Id, string Name);

public record AuthResponse(string Token, DateTime ExpiresAtUtc, AuthUserDto User);

// Part 1: instructor assignments (/api/admin/*)
public record InstructorAssignmentDto(
    int Id, int CourseId, string CourseCode, string CourseTitle,
    int? UniversityId, string UniversityName,
    int DoctorUserId, string DoctorName, string Term, DateTime CreatedAtUtc);

public record CreateInstructorAssignmentRequest(int CourseId, int DoctorUserId, string Term);

public record AdminDoctorDto(
    int Id, string FullName, string Email, int? UniversityId,
    string UniversityName, string Specialization, bool IsActive);

public record AdminCourseDto(int Id, string Code, string Title, int? UniversityId);

// Part 1: doctor workspace (/api/doctor/*)
public record DoctorCourseDto(
    int Id, string Code, string Title, string Department,
    int? AcademicYear, string Semester, int Credits, string Prerequisites,
    int StudentsCount, int MaterialsCount, string Terms,
    string? DisplayTitle = null)
{
    public int CourseId => Id;
    public int StudentCount => StudentsCount;
}

public record CreateDoctorAnnouncementRequest(int CourseId, string Title, string Body, string? Category);

public record DoctorSummaryDto(
    int CoursesCount, int StudentsCount, int MaterialsCount, int AnnouncementsCount,
    IReadOnlyList<MaterialDto> LatestMaterials,
    IReadOnlyList<DoctorAnnouncementDto> LatestAnnouncements,
    IReadOnlyList<DoctorAnnouncementDto> LatestByMe,
    IReadOnlyList<MaterialDto> Upcoming);

// Phase 1: doctor-editable course display title (catalog stays intact)
public record UpdateDoctorCourseRequest(string DisplayTitle)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; init; }
}

// Phase 1: admin editing of locked profile fields
public record AdminUserDetailDto(
    int Id, string FirstName, string? MiddleName, string LastName, string FullName,
    string Email, string Address, string PhoneNumber,
    int? UniversityId, string UniversityName, string Department,
    string Role, int RoleId, bool IsActive, bool IsDeleted);

public record AdminUpdateUserRequest(
    string FirstName, string? MiddleName, string LastName,
    string Email, string PhoneNumber, string? Address,
    string? Department, int? UniversityId);

public class DoctorResearchUploadRequest
{
    public string Title { get; set; } = "";
    public string Abstract { get; set; } = "";
    public string Authors { get; set; } = "";
    public string Keywords { get; set; } = "";
    public int Year { get; set; }
    public string Field { get; set; } = "";
    public string? DoiOrUrl { get; set; }
    public bool IsPublished { get; set; } = true;
    public IFormFile? Pdf { get; set; }
}

public record DoctorResearchDto(
    int Id, string Title, string Abstract, string Authors, string Keywords,
    int Year, string Field, string? DoiOrUrl, bool IsPublished,
    bool HasPdf, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record ResearchBrowseItemDto(
    int Id, string Title, string Authors, string Keywords, int Year,
    string Field, string AuthorName, string UniversityName,
    bool HasPdf, DateTime CreatedAtUtc);

public record ResearchDetailDto(
    int Id, string Title, string Abstract, string Authors, string Keywords,
    int Year, string Field, string? DoiOrUrl,
    string AuthorName, string AuthorDepartment, string UniversityName,
    bool HasPdf, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record ResearchUniversityOptionDto(int Id, string Name);

public record ResearchFiltersDto(
    IReadOnlyList<ResearchUniversityOptionDto> Universities,
    IReadOnlyList<string> Fields,
    IReadOnlyList<int> Years);

// Graduation (/api/graduation/*)
public record GraduationChecklistItemDto(string Key, string Title, string Detail, bool IsMet);

public record GraduationApplicationSummaryDto(
    int Id, string Status, DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc, string? DecisionNote, int? ConferredGraduationYear);

public record GraduationAlumniDto(
    string GraduatedFrom, int? GraduationYear, double FinalGpa, string Honors, bool HasCertificate);

public record GraduationOverviewResponse(
    string Role, bool IsGraduated,
    string Degree, string Department, string UniversityName, int? AcademicYear,
    double Gpa, double MinGpaRequired,
    int CoursesCompleted, int CoursesRequired, int CompletionPercent,
    int CreditsCompleted, int CreditsInProgress,
    IReadOnlyList<string> FailedCourses,
    bool IsEligible, IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<GraduationChecklistItemDto> Checklist,
    string ApplicationStatus, // "none" | Pending | InReview | Approved | Rejected | Withdrawn
    GraduationApplicationSummaryDto? Application,
    GraduationAlumniDto? Alumni);

public record GraduationApplyRequest(string? Note, int? ExpectedGraduationYear);

public record GraduationCeremonyDto(int Id, string Title, string Location, DateTime StartsAtUtc, string? Description);

public record GraduationCertificateDto(
    string CertificateId, string Holder, string Degree, string University,
    int GraduationYear, double FinalGpa, string Honors, DateTime? ConferredOnUtc);

public record GraduationApplicationAdminDto(
    int Id, int UserId, string StudentName, string Email, string Degree,
    double GpaAtApplication, int CoursesCompleted, int CoursesRequired,
    string Status, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc,
    string? DecisionNote, int? ConferredGraduationYear);

public record ReviewGraduationApplicationRequest(bool Approve, string? Note, int? GraduationYear);

// Exam Generator (/api/doctor/exam-generator)

public record ExamGenerationRequest(
    int CourseId,
    int QuestionCount = 10,
    string QuestionTypes = "MCQ",   // comma-separated: MCQ, TrueFalse, ShortAnswer
    string Difficulty = "mixed",    // easy, medium, hard, mixed
    string Language = "en");        // en or ar

public record GeneratedExamQuestionDto(
    string QuestionText,
    string QuestionType,
    string? CorrectAnswer,
    string? Explanation,
    string? SourceMaterial,
    string? OptionsJson);   // JSON array of option strings for MCQ

public record ExamGenerationResponse(
    string Provider, string Model,
    IReadOnlyList<GeneratedExamQuestionDto> Questions,
    int CourseId, string CourseCode, string CourseTitle,
    string? RagWarning = null);

public record ExamQuestionDto(
    int Id, int ExamId,
    string QuestionText, string QuestionType,
    string? CorrectAnswer, string? Explanation, string? SourceMaterial,
    string? OptionsJson, int OrderIndex, int? Marks);

public record ExamDto(
    int Id, int DoctorUserId, int CourseId, string CourseCode, string CourseTitle,
    string Title, int QuestionCount, string QuestionTypes,
    string Difficulty, string Language, string? MaterialsUsed,
    DateTime CreatedAtUtc, string Status, DateTime? PublishedAtUtc, int? Marks,
    IReadOnlyList<ExamQuestionDto> Questions);

public record ExamListDto(
    int Id, int CourseId, string CourseCode, string CourseTitle,
    string Title, int QuestionCount, string Difficulty, string Language,
    DateTime CreatedAtUtc, string Status, DateTime? PublishedAtUtc);

public record UpdateExamRequest(
    int CourseId,
    string Title, int QuestionCount, string QuestionTypes,
    string Difficulty, string Language, string? MaterialsUsed,
    IReadOnlyList<ExamQuestionDto> Questions);

public record UpdateExamQuestionRequest(
    string QuestionText, string QuestionType,
    string? CorrectAnswer, string? Explanation, string? SourceMaterial,
    string? OptionsJson, int OrderIndex);

// Exam Assistant (/api/doctor/exam-assistant)

public record ExamAssistantGenerationRequest(
    int CourseId,
    int[] MaterialIds,
    int QuestionCount = 10,
    string Difficulty = "mixed",
    string Language = "en",
    int? McqCount = null,
    int? TrueFalseCount = null,
    int? EssayCount = null,
    int? DefinitionCount = null);

public record TranslateQuestionsRequest(
    IReadOnlyList<GeneratedExamQuestionDto> Questions,
    string Language = "ar");

public record ExamAssistantPublishRequest(
    int ExamId,
    string Title,
    int? Marks);

public record StudentExamDto(
    int Id, int CourseId, string CourseCode, string CourseTitle,
    string Title, int QuestionCount, int? Marks,
    DateTime CreatedAtUtc, DateTime? PublishedAtUtc);

public record StudentExamQuestionDto(
    int Id, int ExamId,
    string QuestionText, string QuestionType,
    string? OptionsJson, int OrderIndex, int? Marks);

public record UserNotificationSettingDto(
    int UserId, string Preference, DateTime? MutedUntilUtc);

public record UpdateUserNotificationSettingRequest(
    string Preference, DateTime? MutedUntilUtc);

// Personal study plan (/api/study-planner/suggestion)
public record StudyPlanCourseDto(string Course, int WeeklyHours, string Priority, double DifficultyPercent, string Reason);
public record StudyPlanSessionDto(DateTime Date, string Course, int Hours);
public record StudyPlanSuggestionDto(DateTime WeekStart, int TotalHours,
    IReadOnlyList<StudyPlanCourseDto> Courses, IReadOnlyList<StudyPlanSessionDto> Sessions);
