using Easy.Contracts;
using Easy.Data;
using Easy.Notification;
using Easy.Platform;
using Rebus.Config;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform()
    .AddEasyMessaging("notification", async bus =>
    {
        await bus.Subscribe<DocumentUploaded>();
        await bus.Subscribe<DeadlineApproaching>();
    });
builder.Services.AddEasyData();
builder.Services.AddRebusHandler<DocumentUploadedHandler>();
builder.Services.AddRebusHandler<DeadlineApproachingHandler>();
var app = builder.Build().UseEasyPlatform();

app.Run();
