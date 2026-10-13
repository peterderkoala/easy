namespace Easy.Contracts;

// Events: immutable, past tense, IDs and minimal data only, never personal data.
// EventId is fixed when the event is created (a business key, or an ID generated once per write), so idempotent consumers can skip redeliveries.
// The correlation ID travels in the rbs2-corr-id header, never in the body.

public record CycleImported(Guid CycleId)
{
    public string EventId => $"cycle-imported:{CycleId}";
}

// A score was set (S11). ChangeId is generated once per write, so redeliveries of one write share an EventId.
public record GradeChanged(Guid ParticipantId, string Source, Guid ChangeId)
{
    public string EventId => $"grade-changed:{ChangeId}";
}

// A documentation PDF was stored (S06); the notification service mails the examiners (S12).
public record DocumentUploaded(Guid DocumentId, Guid ParticipantId)
{
    public string EventId => $"document-uploaded:{DocumentId}";
}

// A module's due date is near (S12). Published by the scheduler without the outbox; the business key makes retries and
// same-day re-runs one event, while each remaining-days step is a new one.
public record DeadlineApproaching(Guid CycleId, string Module, DateOnly DueDate, int DaysLeft)
{
    public string EventId => $"deadline:{CycleId}:{Module}:{DueDate:yyyy-MM-dd}:{DaysLeft}";
}
