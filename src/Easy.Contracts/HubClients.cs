namespace Easy.Contracts;

// Typed hub clients (S09): every hub is Hub<IXxxClient>, no string SendAsync. Shared with the Blazor client.

// MainApi grading hub (S06). Notify, then refetch: messages carry only IDs, enums and display names, never points
// or remarks; the client re-reads over REST, which applies blinding. Later specs add their events.
public interface IGradingClient
{
    Task RegistrationTaken(Guid participantId, string examiner);
    Task RegistrationReleased(Guid participantId, string examiner);
    Task ExaminerGradingChanged(Guid participantId, string examiner, ExaminerGradingEvent change);
    Task SummaryViewOpened(Guid participantId, string examiner);
    Task CommentAdded(Guid participantId, Guid commentId, string author);
}

public enum ExaminerGradingEvent { Draft, Submitted, Withdrawn }

// ExamRoom hub (S07).
public interface IExamRoomClient
{
    Task ExaminerJoined(string name);
}
