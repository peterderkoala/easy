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
- [ ] Spec 19: as `admin` import a cycle (a CSV in the format of `docs/samples/antragimport`), then open it from the start page (or "Dokumentationen des Prüfungszeitraums" on the import page). "Anmelden" on a row as `admin`, then as `examiner` in a private window: both names show and the row offers "Abmelden". As `examiner2`/`examiner2` the row shows "Voll" and no button. "Abmelden" as `examiner` reopens the place: after a reload `examiner2` gets "Anmelden".
- [ ] Spec 20: upload a documentation PDF for a participant, register `admin` and `examiner` (Spec 19 list), open "Bewerten" in two browsers (one private window). The PDF shows left; setting "Seite" on a tab jumps it there. A point or remark saved by one turns their status to "Entwurf" on the other side without their points. "Übersicht öffnen" unblinds only that side and tells the other. Submit both with a section-3 gap above 4: the notice names the sections, no numbers. Either both "Abweichung akzeptieren" (graded, score and grade shown), or invite `examiner2` ("Drittprüfer:in"): after "annehmen" `examiner2` grades blind, the pair can no longer withdraw or accept, and the third submit grades with the average of three. The same examiner in a second tab, clicking a point at the same moment: one tab shows "Von anderer Stelle geändert; neu geladen."
- [ ] Spec 21: as `admin` open "Einstellungen" on the start page. Change "Gesamt" to 12 and save: "Gespeichert.", and after a reload it shows 12. Change a cycle's weights to 15/10/40/20/15 and save; weights not adding up to 100 show the API's error. On a cycle with a submitted grading (Spec 20) saving shows "Weights are fixed once an examiner grading in the cycle is submitted." As `examiner`, `/settings` shows "Nur für Admins." and the start page has no link.
- [ ] Spec 22: `docker compose -f compose.dev.yml exec -u rabbitmq rabbitmq rabbitmqctl list_policies` shows `error-ttl` on `^error$` with `message-ttl` 604800000; the RabbitMQ management UI (`http://127.0.0.1:15672`) still accepts `RABBITMQ_USER`/`RABBITMQ_PASSWORD` from `.env`.

## License

MIT. See [LICENSE](LICENSE).
