using UniSphere.Api.Models;

namespace UniSphere.Api.Models;

public class GraduateProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string? Headline { get; set; }            // "Backend Engineer | .NET & APIs"
    public string? CareerSummary { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    public string? PreferredJobTitles { get; set; }   // "Backend Developer, Data Analyst"
    public string? PreferredIndustries { get; set; }  // "Tech, Fintech"
    public string? PreferredLocations { get; set; }   // "Cairo, Egypt, Remote"
    public string WorkModes { get; set; } = "";       // CSV: Remote,Hybrid,Onsite
    public string EmploymentType { get; set; } = "";  // Full-time | Part-time | Internship
    public string ExperienceLevel { get; set; } = ""; // Entry-level | Mid | Senior
    public string? Languages { get; set; }            // "English (Fluent), Arabic (Native)"
    public string? CareerGoal { get; set; }

    // Career calibration onboarding (ExperienceLevel above is part of it too).
    public string? TargetRole { get; set; }
    public string? PrimaryStruggle { get; set; }      // CareerCatalog.Struggles
    public string? PreferredWorkMode { get; set; }    // Remote | Hybrid | Onsite
    public string? PreferredCity { get; set; }
    public bool HasCompletedOnboarding { get; set; }
    public string? CareerChallenges { get; set; }
    public string? CareerDnaJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ProfileEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Kind { get; set; } = "Experience";
    public string Title { get; set; } = "";
    public string? Organization { get; set; }
    public string? Description { get; set; }
    // Display period, e.g. "2022 - 2023" or date ranges kept flexible.
    public string? Period { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GraduateSkill
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = "";
    public string? Level { get; set; }
    public string Source { get; set; } = "Profile";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CvDocument
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string FileName { get; set; } = "";       // original display name, sanitized
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = "";    // server-side path, never exposed
    public string ParseStatus { get; set; } = "Pending"; // Parsed | Failed | Manual | UploadOnly
    public string? ExtractedText { get; set; }
    public string? ManualText { get; set; }
    public string? ParseError { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CvAnalysis
{
    public int Id { get; set; }
    public int CvDocumentId { get; set; }
    public CvDocument? CvDocument { get; set; }

    public string? OverallAssessment { get; set; }
    public int? ScorePercent { get; set; }           // CV quality estimate 0-100
    public string StrengthsJson { get; set; } = "[]";
    public string WeaknessesJson { get; set; } = "[]";
    public string SuggestionsJson { get; set; } = "[]";
    public string RewritesJson { get; set; } = "[]";  // [{ from, to, why }]
    public string KeywordsJson { get; set; } = "[]";  // keywords for target role
    public string ReportJson { get; set; } = "{}";    // rich ATS evidence/report data

    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class JobListing
{
    public int Id { get; set; }
    public string Source { get; set; } = "Demo";     // Remotive | Demo
    public string SourceId { get; set; } = "";       // provider's id, stable per source
    public string Title { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Location { get; set; } = "";       // "candidate_required_location"
    public bool Remote { get; set; }
    public string JobType { get; set; } = "full_time";
    public string Category { get; set; } = "";       // industry bucket
    public string? Salary { get; set; }
    public string Url { get; set; } = "";            // original application link
    public string Description { get; set; } = "";
    public string KeySkillsCsv { get; set; } = "";   // skill tokens detected in the text
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;       // delisted/expired flag
}

public class SavedJob
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int JobListingId { get; set; }
    public JobListing? JobListing { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public class JobApplication
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int? JobListingId { get; set; }           // optional link to a catalog job
    public JobListing? JobListing { get; set; }

    public string CompanyName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string Status { get; set; } = "Applied";  // Applied | Interview | Offer | Rejected | Withdrawn
    public string? Notes { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public DateTime? InterviewAtUtc { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RoadmapItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Title { get; set; } = "";
    public string? Category { get; set; }            // Milestone | Course | Project | Application | Skill
    public string Status { get; set; } = "NotStarted"; // NotStarted | InProgress | Completed
    public string? Notes { get; set; }
    public DateTime? TargetDate { get; set; }
    public int SortOrder { get; set; }
    public string Origin { get; set; } = "Manual";   // Manual | AiSuggestion
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class InterviewSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string TargetRole { get; set; } = "";
    public int? JobListingId { get; set; }
    public string QuestionSource { get; set; } = "Curated"; // Curated | Ai
    public string InterviewType { get; set; } = "Technical";   // HR | Technical | Behavioral
    public string Difficulty { get; set; } = "Medium";         // Easy | Medium | Hard
    public string? ProviderName { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class InterviewQuestion
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public InterviewSession? Session { get; set; }
    public string QuestionText { get; set; } = "";
    public string? AnswerText { get; set; }
    public string? Feedback { get; set; }
    public int? FeedbackScore { get; set; }          // 0-10 estimate of the answer
    public string? FeedbackDetailsJson { get; set; }  // strengths, improvements, rubric
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GraduateQuestProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int UnlockedLevel { get; set; } = 1;       // level count + 1 once every level is passed
    public int Xp { get; set; }
    public int QuizAttempts { get; set; }
    public int ReadinessPercent { get; set; }
    public string? PendingQuizJson { get; set; }      // answer key stays server-side
    public string? PendingDrillJson { get; set; }
    public string? SkippedLevelsCsv { get; set; }     // unlocked past, but not passed
    public string? PrepJson { get; set; }             // cached level prep guides and reviewed marks
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GraduateSettings
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public bool NotifyNewMatches { get; set; } = true;
    public bool NotifyFollowUps { get; set; } = true;
    public int MatchAlertThresholdPercent { get; set; } = 80;
    public bool ShowGpaOnProfile { get; set; } = false;  // optional, privacy-first
    public string ThemePreference { get; set; } = "system";
    public string? AlertKeywords { get; set; }
    public string? AlertLocations { get; set; }
}

public class JobSyncState
{
    public int Id { get; set; }                      // always 1
    public string Source { get; set; } = "Remotive";
    public DateTime? LastSyncUtc { get; set; }
    public string? LastStatus { get; set; }          // OK | Failed
    public string? LastMessage { get; set; }
    public int LastImportedCount { get; set; }
}

public class PortfolioProject
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Problem { get; set; }
    public string? Technologies { get; set; }   // CSV
    public string? Role { get; set; }
    public string? Outcomes { get; set; }
    public string? Skills { get; set; }         // CSV
    public string? GitHubUrl { get; set; }
    public string? DemoUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SkillChallengeAttempt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Skill { get; set; } = "";
    public string Difficulty { get; set; } = "Medium";
    public string ChallengeJson { get; set; } = "{}";   // includes the answer key; never sent as is
    public string? SubmissionJson { get; set; }
    public int? Score { get; set; }                     // 0-100
    public bool Passed { get; set; }
    public string? Feedback { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
}

public class XpTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Key { get; set; } = "";       // mission id; unique per user so it is paid once
    public int Amount { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CareerReport
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string ReportJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
