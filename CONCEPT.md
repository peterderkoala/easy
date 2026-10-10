# Easy – Concept

Status: 2026-10-02 (brainstorming). Single source of truth for updates and references.

## READ FIRST: Known flaws and gaps

For the planning agent. Each item needs a decision or check before the affected area is planned. IDs in brackets point to the source section.

### Blockers (core output undefined)

- **Overall grade has no formula** [S04, S10]. Weighting of sources is open, and written-exam pass/fail logic is open. The main output of the system cannot be implemented or tested.
- **Written-exam status undefined** [S04, S10]. Same root cause as above.
- **"Began grading" undefined** [S05, S15]. Examiner change discards unfinished grading only before "begun", and that trigger is not defined.

### Security and privacy

- **Encryption "forget" may not delete anything** [S02]. The key is derived from participant data plus salt, on demand. If both inputs live in the database, anyone with DB access can re-derive it. Needed: key material from outside the DB (e.g. examiner-held secret or external KMS), and a defined salt location.
- **Audit log may hold personal data** [S11]. Old/new values of graded fields may contain personal data. Unclear whether audit rows are encrypted or purged on "forget".
- **Unlimited retention conflicts with GDPR storage limitation** [S11]. Even with "forget", indefinite retention of personal records needs a legal check.
- **Documents survive "forget" as ciphertext** [S06, S02]. SeaweedFS objects remain stored. Deletion of the object, not only key loss, should be considered.
- **SSE-C enforcement unverified** [S06]. Relies on a current SeaweedFS version and a read-test that is not yet done.
- **Blinding is application-level only** [S06]. Safe only if the API never sends the partner's scores. This needs an explicit DTO rule and test in S09.
- **Background consumers have no authorization model** [S09]. Notification runs without examiner context. Rules for which claims or service identity it uses are undefined.
- **Interim and closed-area points editable by any examiner** [S07]. No second check or role split. The audit type "correction" records it but does not prevent it.
- **Hangfire dashboard exposure** [S16]. Access restriction is open.

### Consistency and integrity

- **Frozen grading document vs. later corrections** [S06, S07]. The signed document can diverge from live points. Only the audit log explains the gap. Confirm this is accepted, or define a re-issue process.
- **Dual write: DB commit + RabbitMQ publish** [S09, S11, S12]. No outbox pattern is specified. A crash between commit and publish loses events (e.g. upload notification, session end).
- **Concurrent edits to the same score** [S03, S06]. "In progress" is a UI marker only. Optimistic concurrency or locking is not defined.
- **Application conflicting decisions** [S03, S15]. Two examiners may decide differently. Resolution rule is open.
- **Examiner role for applications unclear** [S03, S15]. "Two examiners" is not restricted to registered examiners. Confirm who may approve.
- **Keycloak status ambiguous** [S05]. Listed as "under consideration" but client layout is written as settled.

### Gaps

- **Single points of failure and recovery** [S16]. Postgres, RabbitMQ, Keycloak: no HA, backup schedule, restore test, or disaster recovery.
- **Dashboard values undefined** [S08, S10]. The dashboard cannot be specified or tested.
- **QuestPDF license unverified** [S06]. Could block the grading document. Fallback not defined.
- **CSV format and versioning** [S02]. Format unknown. No versioning or import error handling defined.
- **Upload size limit not enforced** [S06]. 5 MB is a guideline only.
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
| Encryption key (per participant) | Derived from participant data plus salt. Protects that participant's data (S02). |

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
- CSV format not defined (likely specified by the IHK). Validation and import may need adjustment once it is.
- Import runs asynchronously via RabbitMQ (Rebus), not synchronously in the request.
- Live progress via SignalR: nice-to-have. At max ~120 rows, long waits are not expected unless AI tooling is added to the import.

### Participant ID

- In addition to the CSV identification number, Easy assigns an internal ID:
  - Primary key: GUID.
  - Display: sequential, examiner-readable number per cycle, e.g. `WS2026-014`.

