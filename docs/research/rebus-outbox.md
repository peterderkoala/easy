# Research: Rebus + RabbitMQ transactional outbox on EF Core / PostgreSQL

Issue: peterderkoala/easy#3. Context: CONCEPT.md S09, S11, S12 (dual-write gap, "single messaging layer", correlation ID in headers). Researched 2026-10-10 against primary sources (source code and docs of the owning projects). Package versions are from the NuGet flat-container index on that date.

## Answer in short

1. **Yes, with caveats.** `Rebus.PostgreSql` (>= 8.2.0-b3, current 9.1.1) ships a transactional outbox for Npgsql/PostgreSQL. It is not EF Core specific: it enlists on an `NpgsqlConnection` + `NpgsqlTransaction`. EF Core can share that connection/transaction through `Database.UseTransactionAsync`, so DB commit and "message stored for sending" are one atomic commit. Delivery to RabbitMQ is afterwards a separate, retried step (at-least-once).
2. **Hangfire and Rebus have no integration.** No `Rebus.Hangfire` package exists on NuGet, and neither the Rebus wiki nor Rebus source mention Hangfire. "Hangfire uses Rebus" (S09/S12) means a Hangfire job method calls `IBus.Publish`/`Send`. Treat the Hangfire job as a normal non-handler caller of the outbox.
3. **Correlation ID** is carried in the transport header `rbs2-corr-id` (plus `rbs2-corr-seq`), auto-propagated by Rebus. It survives the outbox because full headers are persisted with each outbox row.

## 1. Outbox support

Sources:
- Rebus wiki "Outbox": https://github.com/rebus-org/Rebus/wiki/Outbox
- Rebus.PostgreSql source and CHANGELOG: https://github.com/rebus-org/Rebus.PostgreSql (`Rebus.PostgreSql/Config/Outbox/*`, `Rebus.PostgreSql/PostgreSql/Outbox/*`, `CHANGELOG.md`)

Findings:
- The wiki page says "Rebus has an outbox implementation for Microsoft SQL Server". It only documents SQL Server. The wiki lags the code: Rebus.PostgreSql CHANGELOG has "8.2.0-b3: Add outbox", "Add ambient transaction support for outbox", and "9.0.0: Update to Rebus 8; clean up outbox messages as they're processed". Rebus 8.9.5 and Rebus.PostgreSql 9.1.1 are the latest on NuGet; Rebus.PostgreSql targets netstandard2.0, depends on Npgsql 8.0.3 and Rebus 8.4.2 (csproj).
- The configuration API's own XML doc calls it "the experimental outbox support" (`PostgreSqlOutboxConfigurationExtensions`). Treat as less battle-tested than the transport.
- Enable (mirrors the SQL Server wiki example, Postgres variant from `OutboxExtensions.StoreInPostgreSql`):
  ```csharp
  services.AddRebus(c => c
      .Transport(t => t.UseRabbitMq(amqp, "queue"))
      .Outbox(o => o.StoreInPostgreSql(connectionString, "RebusOutbox")));
  ```
  The table is created at startup (`Id bigint identity`, `MessageId`, `SourceQueue`, `DestinationAddress`, `Headers text`, `Body bytea`, `Sent bool`).
- Mechanism (source): an outgoing pipeline step plus a transport decorator redirect sends into the outbox table using the current connection/transaction; a background `OutboxForwarder` polls every 1 s, runs `DELETE ... WHERE Id IN (SELECT ... ORDER BY Id FOR UPDATE SKIP LOCKED LIMIT 100) RETURNING ...`, sends via the real transport (with retries), then commits the batch. Multiple forwarders are safe (SKIP LOCKED).
- **Delivery guarantee**: if the process dies after the broker accepted a batch but before the batch transaction commits, the rows reappear and are re-sent. That is at-least-once, so consumers must be idempotent (Rebus wiki "Delivery guarantees" and "Idempotence": exactly-once is "simply not possible in the general case"). Order is by `Id` within a batch, not guaranteed globally across forwarders.
- RabbitMQ side: Rebus.RabbitMq enables publisher confirms by default (CHANGELOG: "Enable publisher confirms by default, because otherwise RabbitMQ can LOSE your messages"), so the forwarder only counts a message as sent after a broker confirm. Opt out per message with the `rbs2-express` header. https://github.com/rebus-org/Rebus.RabbitMq (CHANGELOG, `RabbitMqTransport.cs`: `EnablePublisherConfirms`).

### Two usage scenarios (Rebus wiki "Outbox", Postgres equivalents in `OutboxExtensions.cs` and tests)

1. **Inside a Rebus handler**: Rebus opens the connection and transaction (`OutboxIncomingStep`), puts an `OutboxConnection` in `ITransactionContext.Items["current-outbox-connection"]`, and commits it after the handler succeeds. Your own DB work (including EF Core) must use that connection/transaction, and must not close, commit or dispose them.
2. **Outside a handler** (ASP.NET request, Hangfire job): you own the `NpgsqlConnection`/`NpgsqlTransaction`; wrap the work in `new RebusTransactionScope()`, call `scope.UseOutbox(connection, transaction)`, run code that calls `bus.Publish/Send`, `await scope.CompleteAsync()` (inserts outbox rows), then commit. `UseOutbox` throws if called twice on a scope. The wiki suggests ASP.NET Core middleware for this. The source also supports an ambient `System.Transactions` transaction (`OutboxOutgoingStep`).

## 2. EF Core on PostgreSQL integration

Source: EF Core docs "Transactions", https://learn.microsoft.com/en-us/ef/core/saving/transactions (updated 2026-08).

