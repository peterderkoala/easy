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

// Idempotent consumers: one row per handled event ID.
public class ProcessedEvent
{
    public required string Id { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
