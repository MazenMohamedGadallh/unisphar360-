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
    public int? CourseId { get; set; }
    public string? CourseName { get; set; }
}

// ---- Graduate career platform (api/grad/*) ----

public class GradProfileModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Major { get; set; } = "";
    public string Department { get; set; } = "";
    public string? UniversityName { get; set; }
    public int? GraduationYear { get; set; }
    public string? GraduatedFrom { get; set; }
    public string? Headline { get; set; }
    public string? CareerSummary { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    public string? PreferredJobTitles { get; set; }
    public string? PreferredIndustries { get; set; }
    public string? PreferredLocations { get; set; }
    public string WorkModes { get; set; } = "";
    public string EmploymentType { get; set; } = "";
    public string ExperienceLevel { get; set; } = "";
    public string? Languages { get; set; }
    public string? CareerGoal { get; set; }
    public int CompletionPercent { get; set; }
    public List<string> MissingFields { get; set; } = [];
    public List<GradSkillModel> Skills { get; set; } = [];
    public List<ProfileEntryItemModel> Entries { get; set; } = [];
}

public class GradSkillModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Level { get; set; }
    public string Source { get; set; } = "Profile";
}

public class ProfileEntryItemModel
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Experience";
    public string Title { get; set; } = "";
    public string? Organization { get; set; }
    public string? Description { get; set; }
    public string? Period { get; set; }
}

public class GradProfileCompletionModel
{
    public int Percent { get; set; }
    public List<string> MissingFields { get; set; } = [];
    public List<string> NextActions { get; set; } = [];
}

public class GradCvModel
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ParseStatus { get; set; } = "Pending";
    public string? ParseError { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public bool HasAnalysis { get; set; }
    public int? AnalysisId { get; set; }
    public int TextChars { get; set; }
}

