using System.Security.Claims;
using Easy.Data;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Examiners;

// S05: Keycloak owns identities; MainApi keeps an Examiner mirror. The client posts here after every login,
// so the first call creates the row and later ones refresh role, name and email from the token.
public static class Me
{
    public static void MapMe(this WebApplication app) =>
        app.MapPost("/api/me", async (ClaimsPrincipal user, EasyDb db) =>
        {
            string subject = user.FindFirstValue("sub")!;
            // ponytail: two concurrent first logins of one user hit the unique index and one gets a 500; the client retries on next login
            var examiner = await db.Examiners.SingleOrDefaultAsync(e => e.Subject == subject)
                ?? db.Examiners.Add(new() { Subject = subject, DisplayName = "", Email = "" }).Entity;
            examiner.Role = user.IsInRole("admin") ? ExaminerRole.Admin : ExaminerRole.Examiner;
            examiner.DisplayName = user.FindFirstValue("name") ?? user.Identity!.Name!;
            examiner.Email = user.FindFirstValue("email") ?? "";
            await db.SaveChangesAsync();
            return new { examiner.DisplayName, Role = examiner.Role.ToString(), examiner.Active };
        }).RequireAuthorization();
}
