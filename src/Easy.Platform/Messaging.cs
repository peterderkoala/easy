using Easy.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Config.Outbox;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;
using Rebus.PostgreSql.Outbox;
using Rebus.Retry.Simple;
using Rebus.Transport;
using Serilog.Context;

namespace Easy.Platform;

// Rebus on RabbitMQ with the Rebus.PostgreSql transactional outbox (ADR 0003). Delivery is at-least-once.
public static class Messaging
{
    // ponytail: one table for all hosts on the shared database, so any host's forwarder may send another's rows;
    // harmless with idempotent consumers, per-host tables if forwarding order or load ever matters
    public const string OutboxTable = "rebus_outbox"; // created by Rebus on start
    public const string ErrorQueue = "error"; // shared by all hosts; the compose post_start policy matches this name (7-day TTL)

    // queue null = one-way client (publish only). Rebus:Queue overrides the queue name; tests give each host its own.
    // Never set it for more than one host in Compose, or they would share one input queue.
    // onCreated runs once the bus is up, e.g. for subscriptions.
    public static WebApplicationBuilder AddEasyMessaging(this WebApplicationBuilder builder, string? queue = null, Func<IBus, Task>? onCreated = null)
    {
        int before = builder.Services.Count;
        builder.Services.AddRebus((configure, services) =>
        {
            var config = services.GetRequiredService<IConfiguration>();
            var rabbit = config["Rebus:Rabbit"] ?? throw new InvalidOperationException("Rebus:Rabbit is not configured");
            var database = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
            return configure
                .Transport(t =>
                {
                    if (queue is null) t.UseRabbitMqAsOneWayClient(rabbit);
                    else t.UseRabbitMq(rabbit, config["Rebus:Queue"] ?? queue);
                })
                .Outbox(o => o.StoreInPostgreSql(database, OutboxTable))
                // Rebus:ErrorQueue lets tests give each host its own.
                // Second-level retries: an IFailed<T> handler may end a failed message; without one it is dead-lettered as before.
                .Options(o => o.RetryStrategy(errorQueueName: config["Rebus:ErrorQueue"] ?? ErrorQueue, secondLevelRetriesEnabled: true))
                .Options(o => o.Decorate<IPipeline>(c => new PipelineStepInjector(c.Get<IPipeline>())
                    .OnReceive(new IncomingCorrelation(), PipelineRelativePosition.Before, typeof(DefaultRetryStep))
                    .OnSend(new OutgoingCorrelation(), PipelineRelativePosition.Before, typeof(FlowCorrelationIdStep))));
        }, onCreated: onCreated);

        // ponytail: under WebApplicationFactory two threads stop the host at once (factory Dispose and Program's app.Run()),
        // RebusBus.Dispose is not thread-safe, and Rebus.PostgreSql's OutboxForwarder.Dispose throws on a second call (#25,
        // docs/research/rebus-postgresql-double-dispose.md). Serialize the bus's StopAsync; drop once disposal is idempotent upstream.
        // Exactly the hosted service AddRebus just registered (factory-based in Rebus.ServiceProvider 10.7); throws at start-up if that changes.
        var rebus = builder.Services.Skip(before).Single(d => d.ServiceType == typeof(IHostedService));
        var factory = rebus.ImplementationFactory ?? throw new InvalidOperationException("Rebus hosted service is no longer factory-registered; revisit StopOnce.");
        builder.Services[builder.Services.IndexOf(rebus)] = ServiceDescriptor.Singleton<IHostedService>(p => new StopOnce((IHostedService)factory(p)));
        return builder;
    }

    sealed class StopOnce(IHostedService inner) : IHostedService
    {
        readonly Lock gate = new();
        Task? _stop;

