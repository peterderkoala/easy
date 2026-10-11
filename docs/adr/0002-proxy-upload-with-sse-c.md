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
- A SeaweedFS release containing the fix must be pinned; the SSE-C read test is part of the upload spec.
- The 15 MB limit is enforced at the gateway, Kestrel and MainApi from one setting.
