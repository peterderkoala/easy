---
status: accepted
---
# Blinding at application level through one visibility function

Blinding of examiner gradings (S06) is enforced by one function in MainApi that decides, per viewer and documentation, which examiner gradings' points and remarks the read DTO may contain. All gradings live in one table. We rejected separate tables per examiner (no gain, since one API serves all of them) and Postgres row-level security (it would need the viewer's role and summary-view state inside the database). Hub messages never carry points, so the REST read is the only path that can leak.

## Consequences

- Every read of points or remarks goes through the function. A new endpoint that bypasses it breaks blinding, so the blinding integration test checks the REST read and every received hub message.
- Admins and services with direct database access see everything. Blinding protects examiners from each other, not data from operators.
