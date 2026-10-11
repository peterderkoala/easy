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
