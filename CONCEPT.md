# Easy – Concept

Status: 2026-10-02 (brainstorming). Single source of truth for updates and references.

## READ FIRST: Known flaws and gaps

For the planning agent. Each item needs a decision or check before the affected area is planned. IDs in brackets point to the source section.

### Blockers (core output undefined)

- **Overall grade has no formula** [S04, S10]. Weighting of sources is open, and written-exam pass/fail logic is open. The main output of the system cannot be implemented or tested.
- **Written-exam status undefined** [S04, S10]. Same root cause as above.
- **RESOLVED (S05, issue 35): "Began grading" undefined** [S05, S15]. Now: an examiner has begun when they submit their examiner grading. Original problem: Examiner change discards unfinished grading only before "begun", and that trigger is not defined.

### Security and privacy

- **RESOLVED (ADR 0001, S02): Encryption "forget" may not delete anything** [S02]. Now envelope encryption with an external master secret; no salt. Original problem: The key is derived from participant data plus salt, on demand. If both inputs live in the database, anyone with DB access can re-derive it. Needed: key material from outside the DB (e.g. examiner-held secret or external KMS), and a defined salt location.
- **RESOLVED (S11): Audit log may hold personal data** [S11]. Encrypted fields are audited under the participant key; `participant_id` and points stay after "forget". Original problem: Old/new values of graded fields may contain personal data. Unclear whether audit rows are encrypted or purged on "forget".
- **Unlimited retention conflicts with GDPR storage limitation** [S11]. Even with "forget", indefinite retention of personal records needs a legal check.
- **RESOLVED (ADR 0001, S02): Documents survive "forget" as ciphertext** [S06, S02]. Forget now also deletes the SeaweedFS objects. Original problem: SeaweedFS objects remain stored. Deletion of the object, not only key loss, should be considered.
- **RESOLVED (ADR 0002, S06): SSE-C enforcement unverified** [S06]. SeaweedFS 4.48 is pinned; the spike tests show a read with a wrong or missing key fails and the stored bytes are ciphertext. Original problem: Relies on a current SeaweedFS version and a read-test that is not yet done.
- **RESOLVED (S06, S09, issue 38): Blinding is application-level only** [S06]. Now: hub messages never carry points (strongly typed clients), one visibility function feeds the read DTO, and an integration test checks REST reads and hub messages. Original problem: Safe only if the API never sends the partner's scores. This needs an explicit DTO rule and test in S09.
- **Background consumers have no authorization model** [S09]. Notification runs without examiner context. Rules for which claims or service identity it uses are undefined.
- **Interim and closed-area points editable by any examiner** [S07]. No second check or role split. The audit type "correction" records it but does not prevent it.
- **Hangfire dashboard exposure** [S16]. Access restriction is open.

### Consistency and integrity

- **Frozen grading document vs. later corrections** [S06, S07]. The signed document can diverge from live points. Only the audit log explains the gap. Confirm this is accepted, or define a re-issue process. Documentation grading (issue 40) has no correction path: graded is final until the grading-document slice decides corrections and re-issue together.
- **Dual write: DB commit + RabbitMQ publish** [S09, S11, S12]. Resolved: Rebus.PostgreSql transactional outbox (ADR 0003, S09). Remaining risk: the library calls the outbox experimental (versions pinned).
- **Concurrent edits to the same score** [S03, S06]. "In progress" is a UI marker only. Optimistic concurrency or locking is not defined. Resolved for documentation grading (issue 40): optimistic concurrency on the examiner grading, a stale save gets 409. Application approval still open.
- **Application conflicting decisions** [S03, S15]. Two examiners may decide differently. Resolution rule is open.
- **Examiner role for applications unclear** [S03, S15]. "Two examiners" is not restricted to registered examiners. Confirm who may approve.

### Gaps

- **Single points of failure and recovery** [S16]. Postgres, RabbitMQ, Keycloak: no HA, backup schedule, restore test, or disaster recovery.
- **Dashboard values undefined** [S08, S10]. The dashboard cannot be specified or tested.
- **QuestPDF license unverified** [S06]. Could block the grading document. Fallback not defined.
- **CSV format and versioning** [S02]. Format partly known from a sample (see S02); delimiter, encoding and header layout still unknown. The identification number is not a unique key. No versioning or import error handling defined.
- **RESOLVED (S06): Upload size limit not enforced** [S06]. Now a hard 15 MB limit, configurable, enforced in three places. Original problem: 5 MB was a guideline only.
- **Error handling and user feedback** not defined for import failures, failed uploads, or lost SignalR connections during grading.
- **Offboarding** [S05]. Reassignment is defined for open to-dos, but not for an examiner's historical grades and signature.
- **Testing beyond TDD** [S09]. No integration, end-to-end, or load test scope (exam-room load peaks are expected).

---

## Reading guide

- Sections have fixed IDs (S00–S13). Do not renumber. Cross-references use IDs.
- Find a topic via the Navigation table. Main section in bold, other locations after.
- Markers:
  - `[DECIDED]`: binding.
  - `[PROVISIONAL]`: in effect, changeable.
  - `[CLARIFICATION]`: refines an earlier statement; the clarification wins.
  - `[OPEN]`: undecided. Also listed in S10.
  - `**need check**`: proposed or inferred from the concept, not confirmed. Verify before relying on it.
  - Unmarked statements: requirements or target state.
- Terms: see S00.2 Glossary.
- Participant status values are defined only in S04.

## Navigation

| Topic | Sections |
|---|---|
| Process flow | **S00**, S01 |
| Goals and scope | **S01** |
| Exam cycle | **S02**, S00, S08 |
| CSV import / participant object | **S02**, S04 |
| Application and approval | **S03**, S04 (application status) |
| Text templates | **S03**, S10 |
| Tags and later examiner assignment | **S03**, S05, S10 |
| Project knowledge base (AI, future) | **S06**, S03, S10 |
| Participant status values | **S04**, S06, S07, S08 |
| Points and overall grade | **S04**, S06, S07, S10 |
| Written exams (calculation) | **S04**, S10 |
| Examiner registration | **S05**, S06 |
| User management and roles | **S05**, S10 |
| Project work, split-view grading | **S06** |
| Grading document and signature | **S06**, S05, S10 |
| Oral exam / Virtual Room | **S07**, S09 |
| Real-time / SignalR / "in progress" | **S09**, S03, S06, S07 |
| Dashboards | **S08**, S10 |
| Tech stack and backend | **S09**, S10 |
| Auditability / correlation ID | **S11**, S00, S01, S10 |
| Privacy / GDPR | **S11**, S02, S09, S10 |
| Success criteria | **S01**, S10 |
| Open points | **S10** |
| Notifications / email | **S12** |
| Onboarding / training | **S13** |
| Domain model / entities | **S14** (need check), S02, S04, S05 |
| Permissions / state transitions | **S15** (need check), S03, S05, S06, S07 |
| Infrastructure / deployment / ops | **S16** (need check), S09, S02, S11 |

