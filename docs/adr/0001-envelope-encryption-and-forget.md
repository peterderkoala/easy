---
status: accepted
---
# Per-participant envelope encryption; forget deletes the wrapped key

Participant personal data is encrypted with a random per-participant data key. A master secret outside the database wraps that key, and only the wrapped key is stored. This replaces S02's "derived from participant information", which would put key material in the database. "Forget" deletes the wrapped key and the participant's SeaweedFS objects. The master secret is a Docker secret file mounted into `Easy.MainApi` only, and each wrapped key records the master-secret version.

## Considered Options

- **Key derived from participant data (S02):** rejected, the key is recoverable from the database it protects.
- **Examiner-held secret:** rejected, it makes import, background jobs and recovery depend on a person.
- **External KMS now:** deferred, the master secret sits behind an interface so a KMS can replace it later.

## Consequences

- Threat model is a database-only leak (dump, backup, SQL injection read). Full host compromise is not defended against.
- Encrypted: name, project title, CSV id number, comments, documents. Readable: points, status, display number.
- Encrypted backups are kept about 30 days. Until they expire, forget is reversible from a backup, and this is a spec requirement. After that the data is unrecoverable.
- Master-secret rotation is manual re-wrapping, with no automatic rotation in the skeleton.
- The CSV import message carries the uploaded file encrypted under a one-off data key of the same kind, so no plain participant data sits in the outbox table or the broker (S02).
