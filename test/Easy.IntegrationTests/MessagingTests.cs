using System.Collections.Concurrent;
using System.Net.Http.Json;
using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Easy.IntegrationTests;

[Collection(nameof(Infra))]
public class MessagingTests(Infra infra) : IAsyncLifetime
{
    readonly string database = infra.NewDatabase();
    readonly Received received = new();
    WebApplication publisher = null!;
    WebApplication consumer = null!;

    public async Task InitializeAsync()
    {
        publisher = await StartHost(queue: null);
        consumer = await StartHost(queue: $"test-{Guid.NewGuid():N}");
        await consumer.Services.GetRequiredService<IBus>().Subscribe<CycleImported>();
    }

    public async Task DisposeAsync()
    {
        await publisher.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Outbox_row_is_committed_with_business_row()
    {
        var client = publisher.GetTestClient();
        var failed = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var committed = Guid.NewGuid();

        await Assert.ThrowsAnyAsync<Exception>(() => client.PostAsJsonAsync("/cycles?fail=true", failed));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/cycles?reject=true", rejected)).StatusCode);
        (await client.PostAsJsonAsync("/cycles", committed)).EnsureSuccessStatusCode();

        // Forwarded in order, so a leaked event from the failed or rejected request would arrive first.
        Assert.Equal(committed, (await received.Next()).Event.CycleId);
        Assert.Equal([committed], await Query(db => db.Cycles.Select(c => c.Id).ToListAsync()));
    }

    [Fact]
    public async Task Outbox_message_survives_broker_outage_and_is_sent_later()
    {
        var cycle = Guid.NewGuid();
        await infra.Rabbit.StopAsync();
        try
        {
            (await publisher.GetTestClient().PostAsJsonAsync("/cycles", cycle)).EnsureSuccessStatusCode();
            await Task.Delay(TimeSpan.FromSeconds(3)); // the forwarder keeps failing meanwhile
            Assert.Equal(1, await OutboxRows());
            Assert.True(await Query(db => db.Cycles.AnyAsync(c => c.Id == cycle)));
        }
        finally
        {
            await infra.Rabbit.StartAsync();
        }

        Assert.Equal(cycle, (await received.Next(TimeSpan.FromSeconds(90))).Event.CycleId);
        Assert.Equal(0, await OutboxRows());
    }

    [Fact]
    public async Task Correlation_id_header_is_preserved_through_outbox()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/cycles") { Content = JsonContent.Create(Guid.NewGuid()) };
        request.Headers.Add(CorrelationId.Header, "corr-123");

        (await publisher.GetTestClient().SendAsync(request)).EnsureSuccessStatusCode();

        var delivery = await received.Next();
        Assert.Equal("corr-123", delivery.Header);
        Assert.Equal("corr-123", delivery.Ambient);
    }

    [Fact]
    public async Task Duplicate_event_is_processed_once()
    {
        var bus = publisher.Services.GetRequiredService<IBus>();
        var evt = new CycleImported(Guid.NewGuid());

        await bus.Publish(evt);
        await bus.Publish(evt);

        await received.Next();
        await Task.Delay(TimeSpan.FromSeconds(2)); // give the second delivery time to arrive
        Assert.Equal(2, received.Deliveries);
        Assert.Equal(1, received.Count);
        Assert.Equal(1, await Query(db => db.ProcessedEvents.CountAsync()));
    }

    // Spec 22: second-level retries are on for every host; a message type without an IFailed<T> handler still ends
    // in the error queue after its retries.
    [Fact]
    public async Task Failing_message_without_failed_handler_is_dead_lettered()
    {
        await consumer.Services.GetRequiredService<IBus>().SendLocal(new Poison());

        for (var until = DateTime.UtcNow.AddSeconds(20); await infra.ErrorQueueCount(consumer.Services) == 0; await Task.Delay(200))
            if (DateTime.UtcNow > until)
                throw new TimeoutException("nothing dead-lettered");
    }

    async Task<WebApplication> StartHost(string? queue)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["ConnectionStrings:Default"] = database;
        builder.Configuration["Rebus:Rabbit"] = infra.Rabbit.GetConnectionString();
        builder.Configuration["Rebus:ErrorQueue"] = $"test-error-{Guid.NewGuid():N}";
        builder.AddEasyPlatform().AddEasyMessaging(queue);
        builder.Services.AddEasyData();
        builder.Services.AddSingleton(received).AddRebusHandler<Handler>().AddRebusHandler<PoisonHandler>();

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EasyDb>().Database.MigrateAsync();
        app.UseEasyPlatform().UseOutbox();
        app.MapPost("/cycles", async ([FromBody] Guid id, bool? fail, bool? reject, EasyDb db, IBus bus) =>
        {
            db.Cycles.Add(new Cycle { Id = id, Type = CycleType.Winter, Year = 2026 });
            await db.SaveChangesAsync();
            await bus.Publish(new CycleImported(id));
            if (fail == true)
                throw new InvalidOperationException("fails after save and publish");
            return reject == true ? Results.BadRequest() : Results.Ok();
        });
        await app.StartAsync();
        return app;
    }

    async Task<T> Query<T>(Func<EasyDb, Task<T>> query)
    {
        using var scope = publisher.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyDb>());
    }

    Task<int> OutboxRows() =>
        Query(db => db.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {Messaging.OutboxTable}").SingleAsync());

    public record Delivery(CycleImported Event, string? Header, string? Ambient);

    public class Received
    {
        readonly BlockingCollection<Delivery> handled = new();
        int deliveries;

        public int Deliveries => deliveries;
        public int Count { get; private set; }

        public void Delivered() => Interlocked.Increment(ref deliveries);

        public void Handled(Delivery delivery)
        {
            Count++;
            handled.Add(delivery);
        }

        public Task<Delivery> Next(TimeSpan? timeout = null) => Task.Run(() =>
            handled.TryTake(out var delivery, timeout ?? TimeSpan.FromSeconds(20)) ? delivery : throw new TimeoutException("no event handled"));
    }

    class Handler(Received received, IMessageContext context) : IHandleMessages<CycleImported>
    {
        public async Task Handle(CycleImported message)
        {
            received.Delivered();
            if (!await context.FirstDelivery(message.EventId))
                return;
            received.Handled(new(message, context.Headers.GetValueOrDefault(Headers.CorrelationId), CorrelationId.Current));
        }
    }

    public record Poison;

    class PoisonHandler : IHandleMessages<Poison>
    {
        public Task Handle(Poison message) => throw new InvalidOperationException("always fails");
    }
}
