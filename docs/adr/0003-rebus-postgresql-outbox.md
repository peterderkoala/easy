---
status: accepted
---
# Transactional outbox with Rebus.PostgreSql

Events that follow a database write go through the `Rebus.PostgreSql` outbox, which shares the Npgsql connection and transaction with EF Core. A crash between commit and publish no longer loses events. The library calls its outbox experimental, so versions are pinned (Rebus 8.9.x, Rebus.PostgreSql 9.1.x) and the fallback is a hand-rolled outbox table with a forwarder.

## Consequences

- Delivery is at-least-once, so every consumer is idempotent and events carry deterministic IDs.
- EF Core's retrying execution strategy cannot be used on these contexts.
- Hangfire has no Rebus integration. Jobs that only publish do so directly and rely on Hangfire retries; a job that also writes to the database uses the outbox.
- One integration test runs against a failing broker.
