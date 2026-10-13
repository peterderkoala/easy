extern alias mainapi;
extern alias notification;
extern alias scheduler;

using Easy.Contracts;
using Easy.Data;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using scheduler::Easy.Scheduler;

namespace Easy.IntegrationTests;

// Spec 9 (issue 24): a Hangfire job publishes DeadlineApproaching, the notification service mails the active examiners (S12).
[Collection(nameof(Infra))]
public class ReminderTests(Infra infra) : IAsyncLifetime
{
    // 07:00 in Berlin; "today" for the job is 2026-11-10.
    static readonly DateTimeOffset Now = new(2026, 11, 10, 7, 0, 0, TimeSpan.FromHours(1));
    static readonly DateOnly Today = new(2026, 11, 10);

    readonly string database = infra.NewDatabase();
    readonly List<IAsyncDisposable> hosts = [];
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    WebApplicationFactory<scheduler::Program> scheduler = null!;

    public async Task InitializeAsync()
    {
        await infra.ClearMails();
        mainApi = Track(infra.Configure(new WebApplicationFactory<mainapi::Program>(), database));
        mainApi.Services.GetRequiredService<IBus>(); // starts MainApi: migrations run
        // The recurring job never fires on its own here, and the Hangfire-run job sees the fixed clock too.
        scheduler = Track(infra.Configure(new WebApplicationFactory<scheduler::Program>(), database).WithWebHostBuilder(b => b
            .UseSetting("Reminder:Cron", Cron.Never())
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(Now)))));
        scheduler.Services.GetRequiredService<IBus>();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in Enumerable.Reverse(hosts)) await host.DisposeAsync();
    }

    [Fact]
    public async Task Job_publishes_event_for_cycle_within_window()
    {
        Guid cycle = await AddCycle(Today.AddDays(2));
        using var consumer = await EventConsumer<DeadlineApproaching>.Start(infra);

        await RunJob(Now, "job-1");

        Assert.Equal(new DeadlineApproaching(cycle, "documentation", Today.AddDays(2), 2), (await consumer.Next()).Event);
    }

    [Fact]
    public async Task Job_ignores_cycles_outside_window_or_without_due_date()
    {
        await AddCycle(Today.AddDays(4), year: 2001);
        await AddCycle(Today.AddDays(-1), year: 2002);
        await AddCycle(null, year: 2003);
        Guid edge = await AddCycle(Today.AddDays(3), year: 2004);
        using var consumer = await EventConsumer<DeadlineApproaching>.Start(infra);

        await RunJob(Now, "job-1");

        Assert.Equal(edge, (await consumer.Next()).Event.CycleId);
        Assert.Empty(await consumer.Drain(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Rerun_same_day_produces_same_event_id()
    {
        Guid cycle = await AddCycle(Today.AddDays(1));
        using var consumer = await EventConsumer<DeadlineApproaching>.Start(infra);

        await RunJob(Now, "job-1");
        await RunJob(Now.AddHours(5), "job-2");

        var first = (await consumer.Next()).Event;
        var second = (await consumer.Next()).Event;
        Assert.Equal($"deadline:{cycle}:documentation:{Today.AddDays(1):yyyy-MM-dd}:1", first.EventId);
        Assert.Equal(first.EventId, second.EventId);
    }

    [Fact]
    public async Task Event_carries_rbs2_corr_id()
    {
        // Through the real Hangfire server: the job id becomes the correlation ID.
        Guid cycle = await AddCycle(Today.AddDays(1));
        using var consumer = await EventConsumer<DeadlineApproaching>.Start(infra);

        string jobId = scheduler.Services.GetRequiredService<IRecurringJobManagerV2>().TriggerJob(DeadlineReminder.JobId);

        var delivery = await consumer.Next();
        Assert.Equal(cycle, delivery.Event.CycleId);
        Assert.Equal(jobId, delivery.CorrelationId);
    }

    [Fact]
    public async Task Event_triggers_one_mail_per_active_examiner_in_mailpit()
    {
        await AddCycle(Today.AddDays(2));
        await AddExaminers();
        await StartNotification();

        await RunJob(Now, "job-1");

        var mails = await infra.Mails(expected: 2);
        Assert.Equal(["a@easy.local", "b@easy.local"], mails.Select(m => m.GetProperty("To")[0].GetProperty("Address").GetString()).Order());
        Assert.All(mails, m => Assert.Contains("Dokumentation", m.GetProperty("Subject").GetString()));
        Assert.All(mails, m => Assert.Contains("12.11.2026", m.GetProperty("Snippet").GetString()));
    }

    [Fact]
    public async Task Redelivered_event_sends_no_second_mail()
    {
        Guid cycle = await AddCycle(Today.AddDays(2));
        await AddExaminers();
        await StartNotification();
        var bus = scheduler.Services.GetRequiredService<IBus>();
        var reminder = new DeadlineApproaching(cycle, "documentation", Today.AddDays(2), 2);

        await bus.Publish(reminder);
        await bus.Publish(reminder);

        await infra.Mails(expected: 2);
        await Task.Delay(TimeSpan.FromSeconds(2)); // give the duplicate time to (not) send
        Assert.Equal(2, (await infra.Mails(expected: 2)).Count);
    }

    async Task RunJob(DateTimeOffset now, string correlationId)
    {
        using var scope = scheduler.Services.CreateScope();
        await ActivatorUtilities.CreateInstance<DeadlineReminder>(scope.ServiceProvider, new FixedClock(now)).Run(correlationId);
    }

    async Task<Guid> AddCycle(DateOnly? due, int year = 2026)
    {
        using var scope = mainApi.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
        var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = year, DocumentationDueDate = due }).Entity;
        await db.SaveChangesAsync();
        return cycle.Id;
    }

    async Task AddExaminers()
    {
        using var scope = mainApi.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
        db.Examiners.Add(new() { Subject = "a", DisplayName = "A", Email = "a@easy.local" });
        db.Examiners.Add(new() { Subject = "b", DisplayName = "B", Email = "b@easy.local", Role = ExaminerRole.Admin });
        db.Examiners.Add(new() { Subject = "c", DisplayName = "C", Email = "c@easy.local", Active = false });
        await db.SaveChangesAsync();
    }

    async Task StartNotification()
    {
        Track(infra.Configure(new WebApplicationFactory<notification::Program>(), database)).Services.GetRequiredService<IBus>();
        await Task.Delay(TimeSpan.FromSeconds(1)); // let the subscription settle before publishing
    }


    T Track<T>(T host) where T : IAsyncDisposable
    {
        hosts.Add(host);
        return host;
    }

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
