namespace UniSphere.Api.Dtos;

// ---------------------------------------------------------------------------
// Graduate career platform DTOs (api/grad/*). All user-owned data returns the
// authenticated owner's records only.
// ---------------------------------------------------------------------------

public record GraduateSkillDto(int Id, string Name, string? Level, string Source);

public record GraduateSkillCreateRequest(string Name, string? Level);

public record ProfileEntryDto(int Id, string Kind, string Title, string? Organization, string? Description, string? Period);

public record ProfileEntryCreateRequest(string Kind, string Title, string? Organization, string? Description, string? Period);

public record GraduateProfileDto(
    int UserId, string FullName, string FirstName, string? AvatarUrl,
    string Major, string Department, string? UniversityName, int? GraduationYear, string? GraduatedFrom,
    string? Headline, string? CareerSummary, string? LinkedInUrl, string? GitHubUrl, string? PortfolioUrl,
    string? PreferredJobTitles, string? PreferredIndustries, string? PreferredLocations,
    string WorkModes, string EmploymentType, string ExperienceLevel, string? Languages, string? CareerGoal,
    int CompletionPercent, IReadOnlyList<string> MissingFields,
    IReadOnlyList<GraduateSkillDto> Skills,
    IReadOnlyList<ProfileEntryDto> Entries);

public record GraduateProfileUpdateRequest(
    string? Headline, string? CareerSummary,
    string? LinkedInUrl, string? GitHubUrl, string? PortfolioUrl,
    string? PreferredJobTitles, string? PreferredIndustries, string? PreferredLocations,
    string? WorkModes, string? EmploymentType, string? ExperienceLevel, string? Languages, string? CareerGoal);

public record ProfileCompletionDto(int Percent, IReadOnlyList<string> MissingFields, IReadOnlyList<string> NextActions);

public record CvDocumentDto(
    int Id, string FileName, string ContentType, long SizeBytes, string ParseStatus,
    string? ParseError, DateTime UploadedAtUtc, bool HasAnalysis, int? AnalysisId, int TextChars);

public record CvAnalysisDto(
    int Id, int CvDocumentId, string? OverallAssessment, int? ScorePercent,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Suggestions, IReadOnlyList<KeywordRewrite> Rewrites,
    IReadOnlyList<string> Keywords, string ProviderName, string Model, DateTime CreatedAtUtc);

public record KeywordRewrite(string Original, string Rewritten, string Why);

public record CvAnalyzeRequest(string? TargetRole);

public record JobListingDto(
    int Id, string Source, string Title, string CompanyName, string Location, bool Remote,
    string JobType, string Category, string? Salary, string Url,
    DateTime PublishedAtUtc, IReadOnlyList<string> KeySkills, bool IsSaved, bool IsApplied,
    string? Snippet);

public record JobSearchRequest(
    string? Search, string? Location, string? Industry, string? EmploymentType,
    string? ExperienceLevel, bool? Remote, string? Sort, int Page, int PageSize);

public record PagedJobsDto(IReadOnlyList<JobListingDto> Jobs, int Total, int Page, int PageSize);

public record JobMatchDto(
    JobListingDto Job, int ScorePercent, IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingSkills, string Explanation, IReadOnlyList<string> Actions);

public record SkillGapReportDto(
    string TargetRole, double CoveragePercent,
    IReadOnlyList<SkillGapItem> Matched, IReadOnlyList<SkillGapItem> Missing,
    IReadOnlyList<string> RecommendedSteps);

public record SkillGapItem(string Skill, string? Level, string Priority, string JobsRequiring);

public record CvParseResponse(string ParseStatus, string? ParseError, int TextChars);

public record ApplicationDto(
    int Id, int? JobListingId, string CompanyName, string JobTitle, string? SourceUrl,
    string Status, string? Notes, DateTime? FollowUpDate, DateTime AppliedAtUtc, DateTime UpdatedAtUtc);

public record ApplicationCreateRequest(
    int? JobListingId, string? CompanyName, string? JobTitle, string? SourceUrl,
    string? Status, string? Notes, DateTime? FollowUpDate, DateTime? AppliedAtUtc);

public record ApplicationUpdateRequest(
    string? Status, string? Notes, DateTime? FollowUpDate);

public record ApplicationSummaryDto(
    int Total, IReadOnlyList<StatusCount> ByStatus, int UpcomingFollowUps,
    int Interviews);

public record StatusCount(string Status, int Count);

public record RoadmapItemDto(int Id, string Title, string? Category, string Status,
    string? Notes, DateTime? TargetDate, int SortOrder, string Origin, DateTime? CompletedAtUtc);

public record RoadmapItemCreateRequest(string Title, string? Category, string? Status,
    string? Notes, DateTime? TargetDate, int? SortOrder);

public record RoadmapItemUpdateRequest(string? Title, string? Category, string Status,
    string? Notes, DateTime? TargetDate, int? SortOrder);

public record RoadmapProgressDto(int Total, int NotStarted, int InProgress, int Completed, int Percent);

public record RoadmapSuggestionDto(string Title, string Category, string? Notes, string? Why);

public record InterviewQuestionDto(
    int Id, string QuestionText, string? AnswerText, string? Feedback, int? FeedbackScore);

public record InterviewSessionDto(
    int Id, string TargetRole, int? JobListingId, string QuestionSource,
    string? ProviderName, string? Model, DateTime CreatedAtUtc,
    int AnsweredCount, int TotalCount, IReadOnlyList<InterviewQuestionDto>? Questions = null);

public record InterviewSessionCreateRequest(string? TargetRole, int? JobListingId, bool? UseAi);

public record InterviewAnswerRequest(string AnswerText);

public record GraduateOverviewDto(
    string FirstName, string FullName,
    int ProfileCompletionPercent, IReadOnlyList<string> ProfileMissingFields,
    string? CvStatus, DateTime? CvUploadedAtUtc, int? CvAnalysisScore,
    int MatchingJobsCount, int MatchingJobsAboveThreshold,
    int SavedJobsCount, int ApplicationsCount, int ApplicationsInProgress,
    int UpcomingFollowUpsCount, int RoadmapProgressPercent,
    IReadOnlyList<SkillGapItem> TopSkillGaps, IReadOnlyList<string> NextActions,
    IReadOnlyList<GradEventDto> UpcomingEvents, IReadOnlyList<GradActivityDto> RecentActivity,
    int UnreadNotifications, int PreviousAnalysisCount);

public record GradEventDto(int Id, string Title, string Location, DateTime StartsAtUtc, string? Description);

public record GradActivityDto(int Id, string Kind, string Message, DateTime CreatedAtUtc);

public record GradAnnouncementDto(int Id, string Title, string Body, string? Category, DateTime CreatedAtUtc);

public record GradAlumniDto(
    string? UniversityName, string Department, string? GraduationYear, string? Degree,
    IReadOnlyList<GradEventDto> CareerEvents, IReadOnlyList<GradAnnouncementDto> Announcements,
    string CareerCenterTip, IReadOnlyList<string> Contacts);

public record GradInsightsDto(
    int JobsIndexed, DateTime? LastSyncUtc,
    IReadOnlyList<NameCount> TopSkills, IReadOnlyList<NameCount> JobsByIndustry,
    IReadOnlyList<NameCount> JobsByWorkType, IReadOnlyList<string> RecentTitles);

public record NameCount(string Name, int Count);

public record GradSettingsDto(
    bool NotifyNewMatches, bool NotifyFollowUps, int MatchAlertThresholdPercent, bool ShowGpaOnProfile);
