# Research: SeaweedFS SSE-C enforcement and S3 upload options

Issue: peterderkoala/easy#2. Researched 2026-10-10.
Caveat: `CONCEPT.md` (S06, S02, S16) is not present in this worktree (only LICENSE/README), so the S06 "older versions have gaps" claim and the S16 upload design were not read. Consequences below are stated against the question text only.

## Findings

1. **Current version.** Latest release is SeaweedFS 4.48 (2026-09-28). Source: https://github.com/seaweedfs/seaweedfs/releases
2. **Read enforcement (master source).** `GetObjectHandler` -> `streamFromVolumeServersWithSSE` parses SSE-C headers. Missing key -> `ErrSSECustomerKeyMissing`; key MD5 != stored MD5 -> `ErrAccessDenied`. Both run before the status line is committed. Same checks for directory markers (`checkDirectorySSEAccess`). Source: https://github.com/seaweedfs/seaweedfs/blob/master/weed/s3api/s3api_object_handlers.go
   - Gap: the mismatch check only runs when `storedKeyMD5 != ""`. An entry without stored MD5 decrypts with the supplied key (garbage, no error).
   - HEAD on regular objects was not confirmed (code not in the part read).
   - The MD5 comparison is the only guard against a wrong key (inference, not verified in source).
3. **Documented support.** Wiki lists SSE-C, SSE-KMS, SSE-S3, presigned URLs and all seven multipart operations as supported; range requests work with SSE. Sources: https://github.com/seaweedfs/seaweedfs/wiki/Amazon-S3-API, https://github.com/seaweedfs/seaweedfs/wiki/Server-Side-Encryption. Keys are never stored server-side; lost key = unrecoverable data. The wiki SSE-C guide does not address an HTTPS requirement, multipart-with-SSE-C, presigned, or delete semantics.
4. **Presigned + SSE-C.** Presigned GET with SSE-C was broken (403 SignatureDoesNotMatch) after PR #9121 when key/MD5 headers were unsigned (the pattern AWS SDKs such as .NET use). Fixed on master by PR #11578 (merged 2026-10-03): key and key-MD5 headers are exempt from signing on presigned requests; the algorithm header must still be signed. Sources: https://github.com/seaweedfs/seaweedfs/issues/11556, https://github.com/seaweedfs/seaweedfs/pull/11578
   - The fix post-dates 4.48 (09-28), so 4.48 almost certainly lacks it. The release containing it was not identified; pin a later release or verify.
   - From the PR thread: a presigned PUT holder could supply their own key (maintainer: same as AWS behavior). A presigned upload therefore does not bind the encryption key.
5. **Multipart + SSE.** Many fixes in the area (e.g. #8216 SSE-C chunk offsets for large chunked uploads, #9228 multipart-SSE truncated GETs, #9280 UploadPartCopy re-encryption, #10971 UploadPartCopy with volume encryption in 4.45). This shows active support but also recurring bugs. SSE-C-specific multipart behavior (same key per part, completion) is not documented in the primary docs read. Search listing: https://api.github.com/search/issues?q=repo:seaweedfs/seaweedfs+SSE-C+multipart (only 17 of 33 results read).
6. **Delete / "forget".** `DeleteObject` handlers never read SSE-C headers, so deleting needs no key. Chunk removal is delegated to the filer (`deleteObjectEntry(..., !metadataOnly, ...)`); whether chunk deletion is synchronous or via background GC was not determined. Source: https://github.com/seaweedfs/seaweedfs/blob/master/weed/s3api/s3api_object_handlers_delete.go
   - `filer.backup`/`filer.replicate` cannot decrypt SSE-C objects (skipped with error); `filer.sync` copies encryption metadata. Backups/replicas of ciphertext may outlive a delete.

## Consequences for upload design (S16)

- **Proxy via main-api:** the app holds the key per request and sets SSE-C headers. Key binding is guaranteed, there is no presign/SSE-C compatibility gap, and no dependence on the #11578 fix. Cost: main-api carries upload bandwidth.
- **Presigned direct upload:** needs a build containing #11578 (post-4.48); the client must send key headers; the server cannot bind the key (see 4). Requires TLS end to end.
- **Forget design:** crypto-shredding (discard the key) is the robust path since SSE-C keys are never stored server-side. Physical delete is a second layer, but chunk GC timing and replica/backup copies are unverified.

## Not verified / follow-ups

- Which release first includes #11578; HEAD behavior; SSE-C multipart completion semantics; chunk GC timing after delete; HTTPS enforcement for SSE-C (AWS requires it; SeaweedFS doc examples use plain http). Best resolved with a spike test against a pinned version.
