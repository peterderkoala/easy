using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.Data;

// The one DbContext (S09). Snake-case names; Hangfire keeps its own "hangfire" schema, Rebus its outbox table.
// No retrying execution strategy: it cannot be combined with the outbox's shared transaction (ADR 0003).
public class EasyDb(DbContextOptions<EasyDb> options) : DbContext(options)
{
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Examiner> Examiners => Set<Examiner>();
    public DbSet<OralSession> OralSessions => Set<OralSession>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ConfigEntry> ConfigEntries => Set<ConfigEntry>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<ExaminerGrading> ExaminerGradings => Set<ExaminerGrading>();
    public DbSet<CriterionGrading> CriterionGradings => Set<CriterionGrading>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<SummaryView> SummaryViews => Set<SummaryView>();
    public DbSet<Deviation> Deviations => Set<Deviation>();
    public DbSet<ThirdExaminerInvitation> ThirdExaminerInvitations => Set<ThirdExaminerInvitation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.Properties<Enum>().HaveConversion<string>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Cycle>(e =>
        {
            e.HasIndex(c => new { c.Type, c.Year }).IsUnique(); // S02
            e.Ignore(c => c.Weights);
        });
        model.Entity<Participant>(e =>
        {
            e.HasOne<Cycle>().WithMany().HasForeignKey(p => p.CycleId);
            e.HasIndex(p => new { p.CycleId, p.DisplayNo }).IsUnique();
            e.HasIndex(p => new { p.CycleId, p.CsvIdHash }).IsUnique();
        });
        model.Entity<Examiner>().HasIndex(e => e.Subject).IsUnique();
        model.Entity<OralSession>(e =>
        {
            e.HasOne<Participant>().WithMany().HasForeignKey(s => s.ParticipantId);
            e.HasIndex(s => s.ParticipantId).IsUnique(); // one per participant, so per cycle; closed is final (S07)
        });
        model.Entity<Grade>(e =>
        {
            e.HasKey(g => new { g.ParticipantId, g.Source });
            e.HasOne<Participant>().WithMany().HasForeignKey(g => g.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(g => g.UpdatedBy);
            e.Property(g => g.Value).HasPrecision(5, 1);
            e.ToTable(t => t.HasCheckConstraint("ck_grades_value_non_negative", "value >= 0"));
        });
        model.Entity<Document>().HasOne<Participant>().WithMany().HasForeignKey(d => d.ParticipantId);
        model.Entity<ImportJob>(e =>
        {
            e.HasOne<Cycle>().WithMany().HasForeignKey(j => j.CycleId);
            e.OwnsMany(j => j.Errors, b => b.ToJson());
        });
        model.Entity<ConfigEntry>().HasKey(c => c.Key);
        model.Entity<Registration>(e =>
        {
            e.HasKey(r => new { r.ParticipantId, r.ExaminerId });
            e.HasIndex(r => new { r.ParticipantId, r.Slot }).IsUnique();
            e.HasOne<Participant>().WithMany().HasForeignKey(r => r.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(r => r.ExaminerId);
            e.ToTable(t => t.HasCheckConstraint("ck_registrations_slot", "slot IN (1, 2)"));
        });
        model.Entity<ExaminerGrading>(e =>
        {
            e.HasKey(g => new { g.ParticipantId, g.ExaminerId });
            e.HasOne<Participant>().WithMany().HasForeignKey(g => g.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(g => g.ExaminerId);
            e.HasMany(g => g.Criteria).WithOne().HasForeignKey(c => new { c.ParticipantId, c.ExaminerId }).OnDelete(DeleteBehavior.Cascade);
            e.Property(g => g.Version).IsRowVersion(); // xmin
        });
        model.Entity<CriterionGrading>().HasKey(c => new { c.ParticipantId, c.ExaminerId, c.CriterionKey });
        model.Entity<Deviation>(e =>
        {
            e.HasKey(d => d.ParticipantId);
            e.HasOne<Participant>().WithOne().HasForeignKey<Deviation>(d => d.ParticipantId);
        });
        model.Entity<ThirdExaminerInvitation>(e =>
        {
            e.HasIndex(i => i.ParticipantId).IsUnique().HasFilter("status <> 'Declined'");
            e.HasOne<Participant>().WithMany().HasForeignKey(i => i.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(i => i.ExaminerId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(i => i.InvitedBy);
        });
        model.Entity<SummaryView>(e =>
        {
            e.HasKey(v => new { v.ParticipantId, v.ExaminerId });
            e.HasOne<Participant>().WithMany().HasForeignKey(v => v.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(v => v.ExaminerId);
        });
        model.Entity<Comment>(e =>
        {
            e.HasOne<Participant>().WithMany().HasForeignKey(c => c.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(c => c.ExaminerId);
        });
        model.Entity<ProcessedEvent>().Property(p => p.ProcessedAt).HasDefaultValueSql("now()");
    }
}

public static class EasyDbRegistration
{
    // Connection string is resolved from configuration when the context is created.
    // Interceptors registered in DI (the Platform audit interceptor) are attached to every context.
    public static IServiceCollection AddEasyData(this IServiceCollection services) =>
        services.AddDbContext<EasyDb>((sp, o) => o.UseNpgsql("Name=ConnectionStrings:Default").UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetServices<IInterceptor>()));
}
