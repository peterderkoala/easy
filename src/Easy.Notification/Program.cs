using Easy.Contracts;
using Easy.Data;
using Easy.Notification;
using Easy.Platform;
using Rebus.Config;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform()
    .AddEasyMessaging("notification", bus => bus.Subscribe<DocumentUploaded>());
builder.Services.AddEasyData();
builder.Services.AddRebusHandler<DocumentUploadedHandler>();
var app = builder.Build().UseEasyPlatform();

app.Run();
