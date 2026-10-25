using UniSphere.Api.Models;

namespace UniSphere.Api.Models;

// ---------------------------------------------------------------------------
// Graduate career platform — every record is 1:1 or owned by one authenticated
// account (UserId FK CASCADE), mirroring the ownership model of the other
// modules. Each application table extends this ownership class so an entire
// graduate's data is removable in one cascade.
// ---------------------------------------------------------------------------

/// <summary>Professional profile of one graduate (alumni) account.
/// Header identity data (name/major/degree/university/graduation year) stays
/// on User + the graduation application; this table only holds career fields.</summary>
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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A repeatable profile block (work experience, internship, project,
/// certification or education) for one graduate profile.</summary>
public class ProfileEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    /// <summary>Experience | Project | Certification | Education</summary>
    public string Kind { get; set; } = "Experience";
    public string Title { get; set; } = "";
    public string? Organization { get; set; }
    public string? Description { get; set; }
    // Display period, e.g. "2022 - 2023" or date ranges kept flexible.
    public string? Period { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A stated/professed skill with proficiency. Name normalization
/// (trim/case) is enforced by the service so dedup stays deterministic.</summary>
public class GraduateSkill
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Beginner | Intermediate | Advanced | Expert (optional claim).</summary>
    public string? Level { get; set; }
    /// <summary>Where the skill was stated first: Profile, CV or System (profile default).</summary>
    public string Source { get; set; } = "Profile";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>An uploaded (or manually pasted) CV document owned by one graduate.</summary>
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
    /// <summary>Extracted plain text (truncated to a sane size) for analysis.</summary>
    public string? ExtractedText { get; set; }
    /// <summary>User-typed text replacing/adding to extraction when parsing failed.</summary>
    public string? ManualText { get; set; }
    public string? ParseError { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One persisted AI CV analysis for one CV. Structured findings are
/// stored as JSON strings so SQLite stays strictly typed.</summary>
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

    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A normalized external (or demo-seeded) job listing.
/// Source="Remotive" rows are live external records; Source="Demo" rows are
/// clearly-labeled seed data for offline development.</summary>
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

/// <summary>A job a graduate bookmarked (Saved) — separate from applications.</summary>
public class SavedJob
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int JobListingId { get; set; }
    public JobListing? JobListing { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Application tracker record for one graduate. Applications added
/// here are tracked in UniSphere by the graduate; nothing is ever claimed as
/// submitted to an employer unless an integration performs the submit.</summary>
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
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One career-development goal item on the graduate's roadmap.</summary>
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

/// <summary>Interview practice session for one graduate (one target role/job).</summary>
public class InterviewSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string TargetRole { get; set; } = "";
    public int? JobListingId { get; set; }
    public string QuestionSource { get; set; } = "Curated"; // Curated | Ai
    public string? ProviderName { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>An interview question with an optional submitted answer and AI feedback.</summary>
public class InterviewQuestion
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public InterviewSession? Session { get; set; }
    public string QuestionText { get; set; } = "";
    public string? AnswerText { get; set; }
    public string? Feedback { get; set; }
    public int? FeedbackScore { get; set; }          // 0-10 estimate of the answer
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Graduate per-account preferences (alerts, visibility, thresholds).</summary>
public class GraduateSettings
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public bool NotifyNewMatches { get; set; } = true;
    public bool NotifyFollowUps { get; set; } = true;
    public int MatchAlertThresholdPercent { get; set; } = 80;
    public bool ShowGpaOnProfile { get; set; } = false;  // optional, privacy-first
    /// <summary>UI theme preference persisted per account (light | dark | system);
    /// localStorage remains the per-browser convenience layer by ThemeService.</summary>
    public string ThemePreference { get; set; } = "system";
    /// <summary>Job-alert targeting: comma-separated keywords matched against the
    /// job title/company/description. Null/empty = no keyword filter.</summary>
    public string? AlertKeywords { get; set; }
    /// <summary>Job-alert targeting: comma-separated locations matched against the
    /// job location. Null/empty = no location filter.</summary>
    public string? AlertLocations { get; set; }
}

/// <summary>Single-row state for job catalog sync (throttling last sync).</summary>
public class JobSyncState
{
    public int Id { get; set; }                      // always 1
    public string Source { get; set; } = "Remotive";
    public DateTime? LastSyncUtc { get; set; }
    public string? LastStatus { get; set; }          // OK | Failed
    public string? LastMessage { get; set; }
    public int LastImportedCount { get; set; }
}