## Maintenance

- Change the main section, then check all locations listed in the table.
- New section: assign the next free ID and add it to Navigation.
- Decided open point: record the decision in the main section, remove the S10 entry.
- Do not silently resolve contradictions: add them as `[OPEN]` in S10.

---

## S00 Overview

### S00.1 Summary

- Easy is a web platform for collaborative work by IHK examiners.
- Everything is tied to an exam cycle. Two per year: winter and summer final exams.
- Private project of the examiners. Not part of OMEXOM Deutschland or any other company.
- For examiners only, not participants.

### S00.2 Glossary

| Term | Meaning |
|---|---|
| Exam cycle | Winter or summer final exam. Container for participants, approvals, assignments, grades, dashboard. |
| Participant / candidate | One CSV row per cycle. In the Virtual Room, one session = one participant. |
| Application | Approval of the project idea (S03). First grading step, no points. |
| Documentation | Synonyms: project documentation, project work. The graded project work (S06). |
| Written exam | One of three written exams. Point source (S04). |
| Interim exam | Separate point source, independent of the main process (S04). |
| Oral exam | Formerly "practical exam" / virtual exam room (S07). |
| Virtual Room | Dedicated SignalR hub per exam session (S07). |
| Split view | Grading UI: PDF left, comment sections with points right (S06). |
| Correlation ID | ID that traces one operation across services and messages (S11). |
| Encryption key (per participant) | Random data key, wrapped by an external master secret (S02, ADR 0001). Protects that participant's data. |

### S00.3 Process

1. **Create cycle / CSV import**: CSV creates a new cycle. Each row becomes a participant object (S02).
2. **Application**: Participant submits a project idea (PDF, comments, no points). Two examiners approve, reject, or approve with conditions. Text templates and tags (S03).
3. **Registration**: Two examiners register for the participant's exam (S05).
4. **Documentation**: PDF is downloaded externally from the IHK and uploaded to Easy. Two examiners grade live in split view (S06).
5. **Grading document**: Generate the fixed template, sign digitally, upload to the IHK externally (S06).
6. **Oral exam**: Virtual Room per participant, with chat and criteria-based grading (S07).
7. **Overall grade**: All areas except application contribute points (S04).

### S00.4 Cross-cutting

- **Status**: each participant has one continuous status across four areas (S04).
- **Real-time**: SignalR. Rows being edited are marked live as "in progress".
- **User management**: lightweight, login and roles only (S05).
- **Dashboards**: cycle dashboard (high priority), statistics dashboard (low priority) (S08).
- **Tech stack**: .NET 10, ASP.NET-hosted Blazor WebAssembly (not Blazor Server), SignalR (S09).
- **Auditability**: every operation traceable via correlation ID, especially grading (S11).

---

## S01 Goals and scope

### Vision

Easy replaces distributed manual coordination between IHK examiners with one shared platform. Examiners work live per cycle: from project idea approval to overall grade.

### Main goals

1. **Cycle management**: create a cycle from CSV with all participants.
2. **Structured approval**: two examiners approve, reject, or approve with conditions. Supported by templates, comments, tags.
3. **Real-time collaborative grading**: two examiners grade project work in split view without blocking each other (SignalR, edit markers).
4. **Digital oral exam**: Virtual Room with chat and criteria-based grading. Only the grade is stored.
5. **End-to-end traceability**: one status per participant across all phases; overall grade from individual points.
6. **Overview and control**: cycle dashboard with percent progress and overdue items.
7. **Final documents**: grading document in template format, signed, submitted to IHK externally.
8. **Auditability**: every operation traceable via correlation ID, especially grading.

### Out of scope

- Participants submit project work in a separate IHK environment. Download from there is outside the platform.
- Upload of the signed document to the IHK is external.
- Returning the application to the IHK is manual (no interface, no export).

### Success criteria

- `[OPEN]` Not defined. Proposal: agree on what Easy is measured by in the first cycle (S10).

---

## S02 Cycle and CSV import

### Exam cycle

- Easy always runs within an exam cycle. Two per year: winter and summer final exam.
- Participants, approvals, registrations, grades, and the cycle dashboard each belong to one cycle.
- Every module has a due date and a fixed deadline, set separately (e.g. application processing and grading).
- All dates are maintained manually. No automatic calculation relative to other events.
- Participants per cycle: 20–120, trend rising.
- Active examiners in pool: currently 10–15.
- Examiners switch cycles via UI selection, no separate registration. Registering anywhere in a cycle (e.g. an assignment) makes them active in that cycle.

### CSV import (first module)

- Accepts a prepared CSV. Import creates a new cycle. Each row becomes a participant object.
- CSV contains only: participant name, project title, identification number. Technology tags and project details are not imported; examiners add them later manually.
- Lightweight validation. Scope not yet specified.
- CSV format is specified by the IHK. A hand-made sample (`docs/samples/antragimport/`) exists; the real delimiter, encoding and header layout are still unknown. Validation and import may need adjustment once they are.
- `[CLARIFICATION]` The sample suggests the IHK data may carry more than name, title and ID. IHK columns: participant name ("Vorname Nachname"), identification number (`Prüfl.-Nr.`, integer), training provider (`/ Backhaus IT` marks a cooperating company), a status column with the same text in every row (ignored), and project title. To confirm against the real CSV.
- `[CLARIFICATION]` The identification number is **not a unique key**. It relates to the participant and can be issued again in later years, so it is unique at best within one cycle and must not be used to match participants across cycles. Whether it is unique within a cycle, and whether one person can appear with two numbers, is `[OPEN]` (S10).
- Columns for language and review comment in the sample were added by an examiner after reading the PDF. They are not part of the IHK file.
- `[DECIDED]` Skeleton parsing (the real layout is still unknown, so all settings are configurable): CsvHelper, UTF-8 (BOM accepted), delimiter `;` by default. A header row is required; columns are matched case-insensitively by name. Required: name, identification number, project title. Other columns (provider, status) are ignored. The cycle's type and year come from the import form, not the file. Import is admin-only (S05). The original file is not stored, only a result summary.
- `[DECIDED]` Validation is all-or-nothing: required fields non-empty, ID is an integer, no duplicate IDs inside the file, at most 500 rows and 1 MB. Any error means nothing is imported; the job ends as failed with row errors (row number, column, message) shown to the admin, who uploads a corrected file. A valid file creates the cycle and all participants in one transaction with the outbox event. The import job has an ID, so a redelivered message does not create a second cycle.
- Import runs asynchronously via RabbitMQ (Rebus), not synchronously in the request.
- `[DECIDED]` A cycle is unique per type and year. An import for an existing cycle is rejected. Participants missing from an import are added manually to the existing cycle, not by a second import.
- `[OPEN]` Retention of the encrypted CSV in the Rebus error queue after a failed import (S10, issue 28).
- Live progress via SignalR: nice-to-have. At max ~120 rows, long waits are not expected unless AI tooling is added to the import.