### Encryption and "forgetting"

- `[DECIDED]` Encryption key is derived from participant information (with salt). It protects that participant's data and grades. Derived on demand, not cached server-side (deliberate, for security; performance is non-critical at 10–15 examiners). See S11.
- "Forget" function: deletes a participant's encryption key, making their data effectively unreadable.
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
- Future topic, not in MVP: a background service could read the application PDF with AI and suggest tags (e.g. JavaScript, React, MS SQL).

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

- Under consideration: Keycloak for OIDC and multi-factor authentication. Initially Easy only. No existing central identity system. Keycloak would become the first central identity system, possibly reusable by other projects later.
- One Keycloak client per service:
  - Frontend (Blazor WebAssembly): public client. Login runs in the browser directly against Keycloak, regardless of which server delivers the frontend files.
  - Main API, exam room service, Hangfire: confidential clients with own secret.
- Token validation centrally via the same realm.
- Hangfire is the scheduler for the notification service (S12).
- Idea: store a digital signature in the examiner profile (S06).

---

## S06 Documentation / project work: grading

### Submission and upload

- PDF submission happens in a separate IHK environment. Download is outside the platform.
- Easy needs an upload function for the externally downloaded PDF.
- Guideline size: max 5 MB. Further format and size rules open.
- Storage: S3-compatible object storage, decided on SeaweedFS (Apache-2.0, lightweight, Docker-friendly).
  - Objects encrypted with the participant-specific key (SSE-C). No separate key.
  - Deletion concept (S11): unreadable by key deletion.
  - Use a current SeaweedFS version. Test SSE-C enforcement on read, as older versions had gaps.
- Successful upload automatically emits an event to the notification service. Registered examiners are notified (S12).
- Process waits for this submission after approval.

### Collaborative grading

- The two registered examiners grade together, live via SignalR.
- Direct grading in the PDF viewer is too complex. Instead, split view: PDF left, predefined comment sections right.
- Both examiners grade in this module. The exam is scored here.
- Other, unregistered examiners may comment. These comments do not count toward the grade.
- Blinding: by default, the two examiners' grades are hidden from each other.
  - Applies only to documentation, not to application (where it is transparent).
  - When needed, e.g. during a phone call, an examiner starts a summary view showing both grades.
  - Implemented purely at application level: other examiner's data is not sent. No separate tables per examiner.
- Deviation: if the calculated grades differ too much, the system shows a notice. A third examiner as arbiter should be considered.
  - Notice only, no automatic invitation. Decision stays with the examiners.
  - Threshold: fixed in the database configuration table, not configurable per cycle.
  - Configuration table: central key-value persistence for system settings. Normal table in the same Postgres DB, accessed via EF Core (no separate configuration service).
- Status: submitted, in progress, graded. "Graded" locks the grading.
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
- PostgreSQL with EF Core. Data model still to be designed.
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
- Auditability: scope of logged operations (S11).
- GDPR: concrete requirements for data storage and audit log (S11).
- Format and criteria of oral-exam grading comments (S07).
- Verify QuestPDF license (S06).

---

## S11 Auditability and GDPR

### Audit log

- The system must be auditable.
- `[DECIDED]` Separate table in the same Postgres DB. No Elasticsearch or Loki. Reason: joins with correlation ID, status, points, and grading documents; transactional consistency with business changes.
- `[PROVISIONAL]` Central, reusable audit component (library or EF interceptor) used by all services. Writes synchronously in the same transaction as the business change. No separate audit service: it would either be a thin wrapper or, with async messaging, lose transactional safety.
- `[DECIDED]` Audit log visible to admins only.
- Logged: every value change on grading-relevant data. Not every keystroke.
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
- Encryption via participant-specific key (S02). Deletion by key loss (S02).
- `[OPEN]` Concrete requirements for data storage and audit log, incl. which data must be encrypted without losing findability (S10).

---

## S12 Notifications

