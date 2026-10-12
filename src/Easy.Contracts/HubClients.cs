namespace Easy.Contracts;

// Typed hub clients (S09): every hub is Hub<IXxxClient>, no string SendAsync. Shared with the Blazor client.

// MainApi grading hub (S06). Notify, then refetch: messages carry only IDs, enums and display names, never points
// or remarks; the client re-reads over REST, which applies blinding. Later specs add their events.
public interface IGradingClient
{
    Task RegistrationTaken(Guid participantId, string examiner);
}

// ExamRoom hub (S07).
public interface IExamRoomClient
{
    Task ExaminerJoined(string name);
}
