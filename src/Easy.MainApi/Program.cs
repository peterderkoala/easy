using Easy.Data;
using Easy.MainApi.Documents;
using Easy.MainApi.Examiners;
using Easy.MainApi.Grades;
using Easy.MainApi.Grading;
using Easy.MainApi.Imports;
using Easy.MainApi.OralSessions;
using Easy.MainApi.Participants;
using Easy.MainApi.Registrations;
using Easy.MainApi.Settings;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Rebus.Config;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform().AddEasyMessaging("main-api"); // own queue: CSV import (S02)
builder.Services.AddEasyData();
builder.WebHost.ConfigureKestrel((context, kestrel) => kestrel.Limits.MaxRequestBodySize = Upload.MaxBytes(context.Configuration));
// Read on first use, so only code that touches participant data needs the secret file (ADR 0001, MainApi only).
builder.Services.AddSingleton<IMasterSecret>(sp =>
{
    // From the container, not builder.Configuration, so host-level overrides (tests) apply.
    var config = sp.GetRequiredService<IConfiguration>();
    return FileMasterSecret.Read(config["MasterSecretFile"] ?? throw new InvalidOperationException("MasterSecretFile is not configured."),
        config.GetValue("MasterSecretVersion", 1));
});
builder.Services.AddSingleton<ParticipantKeys>();
builder.Services.AddSingleton<DocumentStore>().AddSingleton<IParticipantObjects>(sp => sp.GetRequiredService<DocumentStore>());
builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection("Import"));
builder.Services.AddRebusHandler<ImportCycleHandler>();
builder.Services.AddSignalR();
var app = builder.Build();

// MainApi owns the schema; the other hosts start after it is healthy (S16).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<EasyDb>().Database.Migrate();

app.UseEasyPlatform();
app.UseOutbox(); // write requests: EF transaction + Rebus outbox, committed together
app.MapMe();
app.MapSetGrade();
app.MapImports();
app.MapUploadDocument();
app.MapCreateOralSession();
app.MapSettings();
app.MapRegistrations();
app.MapDeactivation();
// Hub connections outlive the 5-min token; close at expiry and the client reconnects (as ExamRoom).
app.MapHub<GradingHub>("/hubs/grading", o => o.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();
