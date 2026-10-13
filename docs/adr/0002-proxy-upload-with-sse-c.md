---
status: accepted
---
# Documents are uploaded through MainApi and encrypted with SSE-C

MainApi receives the PDF, unwraps the participant's data key in memory and writes the object to SeaweedFS with that key as the SSE-C key. Presigned direct upload is rejected: a presigned PUT cannot bind the encryption key to the object, and presigned plus SSE-C needs a SeaweedFS fix (PR 11578) that is newer than release 4.48. At 15 MB per file the proxy bandwidth is not a concern.

## Considered Options

- **Presigned direct upload:** rejected, see above.
- **Application-level AES-GCM before upload:** the documented fallback if the SSE-C spike (read with a wrong key must fail) shows gaps. It removes the dependence on SeaweedFS enforcement but makes objects opaque to SeaweedFS tooling.

## Consequences

- Only MainApi touches documents. Notification and ExamRoom never access SeaweedFS.
- SeaweedFS 4.48 is pinned. No release contains PR 11578 yet, but it only matters for presigned requests, which proxy upload never sends. The spike (`SseCSpikeTests`, spec 7) shows 4.48 refuses a signed read with a wrong key or without SSE-C headers and stores ciphertext, so the AES-GCM fallback is not needed. Re-run the spike on every upgrade; presigned upload would need a release with the fix.
- The SSE-C key is the participant data key itself (S06: no separate key). SeaweedFS sees it per request but never stores it.
- 4.48 rejects signed S3 requests unless an identity is configured, so SeaweedFS runs with an admin identity (`SEAWEEDFS_ACCESS_KEY`/`SEAWEEDFS_SECRET_KEY`) and MainApi signs with it.
- The 15 MB limit is enforced at the gateway, Kestrel and MainApi from one setting.
