using Easy.Data;
using Easy.ExamRoom;
using Easy.Platform;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform();
builder.Services.AddEasyData();
builder.Services.AddSignalR();
var app = builder.Build().UseEasyPlatform();

// Exam connections outlive the 5-min token; .NET 10 has no in-place refresh, so close at expiry and the client reconnects.
app.MapHub<ExamRoomHub>("/hubs/exam-room", o => o.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too

app.Run();