### Participant ID

- In addition to the CSV identification number, Easy assigns an internal ID:
  - Primary key: GUID.
  - Display: sequential, examiner-readable number per cycle, e.g. `WS2026-014` (type, year, 3-digit counter). Assigned at import in file order, unique per cycle, gaps are fine.
- `[DECIDED]` The identification number is encrypted (S02 encryption) and not unique across years. Uniqueness within a cycle is a unique index on `(cycle_id, csv_id_hash)`, where the hash is an HMAC-SHA256 blind index over the ID, keyed from the master secret. "Forget" sets the hash to null. Participants are never matched across cycles. The same name with different numbers is accepted as two participants.

### Encryption and "forgetting"

- `[CLARIFICATION]` `[DECIDED]` Envelope encryption (ADR 0001): each participant has a random data key, wrapped by a master secret that lives outside the database (Docker secret file, `Easy.MainApi` only; KMS later behind an interface). Only the wrapped key and its master-secret version are stored. This replaces the earlier "derived from participant information plus salt". Encrypted: name, project title, CSV id number, comments, documents. Readable: points, status, display number. Threat model: database-only leak. See S11.
- "Forget" function: deletes a participant's wrapped key and their SeaweedFS objects. Encrypted backups are kept about 30 days, so forget is reversible from a backup until they expire.
  - Manual action, admins only.
  - No automatic trigger.
  - Safeguard: simple confirmation dialog. No four-eyes principle.
- The participant record remains for statistics. Only personal information becomes unreadable.
- The participant object holds the point values of the exam sections (S04).

---

## S03 Application and approval

### Application

- Participant submits a project idea.
- PDF upload, similar to documentation (S06).
- PDF can be commented. No scored segments.

### Approval

- First grading step per participant.
- Two examiners approve, reject, or approve with conditions. Each can add notes or a rejection.
- Not strictly limited to two fixed examiners: comments may be collected collaboratively from several.
- At the end, the final application status must be set cleanly so the application can be returned to the IHK (manually).
- Application statuses: submitted, rejected, approved, approved with conditions.
- No automatic push notifications or pop-ups for comments or approval activity. Examiners check themselves. Also applies to application PDF upload (unlike project work upload, S06).

### Text templates

- Fixed templates for rejection and conditions.
- For approval, text blocks can be selected with clicks.
- `[DECIDED]` Blocks are predefined and extensible by examiners. New blocks are stored permanently in the database. Each block belongs to exactly one category: rejection or condition.

### Tags

