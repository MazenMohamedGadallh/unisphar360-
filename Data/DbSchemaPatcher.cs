using Microsoft.EntityFrameworkCore;

namespace UniSphere.Api.Data;

/// <summary>
/// EnsureCreated() never ALTERs a database that already exists, so objects added
/// to the model after a database was first created are missing in older files.
/// This patcher runs CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS for
/// every object added later. It is a no-op on fresh databases (EnsureCreated
/// created those objects) and on already-patched databases.
/// </summary>
public static class DbSchemaPatcher
{
    public static void Patch(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) conn.Open();
        try
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS GraduationApplications (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    Degree TEXT NOT NULL DEFAULT '',
                    Department TEXT NOT NULL DEFAULT '',
                    Note TEXT,
                    ExpectedGraduationYear INTEGER,
                    GpaAtApplication REAL NOT NULL,
                    CoursesCompleted INTEGER NOT NULL,
                    CoursesRequired INTEGER NOT NULL,
                    DecisionNote TEXT,
                    ReviewedByUserId INTEGER,
                    ReviewedAtUtc TEXT,
                    ConferredGraduationYear INTEGER,
                    SubmittedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_GraduationApplications_UserId ON GraduationApplications(UserId);
                CREATE INDEX IF NOT EXISTS IX_GraduationApplications_Status ON GraduationApplications(Status);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_GraduationApplications_UserId_Status
                    ON GraduationApplications(UserId)
                    WHERE Status IN ('Pending','InReview');

                CREATE TABLE IF NOT EXISTS GraduateProfiles (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Headline TEXT,
                    CareerSummary TEXT,
                    LinkedInUrl TEXT,
                    GitHubUrl TEXT,
                    PortfolioUrl TEXT,
                    PreferredJobTitles TEXT,
                    PreferredIndustries TEXT,
                    PreferredLocations TEXT,
                    WorkModes TEXT NOT NULL DEFAULT '',
                    EmploymentType TEXT NOT NULL DEFAULT '',
                    ExperienceLevel TEXT NOT NULL DEFAULT '',
                    Languages TEXT,
                    CareerGoal TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_GraduateProfiles_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_GraduateProfiles_UserId ON GraduateProfiles(UserId);

                CREATE TABLE IF NOT EXISTS ProfileEntries (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Kind TEXT NOT NULL DEFAULT 'Experience',
                    Title TEXT NOT NULL,
                    Organization TEXT,
                    Description TEXT,
                    Period TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_ProfileEntries_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ProfileEntries_UserId ON ProfileEntries(UserId);
                CREATE INDEX IF NOT EXISTS IX_ProfileEntries_UserId_Kind ON ProfileEntries(UserId, Kind);

                CREATE TABLE IF NOT EXISTS GraduateSkills (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Level TEXT,
                    Source TEXT NOT NULL DEFAULT 'Profile',
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_GraduateSkills_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_GraduateSkills_UserId_Name ON GraduateSkills(UserId, Name);
                CREATE INDEX IF NOT EXISTS IX_GraduateSkills_UserId ON GraduateSkills(UserId);

                CREATE TABLE IF NOT EXISTS CvDocuments (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    FileName TEXT NOT NULL,
                    ContentType TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    StoragePath TEXT NOT NULL,
                    ParseStatus TEXT NOT NULL DEFAULT 'Pending',
                    ExtractedText TEXT,
                    ManualText TEXT,
                    ParseError TEXT,
                    UploadedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_CvDocuments_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_CvDocuments_UserId ON CvDocuments(UserId);

                CREATE TABLE IF NOT EXISTS CvAnalyses (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CvDocumentId INTEGER NOT NULL,
                    OverallAssessment TEXT,
                    ScorePercent INTEGER,
                    StrengthsJson TEXT NOT NULL DEFAULT '[]',
                    WeaknessesJson TEXT NOT NULL DEFAULT '[]',
                    SuggestionsJson TEXT NOT NULL DEFAULT '[]',
                    RewritesJson TEXT NOT NULL DEFAULT '[]',
                    KeywordsJson TEXT NOT NULL DEFAULT '[]',
                    ProviderName TEXT NOT NULL DEFAULT '',
                    Model TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_CvAnalyses_CvDocuments_CvDocumentId FOREIGN KEY (CvDocumentId) REFERENCES CvDocuments (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_CvAnalyses_CvDocumentId ON CvAnalyses(CvDocumentId);

                CREATE TABLE IF NOT EXISTS JobListings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Source TEXT NOT NULL DEFAULT 'Demo',
                    SourceId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    CompanyName TEXT NOT NULL,
                    Location TEXT NOT NULL DEFAULT '',
                    Remote INTEGER NOT NULL DEFAULT 0,
                    JobType TEXT NOT NULL DEFAULT 'full_time',
                    Category TEXT NOT NULL DEFAULT '',
                    Salary TEXT,
                    Url TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    KeySkillsCsv TEXT NOT NULL DEFAULT '',
                    PublishedAtUtc TEXT NOT NULL,
                    FetchedAtUtc TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_JobListings_Source_SourceId ON JobListings(Source, SourceId);
                CREATE INDEX IF NOT EXISTS IX_JobListings_Category ON JobListings(Category);
                CREATE INDEX IF NOT EXISTS IX_JobListings_PublishedAtUtc ON JobListings(PublishedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_JobListings_Remote ON JobListings(Remote);

                CREATE TABLE IF NOT EXISTS SavedJobs (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    JobListingId INTEGER NOT NULL,
                    SavedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_SavedJobs_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_SavedJobs_JobListings_JobListingId FOREIGN KEY (JobListingId) REFERENCES JobListings (Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_SavedJobs_UserId_JobListingId ON SavedJobs(UserId, JobListingId);

                CREATE TABLE IF NOT EXISTS JobApplications (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    JobListingId INTEGER,
                    CompanyName TEXT NOT NULL,
                    JobTitle TEXT NOT NULL,
                    SourceUrl TEXT,
                    Status TEXT NOT NULL DEFAULT 'Applied',
                    Notes TEXT,
                    FollowUpDate TEXT,
                    AppliedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_JobApplications_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_JobApplications_JobListings_JobListingId FOREIGN KEY (JobListingId) REFERENCES JobListings (Id) ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS IX_JobApplications_UserId ON JobApplications(UserId);
                CREATE INDEX IF NOT EXISTS IX_JobApplications_Status ON JobApplications(Status);

                CREATE TABLE IF NOT EXISTS RoadmapItems (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    Category TEXT,
                    Status TEXT NOT NULL DEFAULT 'NotStarted',
                    Notes TEXT,
                    TargetDate TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    Origin TEXT NOT NULL DEFAULT 'Manual',
                    CompletedAtUtc TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_RoadmapItems_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_RoadmapItems_UserId_SortOrder ON RoadmapItems(UserId, SortOrder);

                CREATE TABLE IF NOT EXISTS InterviewSessions (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    TargetRole TEXT NOT NULL,
                    JobListingId INTEGER,
                    QuestionSource TEXT NOT NULL DEFAULT 'Curated',
                    ProviderName TEXT,
                    Model TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_InterviewSessions_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_InterviewSessions_UserId ON InterviewSessions(UserId);

                CREATE TABLE IF NOT EXISTS InterviewQuestions (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    QuestionText TEXT NOT NULL,
                    AnswerText TEXT,
                    Feedback TEXT,
                    FeedbackScore INTEGER,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_InterviewQuestions_InterviewSessions_SessionId FOREIGN KEY (SessionId) REFERENCES InterviewSessions (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_InterviewQuestions_SessionId ON InterviewQuestions(SessionId);

                CREATE TABLE IF NOT EXISTS GraduateSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    NotifyNewMatches INTEGER NOT NULL DEFAULT 1,
                    NotifyFollowUps INTEGER NOT NULL DEFAULT 1,
                    MatchAlertThresholdPercent INTEGER NOT NULL DEFAULT 80,
                    ShowGpaOnProfile INTEGER NOT NULL DEFAULT 0,
                    ThemePreference TEXT NOT NULL DEFAULT 'system',
                    CONSTRAINT FK_GraduateSettings_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_GraduateSettings_UserId ON GraduateSettings(UserId);

                CREATE TABLE IF NOT EXISTS JobSyncStates (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Source TEXT NOT NULL DEFAULT 'Remotive',
                    LastSyncUtc TEXT,
                    LastStatus TEXT,
                    LastMessage TEXT,
                    LastImportedCount INTEGER NOT NULL DEFAULT 0
                );
                """;
            cmd.ExecuteNonQuery();

            EnsureTableColumn(cmd, "Notifications", "Category",
                "ALTER TABLE Notifications ADD COLUMN Category TEXT NOT NULL DEFAULT 'System'");
            EnsureTableColumn(cmd, "Notifications", "DedupeKey",
                "ALTER TABLE Notifications ADD COLUMN DedupeKey TEXT");
            // Graduate preferences added after the settings row was introduced.
            // Split joining the alert keywords/locations and the UI theme sync.
            EnsureTableColumn(cmd, "GraduateSettings", "ThemePreference",
                "ALTER TABLE GraduateSettings ADD COLUMN ThemePreference TEXT NOT NULL DEFAULT 'system'");
            EnsureTableColumn(cmd, "GraduateSettings", "AlertKeywords",
                "ALTER TABLE GraduateSettings ADD COLUMN AlertKeywords TEXT NULL");
            EnsureTableColumn(cmd, "GraduateSettings", "AlertLocations",
                "ALTER TABLE GraduateSettings ADD COLUMN AlertLocations TEXT NULL");
            // Doctor module objects reintroduced alongside the graduate system:
            // course assignments (join table), research-hub papers and the
            // course-scoped announcement columns. All additive; existing rows
            // are unaffected (CourseId stays NULL = university-wide).
            EnsureTableColumn(cmd, "Announcements", "CourseId",
                "ALTER TABLE Announcements ADD COLUMN CourseId INTEGER NULL");
            EnsureTableColumn(cmd, "Users", "Address",
                "ALTER TABLE Users ADD COLUMN Address TEXT NULL");
            EnsureTableColumn(cmd, "CourseMaterials", "DeadlineUtc",
                "ALTER TABLE CourseMaterials ADD COLUMN DeadlineUtc TEXT NULL");

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS CourseAssignments (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    DoctorUserId INTEGER NOT NULL,
                    CourseId INTEGER NOT NULL,
                    AssignedByUserId INTEGER,
                    AssignedAtUtc TEXT NOT NULL,
                    DisplayTitle TEXT,
                    FOREIGN KEY (CourseId) REFERENCES Courses (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_CourseAssignments_DoctorUserId ON CourseAssignments(DoctorUserId);
                CREATE INDEX IF NOT EXISTS IX_CourseAssignments_CourseId ON CourseAssignments(CourseId);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_CourseAssignments_DoctorUserId_CourseId
                    ON CourseAssignments(DoctorUserId, CourseId);

                CREATE TABLE IF NOT EXISTS DoctorResearches (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    DoctorUserId INTEGER NOT NULL,
                    UniversityId INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    Abstract TEXT NOT NULL,
                    Authors TEXT NOT NULL,
                    Keywords TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Field TEXT NOT NULL,
                    PdfPath TEXT,
                    DoiOrUrl TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    IsPublished INTEGER NOT NULL,
                    FOREIGN KEY (DoctorUserId) REFERENCES Users (Id) ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS IX_DoctorResearches_DoctorUserId ON DoctorResearches(DoctorUserId);
                CREATE INDEX IF NOT EXISTS IX_DoctorResearches_UniversityId ON DoctorResearches(UniversityId);
                CREATE INDEX IF NOT EXISTS IX_DoctorResearches_Year ON DoctorResearches(Year);
                CREATE INDEX IF NOT EXISTS IX_DoctorResearches_Title ON DoctorResearches(Title);
                CREATE INDEX IF NOT EXISTS IX_DoctorResearches_IsPublished ON DoctorResearches(IsPublished);
                CREATE INDEX IF NOT EXISTS IX_Announcements_CourseId ON Announcements(CourseId);
                """;
            cmd.ExecuteNonQuery();
            // Partial unique index: NULL keys never collide, generated career
            // rows do. Runs on every startup (no-op when it already exists).
            using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText = """
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_Notifications_UserId_DedupeKey
                        ON Notifications(UserId, DedupeKey)
                        WHERE DedupeKey IS NOT NULL;
                    """;
                idx.ExecuteNonQuery();
            }
            tx.Commit();
        }
        finally
        {
            if (!wasOpen) conn.Close();
        }
    }

    /// <summary>Adds a column to an existing table (SQL Server-style IF NOT
    /// EXISTS is not available for ALTER TABLE ADD COLUMN in SQLite). No-op
    /// on fresh databases where EnsureCreated already created the column.</summary>
    private static void EnsureTableColumn(System.Data.Common.DbCommand cmd, string table, string column, string alterSql)
    {
        bool exists = false;
        using (var probe = cmd.Connection!.CreateCommand())
        {
            probe.Transaction = cmd.Transaction;
            probe.CommandText = $"PRAGMA table_info({table});";
            using var reader = probe.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }
        if (!exists)
        {
            cmd.CommandText = alterSql;
            cmd.ExecuteNonQuery();
        }
    }
}