public class GradCvAnalysisModel
{
    public int Id { get; set; }
    public int CvDocumentId { get; set; }
    public string? OverallAssessment { get; set; }
    public int? ScorePercent { get; set; }
    public List<string> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public List<string> Suggestions { get; set; } = [];
    public List<GradRewriteModel> Rewrites { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public class GradRewriteModel
{
    public string Original { get; set; } = "";
    public string Rewritten { get; set; } = "";
    public string Why { get; set; } = "";
}

public class GradJobModel
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Location { get; set; } = "";
    public bool Remote { get; set; }
    public string JobType { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Salary { get; set; }
    public string Url { get; set; } = "";
    public DateTime PublishedAtUtc { get; set; }
    public List<string> KeySkills { get; set; } = [];
    public bool IsSaved { get; set; }
    public bool IsApplied { get; set; }
    public string? Snippet { get; set; }
}

public class GradPagedJobsModel
{
    public List<GradJobModel> Jobs { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class GradMatchModel
{
    public GradJobModel Job { get; set; } = new();
    public int ScorePercent { get; set; }
    public List<string> MatchedSkills { get; set; } = [];
    public List<string> MissingSkills { get; set; } = [];
    public string Explanation { get; set; } = "";
    public List<string> Actions { get; set; } = [];
}

public class GradGapRowModel
{
    public string Skill { get; set; } = "";
    public string? Level { get; set; }
    public string Priority { get; set; } = "";
    public string JobsRequiring { get; set; } = "";
}

public class GradSkillGapModel
{
    public string TargetRole { get; set; } = "";
    public int CoveragePercent { get; set; }
    public List<GradGapRowModel> Matched { get; set; } = [];
    public List<GradGapRowModel> Missing { get; set; } = [];
    public List<string> RecommendedSteps { get; set; } = [];
}

public class GradApplicationModel
{
    public int Id { get; set; }
    public int? JobListingId { get; set; }
    public string CompanyName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string Status { get; set; } = "Applied";
    public string? Notes { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class GradStatusCountModel
{
    public string Status { get; set; } = "";
    public int Count { get; set; }
}

public class GradApplicationSummaryModel
{
    public int Total { get; set; }
    public List<GradStatusCountModel> ByStatus { get; set; } = [];
    public int UpcomingFollowUps { get; set; }
    public int Interviews { get; set; }
}

public class GradRoadmapItemModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public string Status { get; set; } = "NotStarted";
    public string? Notes { get; set; }
    public DateTime? TargetDate { get; set; }
    public int SortOrder { get; set; }
    public string Origin { get; set; } = "Manual";
    public DateTime? CompletedAtUtc { get; set; }
}

public class GradRoadmapProgressModel
{
    public int Total { get; set; }
    public int NotStarted { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int Percent { get; set; }
}

public class GradRoadmapSuggestionModel
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Notes { get; set; }
    public string? Why { get; set; }
}

// Envelope returned by /api/grad/roadmap/suggest ({ suggestions, source }).
public class GradRoadmapSuggestionListModel
{
    public List<GradRoadmapSuggestionModel> Suggestions { get; set; } = [];
    public string Source { get; set; } = "";
}

public class GradInterviewSessionModel
{
    public int Id { get; set; }
    public string TargetRole { get; set; } = "";
    public int? JobListingId { get; set; }
    public string QuestionSource { get; set; } = "Curated";
    public string? ProviderName { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int AnsweredCount { get; set; }
    public int TotalCount { get; set; }
    public List<GradInterviewQuestionModel> Questions { get; set; } = [];
}

public class GradInterviewQuestionModel
{
    public int Id { get; set; }
    public string QuestionText { get; set; } = "";
    public string? AnswerText { get; set; }
    public string? Feedback { get; set; }
    public int? FeedbackScore { get; set; }
}

public class GradEventModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public string? Description { get; set; }
}

public class GradActivityItemModel
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public class GradOverviewModel
{
    public string FirstName { get; set; } = "";
    public string FullName { get; set; } = "";
    public int ProfileCompletionPercent { get; set; }
    public List<string> ProfileMissingFields { get; set; } = [];
    public string? CvStatus { get; set; }
    public DateTime? CvUploadedAtUtc { get; set; }
    public int? CvAnalysisScore { get; set; }
    public int MatchingJobsCount { get; set; }
    public int MatchingJobsAboveThreshold { get; set; }
    public int SavedJobsCount { get; set; }
    public int ApplicationsCount { get; set; }
    public int ApplicationsInProgress { get; set; }
    public int UpcomingFollowUpsCount { get; set; }
    public int RoadmapProgressPercent { get; set; }
    public List<GradGapRowModel> TopSkillGaps { get; set; } = [];
    public List<string> NextActions { get; set; } = [];
    public List<GradEventModel> UpcomingEvents { get; set; } = [];
    public List<GradActivityItemModel> RecentActivity { get; set; } = [];
    public int UnreadNotifications { get; set; }
    public int PreviousAnalysisCount { get; set; }
}

public class GradAnnouncementModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Category { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class GradAlumniModel
{
    public string? UniversityName { get; set; }
    public string Department { get; set; } = "";
    public string? GraduationYear { get; set; }
    public string? Degree { get; set; }
    public List<GradEventModel> CareerEvents { get; set; } = [];
    public List<GradAnnouncementModel> Announcements { get; set; } = [];
    public string CareerCenterTip { get; set; } = "";
    public List<string> Contacts { get; set; } = [];
}

public class GradNameCountModel
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class GradInsightsModel
{
    public int JobsIndexed { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public List<GradNameCountModel> TopSkills { get; set; } = [];
    public List<GradNameCountModel> JobsByIndustry { get; set; } = [];
    public List<GradNameCountModel> JobsByWorkType { get; set; } = [];
    public List<string> RecentTitles { get; set; } = [];
}

public class GradSettingsModel
{
    public bool NotifyNewMatches { get; set; }
    public bool NotifyFollowUps { get; set; }
    public int MatchAlertThresholdPercent { get; set; }
    public bool ShowGpaOnProfile { get; set; }
}

public class GradSyncResultModel
{
    public int Imported { get; set; }
    public bool Throttled { get; set; }
    public string? Error { get; set; }
    public DateTime? LastSyncUtc { get; set; }
}
