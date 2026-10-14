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

        // Configure relationships
        modelBuilder.Entity<Enrollment>()
            .HasOne(e => e.Course)
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

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
