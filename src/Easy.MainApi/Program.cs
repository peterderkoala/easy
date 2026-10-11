using Easy.Data;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform().AddEasyMessaging();
builder.Services.AddEasyData();
var app = builder.Build();

// MainApi owns the schema; the other hosts start after it is healthy (S16).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<EasyDb>().Database.Migrate();

app.UseEasyPlatform();
app.UseOutbox(); // write requests: EF transaction + Rebus outbox, committed together
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();
