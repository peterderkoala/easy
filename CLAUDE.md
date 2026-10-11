# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## State

Walking skeleton, built spec by spec (GitHub issues 16 to 24). .NET 10 solution `Easy.slnx`: hosts and libraries in `src/`, xUnit tests in `test/`.

- Build: `dotnet build`. Test: `dotnet test` (single project: `dotnet test test/Easy.Platform.Tests`).
- Run everything: see README "First run" (`docker compose -f compose.dev.yml up --build`). One root `Dockerfile` builds every host via the `PROJECT` build arg.
- Shared host wiring (Serilog JSON, correlation ID, `/health`) is `AddEasyPlatform()` / `UseEasyPlatform()` in `src/Easy.Platform`.
- No lint tooling yet. Do not invent commands.

`easy` is a web platform for collaborative work by IHK examiners: cycle import, application approval, split-view grading, virtual oral exams, dashboards.

## Source of truth

`CONCEPT.md` is the single source of truth. Read its "READ FIRST: Known flaws and gaps" list before planning any area; blockers there (overall grade formula, written-exam status, "began grading" trigger, encryption "forget" design) must be decided, not guessed.

Conventions in `CONCEPT.md`:
- Sections have fixed IDs S00–S16. Never renumber; cross-reference by ID. Use the Navigation table to find a topic.
- Markers: `[DECIDED]` binding, `[PROVISIONAL]` changeable, `[CLARIFICATION]` overrides the earlier statement, `[OPEN]` undecided (also listed in S10), `**need check**` unconfirmed.
- Participant status values are defined only in S04.

## Planned stack

.NET 10, ASP.NET-hosted Blazor WebAssembly, SignalR, PostgreSQL + EF Core, RabbitMQ + Rebus, YARP gateway, SeaweedFS, Keycloak, Hangfire, Docker Compose. TDD is the intended approach (S09).

## Other

- `docs/vault/` is an Obsidian vault (untracked); `docs/vault/easy/.obsidian/` is editor config.
- Branches: work on `dev`, PRs target `main`.

## Agent skills

### Issue tracker

Issues live in GitHub Issues (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-label vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.
