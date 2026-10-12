namespace Easy.Data;

// Opt-in audit (S11): the Platform audit interceptor writes an AuditEntry for every change of a marked property.
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditedAttribute : Attribute;

// Skeleton subset of S14. Feature slices add the rest.

public enum CycleType { Winter, Summer }

public class Cycle
{
    public Guid Id { get; set; }
    public CycleType Type { get; set; }
    public int Year { get; set; }
    public DateOnly? DocumentationDueDate { get; set; }
    public DateOnly? DocumentationDeadline { get; set; }

    // Documentation section weights in percent, matrix order, adding up to 100 (S06). Copied from
    // ConfigEntry.DefaultWeights when the import creates the cycle.
    public int Weight1 { get; set; }
    public int Weight2 { get; set; }
    public int Weight3 { get; set; }
    public int Weight4 { get; set; }
    public int Weight5 { get; set; }

    public int[] Weights
    {
        get => [Weight1, Weight2, Weight3, Weight4, Weight5];
        set => (Weight1, Weight2, Weight3, Weight4, Weight5) = (value[0], value[1], value[2], value[3], value[4]);
    }

    // WS2026 / SS2026: prefix of display numbers and the cycle's name in mails.
    public static string Label(CycleType type, int year) => $"{(type == CycleType.Winter ? "WS" : "SS")}{year}";
}

// Status values: S04 only.
public enum ApplicationStatus { Submitted, Rejected, Approved, ApprovedWithConditions }
public enum DocumentationStatus { Submitted, InProgress, Graded }
public enum ExamStatus { Attended, Failed, Passed } // written and oral

public class Participant
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public required string DisplayNo { get; set; } // WS2026-014, unique per cycle

    // Encrypted under the participant data key (S02, ADR 0001).
    public required byte[] CsvId { get; set; }
    public required byte[] Name { get; set; }
    public required byte[] ProjectTitle { get; set; }
    public byte[]? CsvIdHash { get; set; } // HMAC blind index, null after forget
    public byte[]? WrappedKey { get; set; } // null after forget
    public int KeyVersion { get; set; } // master-secret version that wrapped the key
    public bool KeyForgotten { get; set; }

    public ApplicationStatus? ApplicationStatus { get; set; }
    public ExamStatus? WrittenStatus { get; set; }
    public DocumentationStatus? DocumentationStatus { get; set; }
    public ExamStatus? OralStatus { get; set; }
}

public enum ExaminerRole { Admin, Examiner }

// Mirror of the Keycloak subject, created on first login. Examiner data, not encrypted.
public class Examiner
{
    public Guid Id { get; set; }
    public required string Subject { get; set; }
    public ExaminerRole Role { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public bool Active { get; set; } = true;
}

public enum OralSessionStatus { Open, Closed }

public class OralSession
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public OralSessionStatus Status { get; set; }
}

public enum GradeSource { Written, Documentation, Oral, Interim }

// Current value per source only; history is the audit log (S09).
public class Grade
{
    public Guid ParticipantId { get; set; }
    public GradeSource Source { get; set; }
    [Audited] public decimal Value { get; set; }
    public Guid UpdatedBy { get; set; } // Examiner.Id
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AuditType { Initial, Correction }

// Append-only (S11).
public class AuditEntry
{
    public long Id { get; set; }
    public required string CorrelationId { get; set; }
    public required string ActorId { get; set; } // token sub
    public Guid? ParticipantId { get; set; }
    public required string Entity { get; set; }
    public required string Field { get; set; }
    public string? Old { get; set; }
    public string? New { get; set; }
    public AuditType Type { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class Document
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public required string ObjectKey { get; set; }
    public long Size { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public enum ImportStatus { Queued, Succeeded, Failed }

// S02: one CSV upload. The file itself is never stored; only the outcome and row errors (no field values).
public class ImportJob
{
    public Guid Id { get; set; }
    public CycleType CycleType { get; set; }
    public int Year { get; set; }
    public ImportStatus Status { get; set; }
    public Guid? CycleId { get; set; }
    public List<ImportError> Errors { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

// Row 1 is the header; row 0 means the whole file.
public record ImportError(int Row, string Column, string Message);

// S06 (issue 39): system-wide settings only. Values are text, parsed in code; defaults are seeded by migration.
public class ConfigEntry
{
    public const string DeviationSectionPoints = "grading.deviation.section-points";
    public const string DeviationTotalPoints = "grading.deviation.total-points";
    public const string DefaultWeights = "grading.default-weights"; // 15/10/40/25/10

    public required string Key { get; set; }
    [Audited] public required string Value { get; set; }
}

// Idempotent consumers: one row per handled event ID.
public class ProcessedEvent
{
    public required string Id { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
