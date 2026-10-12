using System.Security.Claims;
using Easy.Contracts;
using Easy.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.ExamRoom;

// S07 Virtual Room, skeleton join path (S09): any examiner may join any open session.
// ponytail: presence is not stored and groups live in this one instance; add a backplane when exam-room scales out.
public class ExamRoomHub(EasyDb db) : Hub<IExamRoomClient>
{
    public async Task JoinSession(Guid sessionId)
    {
        if (!await db.OralSessions.AnyAsync(s => s.Id == sessionId && s.Status == OralSessionStatus.Open))
            throw new HubException("Unknown or closed session.");
        string group = $"session-{sessionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        var user = Context.User!;
        await Clients.Group(group).ExaminerJoined(user.FindFirstValue("name") ?? user.Identity!.Name!);
    }
}
