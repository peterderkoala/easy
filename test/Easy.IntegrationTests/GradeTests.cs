extern alias mainapi;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Messages;

namespace Easy.IntegrationTests;

// Spec 5 (issue 20): score write with a synchronous audit entry and an outbox event (S11).
[Collection(nameof(Infra))]
public class GradeTests(KeycloakFixture keycloak, Infra infra) : IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string token = null!;
    readonly Guid participant = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        await Db(async db =>
        {
            var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026 }).Entity;
            db.Participants.Add(new() { Id = participant, CycleId = cycle.Id, DisplayNo = "WS2026-001", CsvId = [], Name = [], ProjectTitle = [] });
            await db.SaveChangesAsync();
        });
        token = await keycloak.LoginAsync("examiner");
        (await Send(HttpMethod.Post, "/api/me", token)).EnsureSuccessStatusCode(); // examiner mirror
    }

    public async Task DisposeAsync() => await mainApi.DisposeAsync();

    [Fact]
    public async Task Score_write_creates_audit_entry_in_same_transaction()
    {
        (await WriteScore(12.5m)).EnsureSuccessStatusCode();
        (await WriteScore(13m)).EnsureSuccessStatusCode();
        (await WriteScore(13m)).EnsureSuccessStatusCode(); // unchanged: no entry

        var grade = Assert.Single(await Db(db => db.Grades.ToListAsync()));
        Assert.Equal(13m, grade.Value);
        var entries = await Db(db => db.AuditEntries.OrderBy(e => e.Id).ToListAsync());
        Assert.Collection(entries,
            e => Assert.Equal((null, "12.5"), (e.Old, e.New)),
            e => Assert.Equal(("12.5", "13.0"), (e.Old, e.New)));
        Assert.All(entries, e =>
        {
            Assert.Equal(("Grade", "Value", AuditType.Initial, participant), (e.Entity, e.Field, e.Type, e.ParticipantId));
        });
    }

    [Fact]
    public async Task Failed_write_creates_no_audit_entry_and_no_event()
    {
        using var consumer = await Consumer();

        // Unknown participant: the insert fails on the foreign key after the audit entry was added to the same save.
        Assert.Equal(HttpStatusCode.NotFound, (await WriteScore(10m, participantId: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await WriteScore(-1m)).StatusCode);
        (await WriteScore(10m)).EnsureSuccessStatusCode();

        // Forwarded in order, so a leaked event from a failed write would arrive first.
        Assert.Equal(participant, (await consumer.Next()).Event.ParticipantId);
        Assert.Single(await Db(db => db.AuditEntries.ToListAsync()));
    }

    [Fact]
    public async Task Audit_entry_has_actor_and_correlation_id()
    {
        (await WriteScore(7m, correlationId: "corr-grade-1")).EnsureSuccessStatusCode();

        var entry = Assert.Single(await Db(db => db.AuditEntries.ToListAsync()));
        string subject = await Db(db => db.Examiners.Select(e => e.Subject).SingleAsync());
        Assert.Equal(subject, entry.ActorId);
        Assert.Equal("corr-grade-1", entry.CorrelationId);
    }

    [Fact]
    public async Task GradeChanged_is_in_rabbitmq_with_rbs2_corr_id()
    {
        using var consumer = await Consumer();

        (await WriteScore(8m, correlationId: "corr-grade-2")).EnsureSuccessStatusCode();

        var delivery = await consumer.Next();
        Assert.Equal((participant, "Written"), (delivery.Event.ParticipantId, delivery.Event.Source));
        Assert.Equal("corr-grade-2", delivery.CorrelationId);
    }

    [Fact]
    public async Task Audit_entry_cannot_be_updated_or_deleted()
    {
        (await WriteScore(9m)).EnsureSuccessStatusCode();

        await Db(async db =>
        {
            var entry = await db.AuditEntries.SingleAsync();
            entry.New = "99";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        await Db(async db =>
        {
            db.AuditEntries.Remove(await db.AuditEntries.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        Assert.Equal("9.0", (await Db(db => db.AuditEntries.SingleAsync())).New);
    }

    [Fact]
    public async Task Only_authenticated_examiner_can_write_score()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await WriteScore(5m, token: "")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WriteScore(5m, token: await keycloak.ServiceAccountTokenAsync())).StatusCode);
        Assert.Empty(await Db(db => db.AuditEntries.ToListAsync()));

        (await WriteScore(5m)).EnsureSuccessStatusCode();
    }

    Task<HttpResponseMessage> WriteScore(decimal value, Guid? participantId = null, string? token = null, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/participants/{participantId ?? participant}/grades/Written")
        {
            Content = JsonContent.Create(new { value }),
        };
        if ((token ?? this.token) is { Length: > 0 } bearer) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (correlationId is not null) request.Headers.Add(CorrelationId.Header, correlationId);
        return mainApi.CreateClient().SendAsync(request);
    }

    Task<HttpResponseMessage> Send(HttpMethod method, string path, string bearer) =>
        mainApi.CreateClient().SendAsync(new(method, path) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer) } });

    async Task<T> Db<T>(Func<EasyDb, Task<T>> query)
    {
        using var scope = mainApi.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyDb>());
    }

    Task Db(Func<EasyDb, Task> action) => Db(async db => { await action(db); return 0; });

    async Task<GradeConsumer> Consumer()
    {
        var consumer = new GradeConsumer();
        var bus = Configure.With(consumer.Activator)
            .Transport(t => t.UseRabbitMq(infra.Rabbit.GetConnectionString(), $"test-{Guid.NewGuid():N}"))
            .Start();
        await bus.Subscribe<GradeChanged>();
        return consumer;
    }

    public record Delivery(GradeChanged Event, string? CorrelationId);

    public sealed class GradeConsumer : IDisposable
    {
        readonly BlockingCollection<Delivery> received = new();

        public BuiltinHandlerActivator Activator { get; } = new();

        public GradeConsumer() =>
            Activator.Handle<GradeChanged>((_, context, message) =>
            {
                received.Add(new(message, context.Headers.GetValueOrDefault(Headers.CorrelationId)));
                return Task.CompletedTask;
            });

        public Task<Delivery> Next() => Task.Run(() =>
            received.TryTake(out var delivery, TimeSpan.FromSeconds(20)) ? delivery : throw new TimeoutException("no GradeChanged received"));

        public void Dispose() => Activator.Dispose();
    }
}