        public Task StartAsync(CancellationToken cancellationToken) => inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (gate) return _stop ??= inner.StopAsync(cancellationToken);
        }
    }

    // Write requests run in one EF transaction; messages published during the request are stored in the outbox
    // on the same connection and transaction, and both commit together after the endpoint returns a non-error status.
    // ponytail: the response may start before the commit; buffer or commit earlier if endpoints stream large bodies.
    // SignalR transport requests (/hubs) are not writes; hubs send after their own save.
    public static IApplicationBuilder UseOutbox(this IApplicationBuilder app) =>
        app.UseWhen(c => !(HttpMethods.IsGet(c.Request.Method) || HttpMethods.IsHead(c.Request.Method) || HttpMethods.IsOptions(c.Request.Method)
                || c.Request.Path.StartsWithSegments("/hubs")),
            write => write.Use(async (context, next) =>
            {
                var db = context.RequestServices.GetRequiredService<EasyDb>().Database;
                var afterCommit = new List<Func<Task>>();
                context.Items[AfterCommitKey] = afterCommit;
                await using var transaction = await db.BeginTransactionAsync();
                using var scope = new RebusTransactionScope();
                scope.UseOutbox((NpgsqlConnection)db.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());

                await next(context);
                if (context.Response.StatusCode >= 400)
                    return; // error result without exception: dispose rolls back rows and messages

                await scope.CompleteAsync();
                await transaction.CommitAsync();
                foreach (var action in afterCommit)
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception e) // the write stands; a lost notification is healed by the client's refetch
                    {
                        context.RequestServices.GetRequiredService<ILogger<EasyDb>>().LogWarning(e, "After-commit action failed.");
                    }
                }
            }));

    static readonly object AfterCommitKey = new();

    // Runs after UseOutbox committed the request, never on rollback. For hub notifications (notify then refetch):
    // sent earlier, a client could refetch the old state. Write requests only.
    public static void AfterCommit(this HttpContext context, Func<Task> action) =>
        (context.Items[AfterCommitKey] as List<Func<Task>> ?? throw new InvalidOperationException("AfterCommit needs a write request under UseOutbox.")).Add(action);

    // Idempotent consumer, for use inside a handler: records the event ID in the handler's outbox transaction
    // (Rebus opens it per message and commits it after the handler). False = already processed, skip.
    public static async Task<bool> FirstDelivery(this IMessageContext context, string eventId)
    {
        var outbox = Outbox(context);
        await using var insert = new NpgsqlCommand("INSERT INTO processed_events (id) VALUES ($1) ON CONFLICT DO NOTHING", outbox.Connection, outbox.Transaction)
        {
            Parameters = { new() { Value = eventId } },
        };
        return await insert.ExecuteNonQueryAsync() == 1;
    }

    // Puts a handler's EF writes on the message's outbox connection and transaction, so FirstDelivery, business rows
    // and published messages commit together. Call before the context touches the database.
    public static void UseOutboxTransaction(this IMessageContext context, EasyDb db)
    {
        var outbox = Outbox(context);
        db.Database.SetDbConnection(outbox.Connection);
        db.Database.UseTransaction(outbox.Transaction);
    }

    // ponytail: key is internal to Rebus.PostgreSql (OutboxExtensions.CurrentOutboxConnectionKey), pinned versions keep it stable;
    // re-check on every Rebus.PostgreSql upgrade, or switch to the hand-rolled outbox (ADR 0003 fallback) if it moves.
    static OutboxConnection Outbox(IMessageContext context) => (OutboxConnection)context.TransactionContext.Items["current-outbox-connection"];

    sealed class IncomingCorrelation : IIncomingStep
    {
        public async Task Process(IncomingStepContext context, Func<Task> next)
        {
            var id = CorrelationId.ValidOrNew(context.Load<TransportMessage>().Headers.GetValueOrDefault(Headers.CorrelationId));
            CorrelationId.Current = id;
            using (LogContext.PushProperty("CorrelationId", id))
                await next();
        }
    }

    // Entry points (HTTP middleware, Hangfire jobs) set CorrelationId.Current; Rebus carries it in rbs2-corr-id.
    sealed class OutgoingCorrelation : IOutgoingStep
    {
        public Task Process(OutgoingStepContext context, Func<Task> next)
        {
            var headers = context.Load<Message>().Headers;
            if (CorrelationId.Current is { } id)
                headers.TryAdd(Headers.CorrelationId, id);
            return next();
        }
    }
}
