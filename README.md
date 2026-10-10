# easy

Web platform for collaborative work by IHK examiners: cycle import, application approval, split-view grading, virtual oral exams, dashboards.

## Project state

- **Phase:** concept. No application code yet.
- **Concept:** [`CONCEPT.md`](CONCEPT.md) is the single source of truth. Sections S00–S16, with a "READ FIRST" list of known flaws and gaps at the top.
- **Planned stack:** .NET 10, ASP.NET-hosted Blazor WebAssembly, SignalR, PostgreSQL with EF Core, RabbitMQ with Rebus, YARP gateway, SeaweedFS, Keycloak, Hangfire, Docker Compose.
- **Blockers before implementation:**
  - Overall grade formula and written-exam logic are undefined.
  - Encryption-key "forget" design needs a decision (key material and salt location).
  - Audit-log content and retention need a privacy check.
- **Lint:** not applicable yet, no source files.

## License

MIT. See [LICENSE](LICENSE).