- Rebus does not take a `DbContext`. EF Core is wired by sharing the ADO.NET objects: create the `NpgsqlConnection`, `BeginTransaction`, build `DbContextOptions` with `UseNpgsql(connection)`, then `await context.Database.UseTransactionAsync(transaction)`. The docs show this exact pattern for "external DbTransactions" (shown for SqlClient; same API for any relational provider).
- Inside a handler, do the reverse: take the `NpgsqlConnection`/`NpgsqlTransaction` from `OutboxConnection` and hand them to the scoped `DbContext` (`UseNpgsql(conn)` + `UseTransactionAsync(tx)`). Needs a small adapter (e.g. a scoped service that reads `MessageContext.Current`), not provided by Rebus.
- Caveat from EF docs: manually controlled transactions are incompatible with implicit retrying execution strategies (`EnableRetryOnFailure`); do not enable it on these contexts, or wrap in `Database.CreateExecutionStrategy().ExecuteAsync`.
- Caveat: avoid `TransactionScope` with Npgsql plus a second enlisting resource on Linux; the EF docs state distributed transactions are Windows-only (.NET 7+) and that provider support for System.Transactions must be tested. Prefer the explicit connection/transaction route.
- `SaveChanges` creates savepoints inside an existing transaction; harmless for the outbox.
- Third-party `akanieski/Rebus.EntityFramework` exists (0 stars, last push 2024-11) but is persistence, not an outbox. Not recommended.
- Alternative: `rebus-org/Freakout` (general outbox by the Rebus author, has Npgsql package) is archived (as of 2026-09), so not a candidate.

## 3. Hangfire and Rebus (S09 "single messaging layer")

Sources: Rebus wiki (grep of every page: no Hangfire mention), NuGet index (no `Rebus.Hangfire`), Hangfire.PostgreSql README https://github.com/hangfire-postgres/Hangfire.PostgreSql, Hangfire docs https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html.

- No official or community bridge. The pattern is: a Hangfire recurring job (deadline check, S12) resolves `IBus` and publishes. Hangfire runs its own storage in Postgres (`Hangfire.PostgreSql`, which uses Npgsql 6+); its job tables are independent from the Rebus outbox table.
- Hangfire retries failed jobs automatically (default 10 attempts per its docs), so a job can run more than once; the publish should therefore be idempotent or deduplicated by consumers (key on a deterministic message/business ID, e.g. deadline id + date).
- Dual-write inside a Hangfire job: the job normally only reads state and publishes, so the outbox matters only if the job also writes to the app DB in the same step. If it does, use scenario 2 (own connection + `RebusTransactionScope`). If it only publishes, no outbox is needed; a failed publish makes the job fail and Hangfire retries it. Do not try to enqueue Hangfire jobs and Rebus messages in one transaction: Hangfire's ambient-transaction enlistment is undocumented per Hangfire forum reports (https://discuss.hangfire.io/t/possible-bug-that-should-really-be-a-feature/667), treat as unsupported.
- Hosting detail (S09): the Hangfire container also needs the Rebus configuration (one-way client is enough: `Configure.OneWayClient()`, Rebus wiki "Different bus modes") and the outbox forwarder runs in any process that has `.Outbox(...)` configured with a transport that can receive no queue (check: the forwarder only needs `ITransport.Send`). **need check**: whether one-way client mode starts `OutboxForwarder`; the tests `TestOutbox_OutsideOfRebusHandler` use `CreateOneWayClient` with an outbox, which suggests yes.

## 4. Correlation IDs (S11)

Source: Rebus wiki "Correlation ids" https://github.com/rebus-org/Rebus/wiki/Correlation-ids; `Rebus/Messages/Headers.cs` in https://github.com/rebus-org/Rebus.

- Header `rbs2-corr-id` (`Headers.CorrelationId`) on the transport message, i.e. an AMQP header, not the body. Rule: keep an explicit value; else copy from the message being handled; else use the new message's `rbs2-msg-id`.
- `rbs2-corr-seq` (`Headers.CorrelationSequence`) is incremented per hop to give strict ordering of correlated messages. `rbs2-msg-id` and `rbs2-in-reply-to` also exist.
- Set it explicitly for an entry point (HTTP request or Hangfire job) so audit entries (S11 `AuditEntry.correlation_id`) match: `bus.Publish(msg, new Dictionary<string,string>{ [Headers.CorrelationId] = id })`, or an outgoing step that sets it from `HttpContext`/Hangfire job id. Rebus does not read ASP.NET `traceparent`.
- Read in handlers: `messageContext.Headers.GetValue(Headers.CorrelationId)`; the wiki suggests pushing it into the Serilog `LogContext` via an incoming step.
- With the outbox, headers are serialized into the `Headers` column and restored on forward (`OutboxMessage.ToTransportMessage`), so the correlation ID is unchanged by the outbox hop.

## Recommendation for the concept

- Close the S09/S11/S12 dual-write gap with `Rebus.PostgreSql` outbox, both in handlers and in API request middleware (scenario 2), sharing connection/transaction with EF Core. Add one integration test with a flaky/failed broker (the Rebus.PostgreSql tests already model this with `FlakySenderTransportDecorator`).
- Mark consumers idempotent (at-least-once is guaranteed by design); define dedupe key per event.
- Pin versions (Rebus 8.9.x, Rebus.PostgreSql 9.1.x) and treat the outbox as "experimental" in risk list; fallback is a hand-rolled outbox table + Hangfire/BackgroundService forwarder.
- Open for the main session: whether Hangfire jobs need the outbox at all (see section 3); EF Core adapter ownership; the `CorrelationId` source for HTTP entry points.
