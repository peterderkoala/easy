using Easy.Contracts;
using Easy.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Grading;

// Live documentation grading (S06, S09): any examiner watches any documentation. Writers send to Group(participantId)
// through IHubContext<GradingHub, IGradingClient> in HttpContext.AfterCommit, not through the outbox: the client refetches on reconnect.
// ponytail: groups live in this one instance; add a backplane when MainApi scales out.
public class GradingHub(EasyDb db) : Hub<IGradingClient>
{
    public static string Group(Guid participantId) => $"documentation-{participantId}";

    public async Task JoinDocumentation(Guid participantId)
    {
        if (!await db.Participants.AnyAsync(p => p.Id == participantId))
            throw new HubException("Unknown participant.");
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(participantId));
    }

    public Task LeaveDocumentation(Guid participantId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(participantId));
}
