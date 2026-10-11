namespace Easy.Contracts;

// Events: immutable, past tense, IDs and minimal data only, never personal data.
// EventId is deterministic (a business key), so idempotent consumers can skip redeliveries.
// The correlation ID travels in the rbs2-corr-id header, never in the body.

public record CycleImported(Guid CycleId)
{
    public string EventId => $"cycle-imported:{CycleId}";
}
