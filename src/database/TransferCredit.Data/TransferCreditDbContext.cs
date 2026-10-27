using Microsoft.EntityFrameworkCore;
using TransferCredit.Data.Entities;

namespace TransferCredit.Data;

public class TransferCreditDbContext(DbContextOptions<TransferCreditDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TransferCase> Cases => Set<TransferCase>();
    public DbSet<SourceInstitution> Institutions => Set<SourceInstitution>();
    public DbSet<SourceCourse> SourceCourses => Set<SourceCourse>();
    public DbSet<BUCourse> BUCourses => Set<BUCourse>();
    public DbSet<SourceCatalogueCourse> SourceCatalogueCourses => Set<SourceCatalogueCourse>();
    public DbSet<ProgramRule> ProgramRules => Set<ProgramRule>();
    public DbSet<RuleSetting> RuleSettings => Set<RuleSetting>();
    public DbSet<EquivalencyRule> EquivalencyRules => Set<EquivalencyRule>();
    public DbSet<MatchCandidate> MatchCandidates => Set<MatchCandidate>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<IntakeMessage> IntakeMessages => Set<IntakeMessage>();
    public DbSet<MailSyncState> MailSyncStates => Set<MailSyncState>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store enums as readable strings so the database and audit trail are self-describing.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        builder.Properties<decimal>().HavePrecision(9, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
            e.Property(x => x.StudentRef).HasMaxLength(64);
            e.Property(x => x.PasswordHash).HasMaxLength(256);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });

        b.Entity<TransferCase>(e =>
        {
            e.Property(x => x.CaseRef).HasMaxLength(20);
            e.HasIndex(x => x.CaseRef).IsUnique();
            e.Property(x => x.DemoStudentRef).HasMaxLength(64);
            e.HasIndex(x => x.DemoStudentRef);
            e.Property(x => x.StudentName).HasMaxLength(200);
            e.Property(x => x.StudentEmail).HasMaxLength(320);
            e.HasIndex(x => x.StudentEmail);
            e.Property(x => x.Program).HasMaxLength(100);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Courses).WithOne(x => x.Case).HasForeignKey(x => x.CaseId);
            e.HasMany(x => x.Documents).WithOne(x => x.Case).HasForeignKey(x => x.CaseId);
        });

        b.Entity<SourceInstitution>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<SourceCourse>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Grade).HasMaxLength(5);
            e.Property(x => x.Term).HasMaxLength(40);
            e.HasOne(x => x.Institution).WithMany().HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AppliedEquivalencyRule).WithMany().HasForeignKey(x => x.AppliedEquivalencyRuleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Candidates).WithOne().HasForeignKey(x => x.SourceCourseId);
            e.HasMany(x => x.Evaluations).WithOne(x => x.SourceCourse).HasForeignKey(x => x.SourceCourseId);
        });

        b.Entity<BUCourse>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Program).HasMaxLength(100);
            e.Property(x => x.RequirementType).HasMaxLength(100);
            e.Property(x => x.SourceUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.Code, x.Program }).IsUnique();
        });

        b.Entity<SourceCatalogueCourse>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => new { x.InstitutionId, x.Code }).IsUnique();
            e.HasOne(x => x.Institution).WithMany().HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProgramRule>(e =>
        {
            e.Property(x => x.Program).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(500);
            e.HasOne(x => x.BUCourse).WithMany().HasForeignKey(x => x.BUCourseId);
        });

        b.Entity<RuleSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(200);
        });

        b.Entity<EquivalencyRule>(e =>
        {
            e.Property(x => x.SourceCode).HasMaxLength(40);
            e.Property(x => x.SourceTitle).HasMaxLength(200);
            e.Property(x => x.Program).HasMaxLength(100);
            e.Property(x => x.DecisionSource).HasMaxLength(200);
            e.Property(x => x.CacheKey).HasMaxLength(100);
            e.HasIndex(x => new { x.InstitutionId, x.SourceCode, x.Program });
            e.HasOne(x => x.Institution).WithMany().HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BUCourse).WithMany().HasForeignKey(x => x.BUCourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MatchCandidate>(e =>
        {
            e.Property(x => x.Confidence).HasMaxLength(20);
            e.Property(x => x.MatchVersion).HasMaxLength(40);
            e.HasOne(x => x.BUCourse).WithMany().HasForeignKey(x => x.BUCourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AgentRun>(e =>
        {
            e.Property(x => x.CacheKey).HasMaxLength(100);
            e.HasIndex(x => x.CacheKey);
            e.Property(x => x.GraphVersion).HasMaxLength(40);
            e.Property(x => x.PromptVersion).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.ThreadId).HasMaxLength(100);
            e.Property(x => x.InputHash).HasMaxLength(100);
            e.HasOne<SourceCourse>().WithMany().HasForeignKey(x => x.SourceCourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Evaluation>(e =>
        {
            e.Property(x => x.EvaluatorId).HasMaxLength(64);
            e.Property(x => x.Rationale).HasMaxLength(4000);
            e.HasOne(x => x.BUCourse).WithMany().HasForeignKey(x => x.BUCourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Document>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Container).HasMaxLength(40);
            e.Property(x => x.StorageKey).HasMaxLength(500);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.UploadedBy).HasMaxLength(64);
            e.HasIndex(x => x.Sha256);
            e.HasIndex(x => x.ScanStatus);
        });

        b.Entity<IntakeMessage>(e =>
        {
            e.Property(x => x.GraphMessageId).HasMaxLength(500);
            e.Property(x => x.InternetMessageId).HasMaxLength(500);
            e.HasIndex(x => x.InternetMessageId).IsUnique();
            e.Property(x => x.SenderAddress).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.AssignedBy).HasMaxLength(64);
            e.HasIndex(x => x.MatchStatus);
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.IntakeMessageId);
        });

        b.Entity<MailSyncState>(e =>
        {
            e.HasKey(x => new { x.MailboxAddress, x.FolderId });
            e.Property(x => x.MailboxAddress).HasMaxLength(320);
            e.Property(x => x.FolderId).HasMaxLength(100);
            e.Property(x => x.LastStatus).HasMaxLength(500);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.ActorId).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.StudentMessage).HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => x.CaseId);
        });
    }
}
