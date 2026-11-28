namespace UniSphere.Api.Dtos;

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
    IReadOnlyList<ProfileEntryDto> Entries,
    string? TargetRole = null, string? PrimaryStruggle = null, string? PreferredWorkMode = null,
    string? PreferredCity = null, bool HasCompletedOnboarding = false, string? CareerChallenges = null);

public record GraduateProfileUpdateRequest(
    string? Headline, string? CareerSummary,
    string? LinkedInUrl, string? GitHubUrl, string? PortfolioUrl,
    string? PreferredJobTitles, string? PreferredIndustries, string? PreferredLocations,
    string? WorkModes, string? EmploymentType, string? ExperienceLevel, string? Languages, string? CareerGoal, string? CareerChallenges = null);

public record ProfileCompletionDto(int Percent, IReadOnlyList<string> MissingFields, IReadOnlyList<string> NextActions);

public record CvDocumentDto(
    int Id, string FileName, string ContentType, long SizeBytes, string ParseStatus,
    string? ParseError, DateTime UploadedAtUtc, bool HasAnalysis, int? AnalysisId, int TextChars);

public record CvAnalysisDto(
    int Id, int CvDocumentId, string? OverallAssessment, int? ScorePercent,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Suggestions, IReadOnlyList<KeywordRewrite> Rewrites,
    IReadOnlyList<string> Keywords, string ProviderName, string Model, DateTime CreatedAtUtc,
    IReadOnlyList<string>? TechnicalSkills = null, IReadOnlyList<string>? SoftSkills = null,
    IReadOnlyList<string>? Sections = null, IReadOnlyList<string>? ParsingRisks = null,
    bool IsDeterministicFallback = false, int? AiScore = null,
    IReadOnlyList<UniSphere.Api.Services.CvScoreItem>? ScoreBreakdown = null,
    UniSphere.Api.Services.CvJobFit? JobFit = null);

public record KeywordRewrite(string Original, string Rewritten, string Why);

public record CvAnalyzeRequest(string? TargetRole, string? JobDescription = null);

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
    string Status, string? Notes, DateTime? FollowUpDate, DateTime AppliedAtUtc, DateTime UpdatedAtUtc,
    DateTime? InterviewAtUtc = null);

public record ApplicationCreateRequest(
    int? JobListingId, string? CompanyName, string? JobTitle, string? SourceUrl,
    string? Status, string? Notes, DateTime? FollowUpDate, DateTime? AppliedAtUtc, DateTime? InterviewAtUtc = null);

public record ApplicationUpdateRequest(
    string? Status, string? Notes, DateTime? FollowUpDate, DateTime? InterviewAtUtc = null);

public record ApplicationSummaryDto(
    int Total, IReadOnlyList<StatusCount> ByStatus, int UpcomingFollowUps,
    int Interviews);

public record StatusCount(string Status, int Count);

public record ApplicationAnalyticsDto(
    int Total, int ReachedInterview, int? InterviewRatePercent, IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<NameCount> TopRoles, IReadOnlyList<NameCount> UnevidencedSkills, IReadOnlyList<string> Insights);

public record RoadmapItemDto(int Id, string Title, string? Category, string Status,
    string? Notes, DateTime? TargetDate, int SortOrder, string Origin, DateTime? CompletedAtUtc);

public record RoadmapItemCreateRequest(string Title, string? Category, string? Status,
    string? Notes, DateTime? TargetDate, int? SortOrder);

public record RoadmapItemUpdateRequest(string? Title, string? Category, string Status,
    string? Notes, DateTime? TargetDate, int? SortOrder);

public record RoadmapProgressDto(int Total, int NotStarted, int InProgress, int Completed, int Percent);

public record RoadmapSuggestionDto(string Title, string Category, string? Notes, string? Why);

public record RubricItemDto(string Criterion, int Score);

public record InterviewFeedbackDetails(
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Improvements, IReadOnlyList<RubricItemDto> Rubric,
    string? StrongerAnswer = null);

public record InterviewQuestionDto(
    int Id, string QuestionText, string? AnswerText, string? Feedback, int? FeedbackScore,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Improvements, IReadOnlyList<RubricItemDto> Rubric,
    string? StrongerAnswer = null);

public record InterviewSessionDto(
    int Id, string TargetRole, int? JobListingId, string QuestionSource,
    string? ProviderName, string? Model, DateTime CreatedAtUtc,
    int AnsweredCount, int TotalCount, IReadOnlyList<InterviewQuestionDto>? Questions = null,
    string InterviewType = "Technical", string Difficulty = "Medium");

public record InterviewSessionCreateRequest(
    string? TargetRole, int? JobListingId, bool? UseAi, string? InterviewType = null, string? Difficulty = null);

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
    int UnreadNotifications, int PreviousAnalysisCount,
    bool HasCompletedOnboarding = false, int JobReadinessPercent = 0, int Xp = 0);

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
    bool NotifyNewMatches, bool NotifyFollowUps, int MatchAlertThresholdPercent,
    bool ShowGpaOnProfile,
    string ThemePreference = "system",
    string? AlertKeywords = null,
    string? AlertLocations = null);

public record OnboardingRequest(
    string TargetRole, string ExperienceLevel, string PrimaryStruggle, string PreferredWorkMode, string? PreferredCity);

public record OnboardingStatusDto(
    bool HasCompletedOnboarding, string? TargetRole, string? ExperienceLevel, string? PrimaryStruggle,
    string? PreferredWorkMode, string? PreferredCity,
    IReadOnlyList<string> Roles, IReadOnlyList<string> ExperienceLevels,
    IReadOnlyList<string> Struggles, IReadOnlyList<string> WorkModes);

public record QuestLevelDto(int Level, string Title, string Topic, string Status);

public record QuestDto(
    string TargetRole, int Xp, int QuizAttempts, int ReadinessPercent, int PassScore,
    IReadOnlyList<QuestLevelDto> Levels);

public record QuizQuestionDto(string Question, IReadOnlyList<string> Options);

public record QuizDto(int Level, IReadOnlyList<QuizQuestionDto> Questions);

public record QuizSubmitRequest(IReadOnlyList<int>? Answers);

public record QuizReviewDto(bool Correct, int CorrectIndex, string Explanation);

public record QuizResultDto(
    bool Passed, int Score, int Total, int XpAwarded, int ReadinessPercent, IReadOnlyList<QuizReviewDto> Review);

public record PrepItemDto(string Concept, string Detail, bool Reviewed = false);

public record LevelPrepDto(int Level, IReadOnlyList<PrepItemDto> Items);

public record PrepReviewedRequest(bool Reviewed);

public record RecoveryDrillRequest(int SessionId);

public record RecoveryDrillDto(string DrillId, string Dimension, IReadOnlyList<string> Questions, int BonusXp);

public record DrillAnswerRequest(string DrillId, int Index, string AnswerText);

public record DrillAnswerDto(
    string Feedback, int Score, IReadOnlyList<string> Improvements, int Answered, bool Completed, int XpAwarded,
    double Baseline, double DrillAverage, bool Improved);

public record BulletImprovementDto(string Keyword, string? Original, string Improved);

public record JobPrepDto(
    IReadOnlyList<string> MissingKeywords, IReadOnlyList<BulletImprovementDto> BulletImprovements,
    InterviewSessionDto Interview);

public record OutreachRequest(string? Tone);

public record OutreachDto(string Tone, string CoverLetter, string Pitch);