- Examiners assign tags to participant rows, e.g. technology of project work (C#, Docker, SQL).
- Initially filter-only.
- Later basis for competence- or preference-based examiner assignment (S05).
- Future topic, not in MVP: a background service could read the application PDF with AI and suggest tags (e.g. JavaScript, React, MS SQL). Related: project knowledge base (S06).

### Real-time

- Approval runs over SignalR. A row being edited (note, rejection) is marked live as "in progress".

---

## S04 Status and points

### Participant status (single source)

One status per participant, split into four areas:

| Area | Possible values |
|---|---|
| Application | submitted, rejected, approved, approved with conditions |
| Written exam | attended, failed, passed (mathematically from three point grades; logic open, S10) |
| Documentation | submitted, in progress, graded |
| Oral exam | attended, failed, passed |

### Points

- All areas except application have points. They feed the overall grade.
- Role of each area:

| Area | Type |
|---|---|
| Application | active collaboration / approval, no points |
| Written exam | only point values added, no active grading in the portal |
| Documentation | active grading in portal, split view (S06) |
| Oral exam | active grading across sessions in the Virtual Room (S07) |

- Point sources (fixed terminology):
  - written exam
  - documentation
  - oral exam
  - interim exam: separate point source, independent of the main process. Values are available only on the oral exam date and are entered in the session.
- Documentation points come from S06, oral exam points from S07.
- Overall grade: written-exam logic open; weighting of all sources open (S10).
- Overall grade is not stored. It is computed live from individual values when needed (S09).

---

## S05 Examiner registration and user management

### Registration for the exam

- After approval, two examiners register for the actual exam of the participant.
- Initially free choice, no tag restriction. Later possibly assignment by competence or preference.
- These two examiners grade the documentation together (S06).
- Examiner change during a running cycle only before grading has started.
  - `[DECIDED]` An unfinished grading by the previous examiner is discarded.
  - No grades are handed over. The new examiner starts from their own state.
- `[DECIDED]` Registration and "began grading" (issue 35):
  - Any active examiner (admins included) registers for any participant from the import on, first come first served, at most two per participant. Registrations (names only) are visible to all examiners.
  - "Began grading" = the examiner submitted their examiner grading. Before that, an examiner may unregister; deactivation releases the registration the same way. The draft is discarded and the place reopens for free registration. After submit the grading stays and counts, also after deactivation.
  - The third examiner (S06) is not a registration and does not count toward the two: an invitation (invited, accepted, declined) to an examiner other than the registered pair. They may step back before submit, discarding their draft.
  - No admin assignment of free places in this slice; the admin overview of open to-dos comes later.
- `[DECIDED]` Deactivation in the documentation-grading slice (issue 40): admin-only API to deactivate and reactivate an examiner, no UI. Deactivating releases the examiner's unsubmitted registrations and third-examiner invitations as described above.

### User management (lightweight)

- Initially only login and roles. More features later.
- Two roles: admin and examiner. Admins are always also examiners.
- Only admins create new examiners manually. No self-service.
- Admin rights: full administration and starting a cycle. Admins do not intervene in running grading.
- Every examiner can access every cycle. Admins can deactivate an examiner profile.
- Deactivation with open to-dos (registrations, grading) requires reassignment.
  - Open to-dos of deactivated profiles are made visible to admins (issue or overview).
  - This overview is a separate admin area, not part of the cycle dashboard.

### Authentication

- `[DECIDED]` Keycloak (26.x) for OIDC and multi-factor authentication. Initially Easy only. No existing central identity system. Keycloak becomes the first central identity system, possibly reusable by other projects later.
- `[DECIDED]` Realm `easy`, committed as `deploy/keycloak/easy-realm.json` (dev realm: seeded `admin` and `examiner` users, MFA off, no secrets):
  - Frontend (Blazor WebAssembly): public client `easy-web`, authorization code with PKCE S256. Login runs in the browser directly against Keycloak, regardless of which server delivers the frontend files.
  - `easy-api`: resource-server client; every access token carries audience `easy-api`.
  - Notification and scheduler: confidential service-account clients `easy-notification`, `easy-scheduler` (secrets from `.env`). MainApi and ExamRoom are pure resource servers without a client.
  - Realm roles `examiner` and `admin` (composite including `examiner`), emitted as a flat `roles` claim.
  - Access token 5 minutes, SSO session 30 minutes idle and 10 hours maximum.
- Token validation in every service via the same realm (S09).
- Hangfire is the scheduler for the notification service (S12).
- Idea: store a digital signature in the examiner profile (S06).

---

## S06 Documentation / project work: grading

### Submission and upload

- PDF submission happens in a separate IHK environment. Download is outside the platform.
- Easy needs an upload function for the externally downloaded PDF.
- `[CLARIFICATION]` `[DECIDED]` Hard limit: 15 MB by default, configurable through an environment variable. PDF only (magic-bytes check). Enforced at the YARP upload route, Kestrel and MainApi, all reading the same setting. Uploads go through MainApi (proxy), which applies SSE-C with the unwrapped participant key.
- `[DECIDED]` Every role may upload documents (examiners and admins; admins hold the examiner role). Service accounts may not.
- `[OPEN]` Re-upload: which re-upload is meant (application vs documentation, before vs after grading) and its effect on the status (S10, issue 29).
  - `[DECIDED]` Documentation re-upload (issue 40): allowed while the documentation status is submitted. It adds a new `Document`, and the split view shows the latest one. Once in progress or graded, MainApi rejects the upload with 409. Application PDF and history stay open in issue 29.
- Storage: S3-compatible object storage, decided on SeaweedFS (Apache-2.0, lightweight, Docker-friendly).
  - Objects encrypted with the participant-specific key (SSE-C). No separate key.
  - Deletion concept (S11): unreadable by key deletion.
  - SeaweedFS 4.48 pinned; SSE-C enforcement on read is covered by a test, re-run on every upgrade (ADR 0002).
- Successful upload automatically emits an event to the notification service. Registered examiners are notified (S12).
- Process waits for this submission after approval.

### Project knowledge base (future, not in MVP)

- Goal: a knowledge base of past projects, to cross-check whether a topic was present in an earlier cycle.
- Idea: a separate service reads the content of each uploaded PDF with AI. A guardrail prompt drops all GDPR-relevant information.
- Reads documents only through the upload event (S12) and MainApi, the only reader of documents.
- `[OPEN]` Open points: a prompt is not a reliable control, so redaction before the text reaches the model; hosting of the model (an external provider processes participant data); what may persist after "forget" (S02), so only truly anonymous content (S10).

### Collaborative grading

- The two registered examiners grade together, live via SignalR.
- Direct grading in the PDF viewer is too complex. Instead, split view: PDF left, predefined comment sections right.
- `[DECIDED]` Split-view layout (issue 41, prototype on branch `prototype/grading-split-view`, variant B):
  - Right pane shows one section at a time, as five tabs (number, short name, own section points, a check mark when all its criteria are complete).
  - Each criterion is a card: point buttons 0 to max (no number field), the remark below, and the other gradings' points and remarks once visible (summary view, graded, admin).
  - Comments sit under the section they are pinned to; general comments are counted below.
  - A bottom bar carries the examiner statuses, the own total, submit / withdraw and "open summary view". Deviation, "summary view opened" and graded notices sit above the tabs.
  - Page sync is a jump only: the examiner sets a PDF page per section, and switching tabs (or "→ S.") jumps the PDF there via `#page=N`. The page anchor belongs to the examiner's grading (plain, not blinded, not audited). Nothing follows the reader's scroll position, so the native browser viewer suffices and pdf.js is not needed (issue 36).
- `[DECIDED]` The sections come from the IHK grading matrix for the documentation (Fachinformatiker Anwendungsentwicklung only for now): 5 weighted sections, 13 criteria with integer points and a remark each, total out of 100. Source: `docs/samples/bewertungsmatrix/` (issue 33). The matrix covers only the documentation, no other exam step.
  - `[CLARIFICATION]` The matrix has 14 criteria (2 + 2 + 3 + 5 + 2), not 13; "13" in this section was a miscount (found in issue 41).
- `[DECIDED]` Section weights default to 25 % / 10 % for sections 4 and 5 (a 20 % / 15 % variant exists) and should be adjustable by an admin in the frontend. How is decided in issue 37.
- `[DECIDED]` Sections and points model (issue 37):
  - The grading matrix is fixed in code (sections, criteria, max points). Criterion keys carry a matrix version (e.g. `fiae-2026:3.2`); stored points refer to them. A new IHK matrix is a code change (ADR 0004).
  - Weights live in the configuration table and are fixed per cycle: the admin form edits the default for new cycles (15/10/40/25/10) and the weights of a cycle without any submitted examiner grading. The five weights add up to 100 %.
  - Points are integers from 0 to the criterion's max. Drafts may be incomplete; submit needs points and a remark of at least 10 characters (a constant) on all 14 criteria.
  - Comments: one open thread per documentation, optionally on a section, by any examiner including the registered pair. No points, so not blinded. Replaces the S14 `area` and `counted` columns.
  - Remark and comment text are encrypted with the participant key (S02); points stay plain. Criterion points and remarks are `[Audited]` (S11); comments are not.
- Both examiners grade in this module. The exam is scored here.
- Other, unregistered examiners may comment. These comments do not count toward the grade.
- Blinding: by default, the two examiners' grades are hidden from each other.
  - Applies only to documentation, not to application (where it is transparent).
  - When needed, e.g. during a phone call, an examiner starts a summary view showing both grades.
  - Implemented purely at application level: other examiner's data is not sent. No separate tables per examiner.
- `[DECIDED]` Live grading and visibility (issue 38):
  - Live events on the MainApi hub (S09), none carrying points: examiner grading status (draft, submitted, withdrawn), summary view opened, comment added, deviation notice, third examiner invited/accepted/declined, graded, registration taken or released. No presence or typing marker.
  - Summary view: a registered or the third examiner opens it alone, any time before graded, no consent. It shows the other current examiner gradings, drafts included, and unblinds only the viewer. Opening is audited (S11) and the others get the live event. While open it refetches on each change event.
  - Visibility of points and remarks:
    - Registered or third examiner: own grading always; the others' after opening the summary view or once graded.
    - Admin not grading this documentation: all examiner gradings at any time, drafts included, read-only, reads not audited. An admin who is a registered or third examiner is blinded like anyone else.
    - Other examiners: status and comments only.
  - One visibility function decides this and feeds the grading read DTO (ADR 0005). Test: examiner A saves draft points; B's REST read and every hub message B received contain no A points; after B opens the summary view the read contains them.
- Deviation: if the calculated grades differ too much, the system shows a notice. A third examiner as arbiter should be considered.
  - Notice only, no automatic invitation. Decision stays with the examiners.
  - `[CLARIFICATION]` The third examiner is a flow, not only a consideration: see the decision on two examiner gradings below (issue 34).
  - Threshold: fixed in the database configuration table, not configurable per cycle.
  - Configuration table: central key-value persistence for system settings. Normal table in the same Postgres DB, accessed via EF Core (no separate configuration service).
- `[DECIDED]` From two examiner gradings to one documentation score (issue 34):
  - Always two registered examiners; no single-examiner grading.
  - Each examiner submits their own examiner grading. Until both have submitted, both may still change theirs (e.g. after a summary-view call).
  - On the second submit, gap within the threshold: status graded, documentation score = `ROUND((T1 + T2) / 2, 1)`.
  - Gap above the threshold: status stays in progress with the deviation notice. Either examiner may call a third examiner (invited by name, may decline); finishing without one needs both. Below the threshold no third examiner can be called.
  - The third examiner starts blind and may open the summary view. Their submit sets graded; documentation score = `ROUND((T1 + T2 + T3) / 3, 1)`, no further deviation check.
  - Rounding half away from zero (as Excel `ROUND`): 47.5 and 45.0 give 46.3.
  - The documentation score is stored in `Grade` (source documentation) when graded, so later weight changes do not alter it. The German grade 1 to 6 is derived for display only (scale in `docs/samples/bewertungsmatrix/`).
- `[DECIDED]` Deviation rule, notice and configuration table (issue 39). `[CLARIFICATION]` "Gap above the threshold" above means this rule fires:
  - Checked on "both submitted" between the two registered examiner gradings. Deviation if any section's points (0 to 10) differ by more than 4 (so 5 or more), or the totals (out of 100) differ by more than 10. Exactly 4 or exactly 10 is no deviation.
  - Stored at the check: status deviation, with the deviating sections (and "total") and the thresholds used. A later threshold edit only affects future checks.
  - Resolving: until a third examiner is invited, either examiner may withdraw their submit, change points and submit again; withdrawing clears the deviation and the next "both submitted" reruns the check. Inviting a third examiner freezes both gradings.
  - Notice (live event, S09): registered examiners and the third examiner see the names of the deviating sections and/or "total", never gaps or direction (with one's own points that would reveal the other's); the numbers come only through the summary view. Admins not grading see the gaps. Other examiners see the status deviation only.
  - Configuration table `ConfigEntry(key text PK, value text)` holds system-wide settings only: `grading.deviation.section-points` (4), `grading.deviation.total-points` (10), `grading.default-weights` (15/10/40/25/10). Keys are code constants, values parsed in code, defaults seeded by migration, edited by admins in the settings form, `[Audited]` (S11). Per-cycle weights are columns on `Cycle`, copied from the default when the cycle is created.
- Status: submitted, in progress, graded. "Graded" locks the grading.
- `[DECIDED]` Documentation grading slice (issue 40):
  - In progress = the first examiner grading submitted. Graded is final: no unlock or correction in this slice (corrections come with the grading-document slice, S11 `correction`).
  - Concurrent saves of one examiner grading (two tabs or devices): optimistic concurrency, the stale save gets 409 and the client refetches.
  - Grading events are live hub events only, no mail. The documentation upload mail goes to the registered examiners, or to all active examiners if nobody is registered.
  - Finishing without a third examiner: on a deviation, each registered examiner may accept it. When both have accepted, status graded with `ROUND((T1 + T2) / 2, 1)`. Withdrawing a submit clears both acceptances; once a third examiner is invited, accepting is not possible.
  - A declined invitation, or a third examiner stepping back, lifts the freeze: back to a plain deviation (invite again, accept or revise).
  - Graded makes the whole documentation grading read-only, comments included.
  - German grade 1 to 6 from the score by lower bounds: >= 92 → 1, >= 81 → 2, >= 67 → 3, >= 50 → 4, >= 30 → 5, else 6 (91.5 → 2).
- Not a real session like the Virtual Room. Locking depends only on status "graded".

### Grading document

- `[DECIDED]` The grading document uses a fixed template format.
  - Generated once and stored as a file in SeaweedFS in the participant's area. Not regenerated on each download.
  - Document state at signing time stays frozen. Later point corrections do not change it. Traceable only via the audit log (S11).
  - Applies only to documentation. The overall grade is not produced as a document (S09).
- Digital signature: the document should be signable.
- Planned: QuestPDF. License: free for individuals, non-profits, FOSS, and organizations under 1 M USD annual revenue. Public bodies and listed companies excluded. Easy is a private project, so likely free. Final verification open (S10).
- Layout includes a prepared signature area if achievable without large effort.
- Afterwards uploaded to the IHK externally.

### Open: signature

- `[OPEN]` Sign in the platform or externally after download (S10).
- Proposal: signature drawn once as a simple canvas (mouse/touch) and stored in the examiner profile. No image upload. Then applied automatically to each grading document.

---

## S07 Oral exam (Virtual Room)

- Last module. Four examiners with laptops sit together in one room and start an exam session.
- Virtual Room = dedicated SignalR hub.
- Each examiner logs in with their own account. No shared access.
- **One session = exactly one participant.**
- Number of participants not fixed. Any examiner can join any session via "Join Session".
- Each session has three fixed main examiners who do the main grading.
  - Differs from documentation (two examiners, S06).
  - Assignment independent of documentation registration.
- Non-main examiners can chat and comment. They may also submit their own virtual grade. Shown if applicable, but not included in the overall grade.
- No blinding: everything is visible to everyone present from the start.
- Chat: text only. No video or audio.
- Grading based on predefined notes and criteria.
- Interim exam points: a field in the session. Any examiner present can enter them, not only main examiners.
- All point-relevant participant fields are visible and editable in the session, including values of closed areas (e.g. written exam), e.g. for later IHK corrections.
- At the end, only the grade and grading comments are stored permanently. Chat history is not stored.
  - `[OPEN]` Format and criteria of grading comments (S10).
- Status: attended, failed, passed. Points feed the overall grade.
- Closed session is final.
- `[DECIDED]` Reopening by an admin is not intended.
- Closing a session triggers no notification.
- Nice-to-have (not core): admins pre-create "predefined sessions" (session shell for a day).

---

## S08 Dashboards

### Cycle dashboard (high priority)

- All values as percent, per cycle.
- Purpose: overview of the cycle and visibility of whether examiners must step in to finish.
- Per-examiner values needed.
- Visible to every examiner, not only admins.
- Cycle selection via dropdown, including past cycles.
- Additional filtered view: which participants are overdue (based on due dates and deadlines per module).
- Graphical: e.g. pie charts, progress bars.
- `[OPEN]` Exact values (S10).
- Live updates via SignalR (dashboard hub). Main API pushes on relevant status change. No reload, no polling.
- Data source: participant status (S04).

### Statistics dashboard (low priority)

- Statistics across multiple cycles.
- `[DECIDED]` Comparison across cycles and years is possible.
- No export (Excel, CSV). Dashboard is the only access point.
- No live updates. Computed on page load or via refresh button.
- Computed at runtime via plain SQL aggregates. No caching or precomputed tables (data volume too small).

---

## S09 Tech stack and backend

### Frontend

- ASP.NET-hosted Blazor WebAssembly. **Not** Blazor Server.
- Hosted by the main API service (no separate static container).
- Communicates via REST and SignalR with the backend.
- Responsive, usable on tablet (e.g. exam, split view). No native app.
- UI in German only.

### Real-time (SignalR)

- Live changes during parallel work by several examiners.
- "In progress" marker for rows being edited.
- Dedicated hub per Virtual Room (S07).
- `[DECIDED]` Every hub is strongly typed (`Hub<IXxxClient>`), no string `SendAsync` (issue 38). ExamRoom moves over with the first grading spec.
- `[DECIDED]` Grading hub (issue 38): MainApi `/hubs/grading`, routed through YARP, one instance without backplane. Notify then refetch: messages carry only ids, enums and display names; the client re-reads through REST, which applies blinding, and refetches after a reconnect. One group per documentation `documentation-{participantId}`, joinable by any examiner. List screens are not live in this slice.
- `[DECIDED]` Skeleton join path: MainApi creates an `OralSession` through a REST call by any examiner. ExamRoom only reads the table to check that the session exists and is open. Hub method `JoinSession(sessionId)` adds the connection to the SignalR group `session-{id}` and broadcasts `ExaminerJoined` with the display name from the token. Closed or unknown sessions reject the join. Presence is not stored. One instance, no backplane yet. Any authenticated user with the `examiner` role (admins include it) may join any open session; counted-scoring permissions (main examiners only) come with the grading feature slice.
- Container split:
  - Main API service: general live updates (documentation, "in progress").
  - Exam room service: Virtual Rooms. Separate, independently scalable container (load only during exam season).

### Backend

- Modular, separate services, not a monolith.
- In front: YARP API gateway. Bundles traffic, single address for the frontend, optional coarse check for a token's presence. SignalR/WebSocket routed through it as well.
- `[DECIDED]` Token validation (signature, claims, roles) happens in each service (zero trust). Not centralized in the gateway.
- Cycles are not microservices.
- Candidates for separate services: notification service, live chat (SignalR).
- Inter-service communication via message queue, not REST calls.
  - `[DECIDED]` Rebus as messaging library on RabbitMQ. Single messaging layer for the whole system (including Hangfire).
  - MassTransit excluded (commercially licensed from v9).
- Messages carry the examiner's context and claims, so permissions survive the broker.
  - Not uniform: notification service sometimes runs without examiner context.
- Correlation ID: transported in RabbitMQ headers, not in body (S11).

### Database

- One shared relational database for all cycles. No schema per cycle.
- PostgreSQL with EF Core. Skeleton schema (S14 subset) is `EasyDb` in `Easy.Data`, snake_case names, EF migrations applied by MainApi at start-up. The Rebus outbox table `rebus_outbox` is created by Rebus itself. Feature slices extend the model.
- `[DECIDED]` Write requests in MainApi (`UseOutbox` middleware in `Platform`) run in one EF transaction; events published during the request go to the outbox on the same connection and commit with it (ADR 0003). Consumers call `FirstDelivery(eventId)`, which records the ID in `processed_events` inside the handler's transaction.
- Point values from all sources (written, documentation, oral, interim) in one shared table with a type column, not separate tables.
  - Only the current value per source. No history in this table.
  - History is handled exclusively by the audit log (S11).
- Overall grade is not persisted. Computed live when needed.
  - Relevant only for the final exam / final discussion.
  - No own PDF. Entered manually on a paper form for the IHK.

### Hosting and operations

- Hosting-independent design (own infrastructure or cloud).
- First approach: bitfabrik.it, orchestrated via Docker Compose in one shared definition. No Kubernetes.
- Hangfire runs as its own container, uses the same Postgres DB.
- Test-driven development (TDD) with unit tests. CI/CD pipeline deferred.
- Prepare OpenAPI/Swagger, low priority.
- Rate limiting: not relevant at 10–15 examiners.

---

## S10 Open points

- Calculation logic for the three written exams.
- Weighting of point sources in the overall grade.
- Signature: in platform or external (S06).
- Cycle dashboard values, incl. per-examiner values (S08).
- Later competence-based examiner assignment via tags (S03, S05).
- Further user management features (S05).
- Success criteria for Easy (S01).
- Retention of the encrypted import CSV in the Rebus error queue (S02, issue 28).
- Re-upload of documents and its effect on the status (S06, issue 29); documentation re-upload decided in issue 40.
- CSV: uniqueness of the identification number within a cycle, and whether one person can appear with two numbers (S02).
- Auditability: scope of logged operations (S11).
- GDPR: concrete requirements for data storage and audit log (S11).
- Format and criteria of oral-exam grading comments (S07).
- Verify QuestPDF license (S06).
- Project knowledge base: AI reading of PDFs, GDPR guardrail, model hosting, persistence after "forget" (S06).

---

## S11 Auditability and GDPR

### Audit log

- The system must be auditable.
- `[DECIDED]` Separate table in the same Postgres DB. No Elasticsearch or Loki. Reason: joins with correlation ID, status, points, and grading documents; transactional consistency with business changes.
- `[DECIDED]` Central, reusable audit component: an own EF Core `SaveChangesInterceptor` in `Platform`, no library. Opt-in `[Audited]` attribute on properties. `actor_id` is the token `sub` (service accounts included), `correlation_id` comes from the ambient context (`CorrelationId.Current`, set by the HTTP middleware and the Rebus incoming step). Writes synchronously in the same transaction as the business change. No separate audit service: it would either be a thin wrapper or, with async messaging, lose transactional safety.
- `[DECIDED]` Audit log visible to admins only.
- Logged: every value change on grading-relevant data. Not every keystroke.
- Audited so far: the `Grade` value, settings (`ConfigEntry`), and per examiner grading the criterion points and remarks (spec 13), always type `initial`. The `correction` logic needs the "closed area" state and comes with the grading feature slice.
- Discarded drafts (unregister, deactivation) are deleted in bulk and leave no audit rows; marked `ponytail:` in code. Logging them is part of the open scope question below.
- Append-only: no update or delete path in code, covered by a test. DB-level protection (insert-only role) is not decided.
- Personal data: audit rows for readable fields store plain old/new values and keep `participant_id`. Rows for encrypted fields (e.g. comments) store old/new encrypted under the participant data key (S02), so they are unreadable after "forget" while the row survives. `participant_id` and point values stay after "forget"; the legal check is open.
- Entries carry a type (e.g. "correction"). Late changes to closed areas are distinguishable from initial entries.
- Retention: unlimited, as long as the participant is not "forgotten" (S02). No fixed deletion period.
- `[OPEN]` Scope of logged operations (S10).

### Correlation ID

- Every operation traceable via correlation ID. Especially grading.
- The ID survives the whole asynchronous flow across service boundaries. Example: an action in the main API emits an event via RabbitMQ, processed by the notification service.
- Transport via RabbitMQ message headers (metadata or tracing), not the body.
- Linked to status, points, and grading documents.

### GDPR

- Relevant for data storage and the audit log.
- Encryption via participant-specific key (S02). Deletion by wrapped-key and object deletion (S02).
- Encrypted: name, project title, CSV id number, comments, documents. Readable: points, status, display number (S02, ADR 0001). Audit rules above.
- `[OPEN]` Findability of encrypted data (search over names or titles) is not designed (S10).

---

## S12 Notifications

- Email service informs examiners about new items or actions. Also reminders for due dates.
- Examiners only, never participants.
- Sent via own Mailcow server on bitfabrik.it. No external provider.
- SMTP configuration via `.env` and Docker environment variables (`Smtp__*`). Dev uses Mailpit (S16).
- Possibly in-app notifications later, in addition to email.
- Asynchronous and decoupled from user interaction. No synchronous trigger on comments.

### Eventing

- Action-based events (e.g. application approval, session end, documentation upload) are published by services to RabbitMQ. The notification service consumes them.
- Time-based reminders (due dates): Hangfire periodically checks deadlines and publishes the event itself. Hangfire uses Rebus (S09), job persistence in Postgres.
- `[DECIDED]` Skeleton reminder: a Hangfire recurring job runs daily (cron configurable, default 07:00). It finds cycles whose documentation due date is within 3 days (configurable) and publishes `DeadlineApproaching(cycleId, module, dueDate, daysLeft)` directly, without the outbox. Event ID `deadline:{cycleId}:{module}:{dueDate}:{daysLeft}`, so Hangfire retries and re-runs do not send duplicate mails. The notification service mails all active examiners (recipient rules come with the feature slices; documentation upload: S06, issue 40) one plain-text German mail each. Addresses come from the `Examiner` mirror (S14). The Hangfire dashboard is disabled in the skeleton.
- Events carry IDs and minimal data, never personal data. Consumers are idempotent: processed event IDs are recorded and repeats skipped.
- Application PDF upload triggers no notification (S03). Documentation upload does (S06).

---

## S13 Onboarding

- No onboarding area in Easy planned.
- Onboarding of new examiners happens as training outside the platform, based on jointly developed documentation.

---

## S14 Domain model **need check**

Proposed entities, derived from S02–S12. Not a final schema.

| Entity | Key fields | Relations / notes |
|---|---|---|
| Cycle | id, type (winter/summer), year, per-module due date and deadline | 1–n Participant. Skeleton: two nullable columns for the documentation module only (due date, deadline). Five section weights, copied from the default on create (S06, issue 39) |
| Participant | id (GUID), cycle_id, display_no (e.g. `WS2026-014`), csv_id (encrypted), csv_id_hash (blind index, null after forget), name (encrypted), project_title (encrypted), application_status, written_status, documentation_status, oral_status (four nullable enums, S04), key_forgotten flag | n–m Tag; 1–n Registration; 1–n Grade; 1–n Document; 1–n Comment |
| Examiner | id, external subject (Keycloak), role (admin/examiner), active flag, display name and email (from token claims at first login; examiner data, not encrypted) | 1–n Signature (optional) |
| Registration | participant_id, examiner_id | Documentation pair (2 examiners) |
| Grade | participant_id, source (written/documentation/oral/interim), value, updated_by, updated_at | Current value only, no history (S09). `source` enum, unique on `(participant_id, source)`, `value` numeric(5,1) non-negative; maximum per source not enforced in the skeleton |
| DocumentationScore | participant_id, examiner_id, section, points | Per-examiner; hidden from partner until summary view (S06) |
| Comment | id, participant_id, section_key (optional), examiner_id (the author), text (encrypted), created_at | Open discussion on a documentation, never counted (S06, issue 37) |
| OralSession | id, participant_id, status (open/closed) | 3 main examiners; joined examiners; closed = final. `[DECIDED]` One session per participant (so per cycle), unique on `participant_id`; a second create is rejected. Skeleton: only these three fields, created by MainApi, read by ExamRoom |
| OralGrade | session_id, examiner_id, criteria values, counted flag | Counted only for main examiners |
| TextTemplate | id, category (rejection/condition), text, created_by | Fixed plus examiner-added (S03) |
| Tag | id, name | Filter now, assignment later (S03, S05) |
| Document | participant_id, kind (application PDF / project PDF / grading document), storage key, hash, frozen flag | Stored in SeaweedFS (S06) |
| Signature | examiner_id, drawn image | Stored once in profile (S06) |
| ConfigEntry | key (PK), value (text) | System-wide settings only: deviation thresholds, default weights (S06, issue 39). Audited |
| AuditEntry | id, correlation_id, actor_id, participant_id, entity, entity_key (primary key without participant_id, e.g. examiner/criterion), field, old, new, type (initial/correction), timestamp | Admin-only read (S11) |

Derived, not stored: overall grade (S04, S09).

Skeleton subset (decided): Cycle, Participant, Examiner (mirror of the Keycloak subject, created on first login), OralSession, Grade, AuditEntry, Document (participant id, generated object key, size, uploaded at), the Rebus outbox, and a processed-events table. Import sets application status `submitted`; the first upload sets documentation status `submitted`; written and oral stay null.

---

## S15 Permissions and state transitions **need check**

### State transitions (proposed)

- **Application**: submitted → approved | approved with conditions | rejected.
  - Set by two examiner decisions. Rule for conflicting decisions: open.
- **Documentation**: submitted (upload) → in progress → graded. Graded is locked.
  - Reassignment allowed only before "in progress". Trigger for "begun" (first point entry?): open.
  - `[CLARIFICATION]` Documentation goes in progress on the first examiner grading submitted (issue 40).
  - `[CLARIFICATION]` Per examiner, not per documentation: an examiner can be changed until they submit their examiner grading (S05, issue 35).
- **Written exam**: attended / failed / passed derived from three point grades. Formula open (S10).
- **Oral session**: open → closed. Closed is final. No reopen (S07) One session per participant (S14).

### Permission matrix (proposed)

| Action | Admin | Examiner |
|---|---|---|
| Create cycle / CSV import | yes | no |
| Create / deactivate examiner | yes | no |
| Start cycle | yes | no |
| Register for a participant | yes | yes |
| Approve / reject application | no (open) | yes, if involved (open) |
| Score documentation | no | registered pair only |
| Comment on documentation | no | any examiner (uncounted) |
| View partner's documentation scores | no | no; summary view by registered pair (open) |
| Join oral session | no | any examiner |
| Score oral (counted) | no | main examiners only |
| Enter interim points | no | any examiner in session |
| Edit closed-area points in session | yes | yes (IHK correction) (open) |
| Forget participant key | yes | no |
| View audit log | yes | no |
| Intervene in running grading | no | no |

---

## S16 Infrastructure and operations **need check**

### Containers (Docker Compose, one definition)

| Container | Role | Exposure |
|---|---|---|
| gateway (YARP) | Single entry point, coarse token check, REST and SignalR routing | public |
| main-api | REST, Blazor WASM static files, dashboard and documentation-grading hub, audit component, Rebus | via gateway |
| exam-room | Virtual Room SignalR hub, independently scalable | via gateway |
| notification | Rebus consumer, email via Mailcow SMTP | internal |
| hangfire | Scheduler for due-date events, job storage in Postgres | internal, dashboard restricted (open) |
| postgres | App data, audit log, configuration, Hangfire jobs | internal |
| rabbitmq | Rebus transport | internal |
| seaweedfs | S3 object storage, SSE-C | internal |
| keycloak | OIDC, MFA, realm and clients | hosting model open (own container or existing) |

### Flows

- Browser → gateway → services. Frontend files served by main-api.
- Browser → Keycloak directly for login (public client, S05).
- Each service validates tokens itself against Keycloak signing keys (S09).
- Events: services publish to RabbitMQ with correlation ID in headers (S11). Notification and Hangfire consume.
- Documents: uploads are proxied through main-api (S06).

### Environments and config

- Dev: local Docker Compose. Prod: bitfabrik.it. Staging: open.
- `[DECIDED]` Compose files in the repo root: `compose.dev.yml` is the dev topology (all nine containers plus Mailpit, ports published on `127.0.0.1` only). `compose.example.yml` is a deployment template: copy it to `compose.prod.yml` (gitignored) and fill in the `CHANGE-ME` values. It differs by an `internal` network without outside access, no debug ports, pinned images, restart policies and Keycloak in production mode. Start in dev: `cp .env.example .env`, create `secrets/master_secret`, then `docker compose -f compose.dev.yml up --build`.
- `[DECIDED]` Secrets: `.env.example` is committed with placeholders, the real `.env` is gitignored. Service-account client secrets (notification, scheduler; S05), the SeaweedFS S3 admin identity (S06), SMTP settings (incl. sender and STARTTLS) and the upload limit live in `.env`. The master secret (S02) is the gitignored file `secrets/master_secret`, mounted through Compose `secrets:` into main-api only. Secret storage beyond this: open.
- `[DECIDED]` Mail: Mailpit is the dev mail catcher (UI on `127.0.0.1`). SMTP host, port and credentials come only from environment variables (`Smtp__*`); production points them at Mailcow.
- `[DECIDED]` Networks: only the gateway is publicly reachable, all others are on the internal network. Exception: Keycloak must also be reachable by the browser (login goes directly to it, S05), so it joins the public network behind the TLS reverse proxy. TLS termination point: open.
- `[DECIDED]` Start-up order through healthchecks and `depends_on: service_healthy`: Postgres, RabbitMQ, SeaweedFS and Keycloak (realm import) first, then main-api (runs the migrations), then the other hosts and the gateway. One database with a `hangfire` schema, one connection string per host. Every app host exposes `/health` (answers `Healthy <host>`), which its Compose healthcheck calls; the gateway also routes `/health/main-api` and `/health/exam-room`.
- App hosts build from the shared root `Dockerfile` (`PROJECT` build arg). Gateway routes: `/hubs/exam-room/**` to exam-room, everything else to main-api (`[PROVISIONAL]` hub path, spec 8 may change it).
- `[DECIDED]` Keycloak realm import: `deploy/keycloak/easy-realm.json` (S05), client secrets filled from environment placeholders. Token issuer is the browser-facing URL (`KEYCLOAK_AUTHORITY`, Keycloak `KC_HOSTNAME`); containers fetch discovery and signing keys through `Keycloak__MetadataAddress` on `keycloak:8080` (`KC_HOSTNAME_BACKCHANNEL_DYNAMIC`). Every app host except the gateway validates tokens via `Easy.Platform` (JwtBearer) and exposes the authenticated probe `/whoami`.

### Operations

- Backups: Postgres dumps, SeaweedFS volumes, Keycloak realm export. Schedule, retention, restore test: open.
- `[DECIDED]` Logging: Serilog compact JSON to stdout in every host, enriched with service name and correlation ID (HTTP middleware and a Rebus incoming step). No log stack in the skeleton: `docker compose logs`. Logs never contain participant data. Tracing and a log stack are open (S11 rules out Elasticsearch/Loki for audit only, not for ops logs).
- Monitoring and alerting: not defined.
- Scaling: exam-room scalable; other containers assumed single-instance (open).
- Limits: document upload max 15 MB by default, configurable by environment variable and enforced (S06).
- CI/CD: deferred (S09).
- Disaster recovery: not defined.
