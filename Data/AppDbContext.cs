using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Models;

namespace UniSphere.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RoleItem> Roles => Set<RoleItem>();
    public DbSet<ModuleItem> Modules => Set<ModuleItem>();
    public DbSet<EventItem> Events => Set<EventItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<SemesterRecord> SemesterRecords => Set<SemesterRecord>();
    public DbSet<StudyPlanItem> StudyPlanItems => Set<StudyPlanItem>();
    public DbSet<ActivityItem> Activities => Set<ActivityItem>();
    public DbSet<LoginRecord> LoginRecords => Set<LoginRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<University> Universities => Set<University>();
    public DbSet<CourseMaterial> CourseMaterials => Set<CourseMaterial>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatUsage> ChatUsages => Set<ChatUsage>();
    public DbSet<CareerRecommendation> CareerRecommendations => Set<CareerRecommendation>();
    public DbSet<ResearchPaper> ResearchPapers => Set<ResearchPaper>();
    public DbSet<GraduationApplication> GraduationApplications => Set<GraduationApplication>();
    public DbSet<GraduateProfile> GraduateProfiles => Set<GraduateProfile>();
    public DbSet<ProfileEntry> ProfileEntries => Set<ProfileEntry>();
    public DbSet<GraduateSkill> GraduateSkills => Set<GraduateSkill>();
    public DbSet<CvDocument> CvDocuments => Set<CvDocument>();
    public DbSet<CvAnalysis> CvAnalyses => Set<CvAnalysis>();
    public DbSet<JobListing> JobListings => Set<JobListing>();
    public DbSet<SavedJob> SavedJobs => Set<SavedJob>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<RoadmapItem> RoadmapItems => Set<RoadmapItem>();
    public DbSet<InterviewSession> InterviewSessions => Set<InterviewSession>();
    public DbSet<InterviewQuestion> InterviewQuestions => Set<InterviewQuestion>();
    public DbSet<GraduateSettings> GraduateSettings => Set<GraduateSettings>();
    public DbSet<JobSyncState> JobSyncStates => Set<JobSyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User indexes
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // Enrollment indexes + unique constraint
        modelBuilder.Entity<Enrollment>()
            .HasIndex(e => e.UserId);
        modelBuilder.Entity<Enrollment>()
            .HasIndex(e => e.CourseId);
        modelBuilder.Entity<Enrollment>()
            .HasIndex(e => new { e.UserId, e.CourseId }).IsUnique();

        // Notification indexes
        modelBuilder.Entity<Notification>()
            .HasIndex(n => n.UserId);
        modelBuilder.Entity<Notification>()
            .HasIndex(n => n.IsRead);

        // Study plan indexes
        modelBuilder.Entity<StudyPlanItem>()
            .HasIndex(s => s.UserId);
        modelBuilder.Entity<StudyPlanItem>()
            .HasIndex(s => s.Date);

        // Activity indexes
        modelBuilder.Entity<ActivityItem>()
            .HasIndex(a => a.UserId);
        modelBuilder.Entity<ActivityItem>()
            .HasIndex(a => a.CreatedAtUtc);

        // Login record indexes
        modelBuilder.Entity<LoginRecord>()
            .HasIndex(l => l.UserId);
        modelBuilder.Entity<LoginRecord>()
            .HasIndex(l => l.Timestamp);

        // Event indexes
        modelBuilder.Entity<EventItem>()
            .HasIndex(e => e.StartsAtUtc);

        // Audit log indexes
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.UserId);
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.Timestamp);

        // Announcement indexes
        modelBuilder.Entity<Announcement>()
            .HasIndex(a => a.CreatedAtUtc);

        // Career recommendation indexes
        modelBuilder.Entity<CareerRecommendation>()
            .HasIndex(c => c.UserId);

        // Research paper indexes
        modelBuilder.Entity<ResearchPaper>()
            .HasIndex(r => r.Title);

        // Graduation application indexes + "one active application per student" guard
        modelBuilder.Entity<GraduationApplication>()
            .HasIndex(g => g.UserId);
        modelBuilder.Entity<GraduationApplication>()
            .HasIndex(g => g.Status);
        modelBuilder.Entity<GraduationApplication>()
            .HasIndex(g => new { g.UserId, g.Status })
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending' OR \"Status\" = 'InReview'");

        // Configure relationships
        modelBuilder.Entity<Enrollment>()
            .HasOne(e => e.Course)
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GraduationApplication>()
            .HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ---- Graduate career platform relationships/indexes ----
        modelBuilder.Entity<GraduateProfile>().HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GraduateProfile>().HasIndex(p => p.UserId).IsUnique();

        modelBuilder.Entity<ProfileEntry>().HasIndex(p => p.UserId);
        modelBuilder.Entity<ProfileEntry>().HasIndex(p => new { p.UserId, p.Kind });

        modelBuilder.Entity<GraduateSkill>().HasIndex(s => new { s.UserId, s.Name }).IsUnique();
        modelBuilder.Entity<GraduateSkill>().HasIndex(s => s.UserId);

        modelBuilder.Entity<CvDocument>().HasIndex(c => c.UserId);
        modelBuilder.Entity<CvAnalysis>().HasIndex(a => a.CvDocumentId);
        modelBuilder.Entity<CvAnalysis>().HasOne(a => a.CvDocument).WithMany().HasForeignKey(a => a.CvDocumentId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobListing>().HasIndex(j => new { j.Source, j.SourceId }).IsUnique();
        modelBuilder.Entity<JobListing>().HasIndex(j => j.Category);
        modelBuilder.Entity<JobListing>().HasIndex(j => j.PublishedAtUtc);
        modelBuilder.Entity<JobListing>().HasIndex(j => j.Remote);

        modelBuilder.Entity<SavedJob>().HasIndex(s => new { s.UserId, s.JobListingId }).IsUnique();
        modelBuilder.Entity<SavedJob>().HasOne(s => s.JobListing).WithMany().HasForeignKey(s => s.JobListingId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobApplication>().HasIndex(a => a.UserId);
        modelBuilder.Entity<JobApplication>().HasIndex(a => a.Status);
        modelBuilder.Entity<JobApplication>().HasOne(a => a.JobListing).WithMany().HasForeignKey(a => a.JobListingId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RoadmapItem>().HasIndex(r => new { r.UserId, r.SortOrder });

        modelBuilder.Entity<InterviewSession>().HasIndex(s => s.UserId);
        modelBuilder.Entity<InterviewQuestion>().HasIndex(q => q.SessionId);
        modelBuilder.Entity<InterviewQuestion>().HasOne(q => q.Session).WithMany().HasForeignKey(q => q.SessionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GraduateSettings>().HasIndex(g => g.UserId).IsUnique();
        modelBuilder.Entity<GraduateSettings>().HasOne(g => g.User).WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CvDocument>().HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ProfileEntry>().HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GraduateSkill>().HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SavedJob>().HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JobApplication>().HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RoadmapItem>().HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<InterviewSession>().HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        // Chat indexes
        modelBuilder.Entity<ChatConversation>()
            .HasIndex(c => c.UserId);
        modelBuilder.Entity<ChatConversation>()
            .HasIndex(c => c.UpdatedAtUtc);
        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.ConversationId);
        modelBuilder.Entity<ChatUsage>()
            .HasIndex(u => new { u.UserId, u.Date }).IsUnique();
        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Conversation)
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Course material indexes + relationship
        modelBuilder.Entity<CourseMaterial>()
            .HasIndex(m => m.CourseId);
        modelBuilder.Entity<CourseMaterial>()
            .HasIndex(m => m.Type);
        modelBuilder.Entity<CourseMaterial>()
            .HasOne(m => m.Course)
            .WithMany()
            .HasForeignKey(m => m.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed roles if empty
        modelBuilder.Entity<RoleItem>().HasData(
            new RoleItem { Id = 1, Name = "Student" },
            new RoleItem { Id = 2, Name = "Graduate" },
            new RoleItem { Id = 3, Name = "Doctor" },
            new RoleItem { Id = 4, Name = "Admin" }
        );
    }
}
