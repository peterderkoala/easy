using Easy.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    public const string OutboxTable = "rebus_outbox"; // created by Rebus on start

    // queue null = one-way client (publish only).
    public static WebApplicationBuilder AddEasyMessaging(this WebApplicationBuilder builder, string? queue = null)
    {
        builder.Services.AddRebus((configure, services) =>
        {
            var config = services.GetRequiredService<IConfiguration>();
            var rabbit = config["Rebus:Rabbit"] ?? throw new InvalidOperationException("Rebus:Rabbit is not configured");
            var database = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
            return configure
                .Transport(t =>
                {
                    if (queue is null) t.UseRabbitMqAsOneWayClient(rabbit);
                    else t.UseRabbitMq(rabbit, queue);
                })
                .Outbox(o => o.StoreInPostgreSql(database, OutboxTable))
                .Options(o => o.Decorate<IPipeline>(c => new PipelineStepInjector(c.Get<IPipeline>())
                    .OnReceive(new IncomingCorrelation(), PipelineRelativePosition.Before, typeof(DefaultRetryStep))
                    .OnSend(new OutgoingCorrelation(), PipelineRelativePosition.Before, typeof(FlowCorrelationIdStep))));
        });
        return builder;
    }

    // Write requests run in one EF transaction; messages published during the request are stored in the outbox
    // on the same connection and transaction, and both commit together after the endpoint returns.
    // ponytail: the response may start before the commit; buffer or commit earlier if endpoints stream large bodies.
    public static IApplicationBuilder UseOutbox(this IApplicationBuilder app) =>
        app.UseWhen(c => !(HttpMethods.IsGet(c.Request.Method) || HttpMethods.IsHead(c.Request.Method) || HttpMethods.IsOptions(c.Request.Method)),
            write => write.Use(async (context, next) =>
            {
                var db = context.RequestServices.GetRequiredService<EasyDb>().Database;
                await using var transaction = await db.BeginTransactionAsync();
                using var scope = new RebusTransactionScope();
                scope.UseOutbox((NpgsqlConnection)db.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());

                await next(context);

                await scope.CompleteAsync();
                await transaction.CommitAsync();
            }));

    // Idempotent consumer, for use inside a handler: records the event ID in the handler's outbox transaction
    // (Rebus opens it per message and commits it after the handler). False = already processed, skip.
    public static async Task<bool> FirstDelivery(this IMessageContext context, string eventId)
    {
        // Key is internal to Rebus.PostgreSql (OutboxExtensions.CurrentOutboxConnectionKey); versions are pinned.
        var outbox = (OutboxConnection)context.TransactionContext.Items["current-outbox-connection"];
        await using var insert = new NpgsqlCommand("INSERT INTO processed_events (id) VALUES ($1) ON CONFLICT DO NOTHING", outbox.Connection, outbox.Transaction)
        {
            Parameters = { new() { Value = eventId } },
        };
        return await insert.ExecuteNonQueryAsync() == 1;
    }

    sealed class IncomingCorrelation : IIncomingStep
    {
        public async Task Process(IncomingStepContext context, Func<Task> next)
        {
            var id = context.Load<TransportMessage>().Headers.GetValueOrDefault(Headers.CorrelationId);
            CorrelationId.Current = id;
            using (LogContext.PushProperty("CorrelationId", id))
                await next();
        }
    }

    // Entry points (HTTP middleware, later Hangfire jobs) set CorrelationId.Current; Rebus carries it in rbs2-corr-id.
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
