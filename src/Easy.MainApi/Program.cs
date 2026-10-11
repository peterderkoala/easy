using Easy.Data;
using Easy.MainApi.Examiners;
using Easy.MainApi.Participants;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform().AddEasyMessaging();
builder.Services.AddEasyData();
// Read on first use, so only code that touches participant data needs the secret file (ADR 0001, MainApi only).
builder.Services.AddSingleton<IMasterSecret>(_ => FileMasterSecret.Read(
    builder.Configuration["MasterSecretFile"] ?? throw new InvalidOperationException("MasterSecretFile is not configured."),
    builder.Configuration.GetValue("MasterSecretVersion", 1)));
builder.Services.AddSingleton<ParticipantKeys>();
var app = builder.Build();

// MainApi owns the schema; the other hosts start after it is healthy (S16).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<EasyDb>().Database.Migrate();

app.UseEasyPlatform();
app.UseOutbox(); // write requests: EF transaction + Rebus outbox, committed together
app.MapMe();
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();
