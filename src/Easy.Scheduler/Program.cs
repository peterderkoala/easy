using Easy.Data;
using Easy.Platform;
using Easy.Scheduler;
using Hangfire;
using Hangfire.PostgreSql;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform().AddEasyMessaging(); // publish only
builder.Services.AddEasyData();
builder.Services.AddSingleton(TimeProvider.System);
// Job storage in the shared database, schema "hangfire" (S16). Long polling (LISTEN/NOTIFY) starts triggered jobs at once.
builder.Services.AddHangfire((sp, hangfire) => hangfire.UsePostgreSqlStorage(
    o => o.UseNpgsqlConnection(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")),
    new PostgreSqlStorageOptions { SchemaName = "hangfire", EnableLongPolling = true }));
builder.Services.AddHangfireServer(o => o.WorkerCount = 2); // a daily job; each long-polling worker holds a connection
var app = builder.Build().UseEasyPlatform(); // no dashboard in the skeleton

app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<DeadlineReminder>(DeadlineReminder.JobId, r => r.Execute(null!),
    app.Configuration["Reminder:Cron"] ?? "0 7 * * *", new RecurringJobOptions { TimeZone = DeadlineReminder.Berlin });

app.Run();
