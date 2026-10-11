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

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.Properties<Enum>().HaveConversion<string>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Participant>(e =>
        {
            e.HasOne<Cycle>().WithMany().HasForeignKey(p => p.CycleId);
            e.HasIndex(p => new { p.CycleId, p.DisplayNo }).IsUnique();
            e.HasIndex(p => new { p.CycleId, p.CsvIdHash }).IsUnique();
        });
        model.Entity<Examiner>().HasIndex(e => e.Subject).IsUnique();
        model.Entity<OralSession>().HasOne<Participant>().WithMany().HasForeignKey(s => s.ParticipantId);
        model.Entity<Grade>(e =>
        {
            e.HasKey(g => new { g.ParticipantId, g.Source });
            e.HasOne<Participant>().WithMany().HasForeignKey(g => g.ParticipantId);
            e.HasOne<Examiner>().WithMany().HasForeignKey(g => g.UpdatedBy);
            e.Property(g => g.Value).HasPrecision(5, 1);
            e.ToTable(t => t.HasCheckConstraint("ck_grades_value_non_negative", "value >= 0"));
        });
        model.Entity<Document>().HasOne<Participant>().WithMany().HasForeignKey(d => d.ParticipantId);
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
