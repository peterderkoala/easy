using System.Globalization;
using System.Reflection;
using Easy.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Easy.Platform;

// S11 audit component: every change of an [Audited] property adds an AuditEntry to the same SaveChanges,
// so it commits or rolls back with the business change. Audit entries themselves are append-only.
// ponytail: ExecuteUpdate/ExecuteDelete and raw SQL bypass this; an insert-only DB role is the upgrade (S11, not decided).
public sealed class AuditInterceptor(IHttpContextAccessor http) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Audit(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Audit(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    void Audit(DbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var entries = new List<AuditEntry>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditEntry && entry.State is (EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Audit entries are append-only.");
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            foreach (var property in entry.Properties.Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<AuditedAttribute>() is not null))
            {
                if (entry.State == EntityState.Modified && !property.IsModified)
                    continue;
                entries.Add(new()
                {
                    CorrelationId = CorrelationId.Current ?? throw new InvalidOperationException("Audited write outside an operation with a correlation ID."),
                    // ponytail: actor from the HTTP request only; Rebus handlers and Hangfire jobs need an ambient actor once they write audited data
                    ActorId = http.HttpContext?.User.FindFirst("sub")?.Value ?? throw new InvalidOperationException("Audited write without an authenticated actor."),
                    ParticipantId = ParticipantId(entry),
                    Entity = entry.Metadata.ClrType.Name,
                    Field = property.Metadata.Name,
                    EntityKey = EntityKey(entry),
                    Old = entry.State == EntityState.Added ? null : Format(property.OriginalValue),
                    New = Format(property.CurrentValue),
                    Type = AuditType.Initial, // ponytail: always initial; correction comes with the closed-area state (S11)
                    Timestamp = DateTimeOffset.UtcNow,
                });
            }
        }
        db.AddRange(entries);
    }

    // By convention: audited entities that belong to a participant carry a ParticipantId property.
    static Guid? ParticipantId(EntityEntry entry) =>
        entry.Metadata.FindProperty("ParticipantId") is { } p ? (Guid?)entry.Property(p.Name).CurrentValue : null;

    static string EntityKey(EntityEntry entry) =>
        string.Join('/', entry.Metadata.FindPrimaryKey()!.Properties.Where(p => p.Name != "ParticipantId").Select(p => Format(entry.Property(p.Name).CurrentValue)));

    // Encrypted columns (byte[]) are logged as their ciphertext.
    static string? Format(object? value) => value is byte[] bytes ? Convert.ToBase64String(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture);
}
