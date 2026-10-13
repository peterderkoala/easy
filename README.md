# easy

Web platform for collaborative work by IHK examiners: cycle import, application approval, split-view grading, virtual oral exams, dashboards.

## Project state

- **Phase:** walking skeleton. Specs are GitHub issues 16 to 24.
- **Concept:** [`CONCEPT.md`](CONCEPT.md) is the single source of truth. Sections S00–S16, with a "READ FIRST" list of known flaws and gaps at the top.
- **Planned stack:** .NET 10, ASP.NET-hosted Blazor WebAssembly, SignalR, PostgreSQL with EF Core, RabbitMQ with Rebus, YARP gateway, SeaweedFS, Keycloak, Hangfire, Docker Compose.
- **Blockers before implementation:**
  - Overall grade formula and written-exam logic are undefined.
  - Encryption-key "forget" design needs a decision (key material and salt location).
  - Audit-log content and retention need a privacy check.

## First run

Needs Docker and, for local builds and tests, the .NET 10 SDK.

```sh
cp .env.example .env
mkdir -p secrets && head -c 32 /dev/urandom | base64 > secrets/master_secret
docker compose -f compose.dev.yml up --build
```

The gateway listens on http://127.0.0.1:8080. Keycloak is on 8081, the Mailpit UI on 8025, RabbitMQ management on 15672, Postgres on 5432 (all on 127.0.0.1 only).

Tests: `dotnet test` in the repo root.

## Smoke checklist

One line per proving path. Each spec adds its line.

- [ ] Spec 1: `docker compose -f compose.dev.yml ps` shows every container healthy; `curl 127.0.0.1:8080/health/main-api` and `/health/exam-room` answer `Healthy Easy.MainApi` and `Healthy Easy.ExamRoom`; `http://127.0.0.1:8080/` loads the Blazor client.
- [ ] Spec 2: on `http://localhost:8080/` "Anmelden" logs in through Keycloak as `admin`/`admin` (or `examiner`/`examiner`) and shows the name and roles `examiner, admin`; `curl 127.0.0.1:8080/whoami` without a token answers 401; after the login `docker compose -f compose.dev.yml exec postgres psql -U easy -d easy -c 'select * from examiners'` shows the user with role `Admin`.
- [ ] Spec 3: `docker compose -f compose.dev.yml exec postgres psql -U easy -d easy -c '\dt'` lists the skeleton tables (`cycles`, `participants`, `examiners`, `oral_sessions`, `grades`, `audit_entries`, `documents`, `processed_events`, `rebus_outbox`); `docker compose -f compose.dev.yml logs main-api` shows `Bus "Rebus 1" started` and the `OutboxForwarder` task. New migration: `ConnectionStrings__Default=Host=x dotnet ef migrations add <Name> -p src/Easy.Data -s src/Easy.MainApi`.

## License

MIT. See [LICENSE](LICENSE).