- Email service informs examiners about new items or actions. Also reminders for due dates.
- Examiners only, never participants.
- Sent via own Mailcow server on bitfabrik.it. No external provider.
- SMTP configuration later. Planned via `.env` and Docker environment variables.
- Possibly in-app notifications later, in addition to email.
- Asynchronous and decoupled from user interaction. No synchronous trigger on comments.

### Eventing

- Action-based events (e.g. application approval, session end, documentation upload) are published by services to RabbitMQ. The notification service consumes them.
- Time-based reminders (due dates): Hangfire periodically checks deadlines and publishes the event itself. Hangfire uses Rebus (S09), job persistence in Postgres.
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
| Cycle | id, type (winter/summer), year, per-module due date and deadline | 1–n Participant |
| Participant | id (GUID), cycle_id, display_no (e.g. `WS2026-014`), csv_id, name, project_title, application_status, written_status, documentation_status, oral_status, key_forgotten flag | n–m Tag; 1–n Registration; 1–n Grade; 1–n Document; 1–n Comment |
| Examiner | id, external subject (Keycloak), role (admin/examiner), active flag | 1–n Signature (optional) |
| Registration | participant_id, examiner_id | Documentation pair (2 examiners) |
| Grade | participant_id, source (written/documentation/oral/interim), value, updated_by, updated_at | Current value only, no history (S09) |
| DocumentationScore | participant_id, examiner_id, section, points | Per-examiner; hidden from partner until summary view (S06) |
| Comment | participant_id, area, author_id, counted flag, text | Uncounted comments from non-registered examiners |
| OralSession | id, participant_id, status (open/closed) | 3 main examiners; joined examiners; closed = final |
| OralGrade | session_id, examiner_id, criteria values, counted flag | Counted only for main examiners |
| TextTemplate | id, category (rejection/condition), text, created_by | Fixed plus examiner-added (S03) |
| Tag | id, name | Filter now, assignment later (S03, S05) |
| Document | participant_id, kind (application PDF / project PDF / grading document), storage key, hash, frozen flag | Stored in SeaweedFS (S06) |
| Signature | examiner_id, drawn image | Stored once in profile (S06) |
| ConfigEntry | key, value | Holds grading-deviation threshold (S06) |
| AuditEntry | id, correlation_id, actor_id, entity, field, old, new, type (initial/correction), timestamp | Admin-only read (S11) |

Derived, not stored: overall grade (S04, S09).

---

## S15 Permissions and state transitions **need check**

### State transitions (proposed)

- **Application**: submitted → approved | approved with conditions | rejected.
  - Set by two examiner decisions. Rule for conflicting decisions: open.
- **Documentation**: submitted (upload) → in progress → graded. Graded is locked.
  - Reassignment allowed only before "in progress". Trigger for "begun" (first point entry?): open.
- **Written exam**: attended / failed / passed derived from three point grades. Formula open (S10).
- **Oral session**: open → closed. Closed is final. No reopen (S07).

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
- Documents: upload path (proxied through main-api vs. presigned direct to SeaweedFS) open.

### Environments and config

- Dev: local Docker Compose. Prod: bitfabrik.it. Staging: open.
- Secrets: `.env` and Docker environment variables. Confidential client secrets per service (S05). Mechanism for secret storage beyond `.env`: open.
- Network: only gateway publicly reachable; all others on internal network. TLS termination point: open.

### Operations

- Backups: Postgres dumps, SeaweedFS volumes, Keycloak realm export. Schedule, retention, restore test: open.
- Logging: structured logs with correlation ID. Tracing and log stack: not chosen (S11 rules out Elasticsearch/Loki for audit only, not for ops logs; open).
- Monitoring and alerting: not defined.
- Scaling: exam-room scalable; other containers assumed single-instance (open).
- Limits: document upload max ~5 MB (S06, guideline, not enforced).
- CI/CD: deferred (S09).
- Disaster recovery: not defined.
