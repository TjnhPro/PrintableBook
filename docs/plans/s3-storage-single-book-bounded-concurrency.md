<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\feat-s3-storage-mvp-autoplan-restore-20261002-182912.md -->
# S3 Storage MVP Hardening Plan

## Status

- Planning only. No source implementation belongs to this phase.
- Autoplan review complete: CEO, Design, and Engineering findings are incorporated; the DX applicability gate was executed and correctly marked not applicable because this is an end-user desktop workflow, not a public developer product.
- Baseline: commit `71b00a4` on `feat/s3-storage-mvp`, compared with `origin/main` at `46468d0`.
- Original product plan: the user-provided “MVP S3 Storage cho Book” specification.
- Product premise and concurrency boundaries were explicitly confirmed by the user on 2026-10-02.

## Goal

Turn the current first-pass S3 branch into the intended Book-scoped publishing workflow:

- global S3 configuration lives in **Settings → Configuration**;
- the selected Book owns its own **S3 Storage** group in **Book Detail → Settings**;
- one and only one Book may run `Check` or `Upload` at a time across the app;
- one Book may process its seven manifest files concurrently, with a fixed `SemaphoreSlim(4, 4)` cap;
- `Upload` performs a complete preflight before the first PUT, skips synced files, uploads missing/changed files, verifies each upload, and exposes truthful progress without leaking credentials.
- every available source file is copied into one immutable local publication package before remote comparison; Upload requires all seven staged files, while Check may retain `MissingLocal` rows, so bytes cannot change between hash and PUT;
- stable public object keys remain the MVP contract; the UI and documentation explicitly disclose that cancellation or a remote failure can leave a partially updated remote set until retry completes.

## Locked premises

1. **Single-Book boundary:** the app must reject a second Book-level S3 action while another Book-level S3 action is queued, running, or cancelling. It must not create a multi-Book upload queue.
2. **Four-file boundary:** inside the one active Book operation, remote/file work is bounded by one fixed `SemaphoreSlim(4, 4)`. At most four manifest-file operations may be in flight.
3. **Seven-file manifest:** only the seven canonical published outputs are managed. No recursive scan and no remote deletion.
4. **Full upload barrier:** `Upload` completes local validation, hashing, and remote comparison for the entire manifest before any PUT starts.
5. **Book-scoped UX:** no top-level S3 route and no grid that invites multi-Book actions. Configuration is global; Check/Upload is shown only for the selected Book.
6. **Public artifact contract:** objects are uploaded with `public-read`, verified by signed HEAD metadata and unauthenticated HTTP HEAD. ACL-disabled buckets fail with a named recovery message; the app does not silently downgrade to private objects.
7. **Cancellation:** cancellation is cooperative. In-flight AWS requests receive the task token; waiters on the four-slot semaphore stop before starting new work.
8. **Stable public keys:** keep the user-specified `{folder}/{ASIN}/{file}` URLs. Versioned remote releases/current-pointer publishing are deferred; this MVP does not claim atomic visibility across seven independent S3 objects.

## Why this plan is needed

The branch contains a useful vertical slice, but several contracts do not match the original plan or the real output pipeline:

| Area | Current branch | Required end state |
|---|---|---|
| Book concurrency | S3 lane allows `2`; distinct Books run together | one Book action app-wide; second Book/action rejected, not queued |
| File concurrency | sequential `for` loop | fixed `SemaphoreSlim(4, 4)` within one Book |
| Duplicate semantics | `JoinByKey` keyed only by Book ID, so Check and Upload can accidentally join | exact duplicate Book+action joins; different action or Book gets `s3_operation_active` |
| Manifest | includes nonexistent `Cover_thumbnail_back.png`, `Cover_thumbnail_front.png`, `Interior_thumbnail.png` | one shared seven-artifact contract; S3 readiness requires all seven even though some producers currently treat previews as best-effort |
| Folder | not modeled | normalized global folder prefix, e.g. `coloring` |
| Object key | `{ASIN}/{file}` | `{folder}/{UPPERCASE-ASIN}/{file}` |
| Endpoint | standard regional endpoint unless user manually types a URL | SDK dual-stack + path-style by default; URL matches the required absolute path |
| AWS client lifetime | creates/disposes a client for every HEAD/PUT | one thread-safe client session per Book operation, shared by its four workers |
| Remote check | one HEAD per file only | prefix existence plus per-file HEAD size/hash metadata |
| Upload verification | PUT only | PUT, signed HEAD verification, public HTTP HEAD |
| Settings storage | encrypted credentials and config mixed in `storage.json` | ordinary config in `GlobalSettings`; credentials in atomic DPAPI `s3.credentials.dat` |
| Safe snapshot | bucket/region/base URL + boolean | bucket/region/folder, credential state, masked access-key hint; never secret |
| UI location | separate top-level S3 page listing every Book | global config group + selected-Book group |
| Missing local file | Check and Upload both fail before per-file result | Check reports all rows; Upload performs zero PUT and explains missing files |
| Task conflicts | S3 has no conflicts | one global S3 owner plus a same-Book output snapshot lease; unrelated Book work and Cache Cleanup remain available |
| Process boundary | task manager is in-memory | define “app-wide” as one running desktop process; multi-instance coordination is explicitly outside this MVP |
| Crash evidence | terminal session is memory-only | persist one secret-free latest publication receipt per Book; no history/queue |

## What already exists and should be reused

| Sub-problem | Existing code/pattern | Planned reuse |
|---|---|---|
| Background lifecycle | `BackgroundTaskManager`, `BackgroundTaskPolicy`, `BackgroundTaskWorker<TRequest,TView>` | keep queue/run/cancel/terminal ownership in Desktop task manager |
| Book identity and ASIN | `BookProductionMetadata`, `S3StoragePolicy.NormalizeAsin` | keep ASIN validation at the Core boundary |
| Secure Windows storage | DPAPI `CurrentUser` use in `JsonS3StorageSettingsStore` | extract into dedicated credential store; preserve entropy/versioning and atomic writes |
| Atomic filesystem writes | `IFileSystem.WriteTextAtomicallyAsync` | write encrypted credential envelope safely |
| Published output naming | `ValidatedBookOutputPublisher`, `CoverPanelPreviewFileNames` | derive one canonical S3 manifest from actual output contracts |
| Safe bridge boundary | typed bridge records and `BridgeResponse` | keep AWS SDK types and secrets out of WebView messages |
| Book Settings pattern | `book-settings-workspace` fieldsets with legends | add one full-width S3 group without creating a new visual language |
| Configuration pattern | `configuration-group` / full-width group | add global S3 configuration with the existing scroll container and save feedback |
| Session polling | production/crawl task polling patterns | one app-global S3 session poll that survives Book switching and rerenders |
| Tests | fake S3 client and blocking background workers | extend them with deterministic barriers and max-active counters |

## Architecture decision

### Alternatives considered

| Approach | Summary | Effort | Risk | Completeness | Decision |
|---|---|---:|---:|---:|---|
| A. Minimal loop patch | Change lane `2 → 1`, wrap current per-file body with a semaphore, retain separate S3 page and current store/client | S | High | 4/10 | Rejected: permits queued second Books, preserves the wrong manifest and makes concurrent client creation/progress races likely |
| B. Bounded worker refactor | Keep current service surface, add a two-phase worker and shared operation client, then move UI/config to the requested locations | M | Medium | 8/10 | Rejected as final target: still carries `storage.json`/route-era contracts and ambiguous duplicate semantics |
| C. Contract-first hardening | Replace unshipped S3 contracts where needed, align persistence/manifest/endpoint/task policy/UI, then add bounded two-phase execution | L | Low | 10/10 | **Selected**: best fit for the original plan and the user’s permission to reshape current code |

### Component map

```text
Settings → Configuration
  ├─ ordinary S3 config: region / bucket / folder
  └─ credential form: access key / secret key
            │
            ▼
S3 settings coordinator
  ├─ GlobalSettings.S3StorageConfiguration
  ├─ page Save: non-secret configuration only
  └─ explicit Replace credentials: DPAPI s3.credentials.dat

Book Detail → Settings → S3 Storage
            │ book.s3.check/upload/cancel/get
            ▼
S3StorageService
  ├─ validates Book + ASIN + config
  ├─ validates unique ASIN ownership across discovered Books
  ├─ creates a provisional opaque operation context
  ├─ atomically admits or joins one active Book/action in this app process
  ├─ disposes the provisional context when the manager joins/rejects
  └─ starts S3StorageWorker
            │
            ▼
S3StorageWorker (one Book)
  ├─ shared 7-artifact contract + immutable local staging package
  ├─ shared S3 operation session/client
  ├─ SemaphoreSlim(4, 4)
  ├─ Phase A: stable snapshot + hashes + prefix/HEAD/public comparison
  └─ Phase B: PUT changed/missing + signed HEAD + public HTTP HEAD
```

## Canonical data contracts

### Global configuration

Add a non-secret nested record to `GlobalSettings`:

```text
S3StorageConfiguration
├─ Region = "us-east-1"
├─ Bucket
└─ Folder
```

Rules:

- `Region`, `Bucket`, and `Folder` are ordinary settings and serialize in `settings.json`.
- Folder is trimmed, converts `\` to `/`, removes leading/trailing `/`, rejects empty segments, `.` and `..`, and stores no trailing slash.
- `AccessKeyId` and `SecretAccessKey` never enter `GlobalSettings`, `ApplicationSnapshot`, diagnostics, exceptions, or bridge response payloads.
- `s3.credentials.dat` is a versioned encrypted envelope protected by Windows DPAPI `CurrentUser`, written atomically next to `settings.json`.
- Configuration and credentials have separate commit boundaries: the existing page Save writes only Region/Bucket/Folder, while an explicit `Replace credentials` action requires both credential values and atomically replaces only `s3.credentials.dat`. Blank password fields are never interpreted as a credential mutation.
- Before Start, create one provisional short-lived in-memory operation context containing the immutable validated config, decrypted credentials, and configuration revision. The task request carries only an opaque context ID; snapshots, diagnostics, receipts, bridge DTOs, and terminal task history never contain raw credentials.
- Extend the manager admission result for this path to return both the task snapshot and `WasCreated`. If the exact task already exists or admission rejects, the service immediately destroys the new provisional context; only a newly registered task owns it. This closes the duplicate-start race without moving admission outside the manager lock.
- Exact duplicate Start reuses the existing task/context. The owned context is removed and sensitive buffers/references cleared after completed, failed, or cancelled terminal unwind, including queued cancellation and manager disposal paths.
- S3 configuration and credentials may be edited while a task runs, but the UI states “Applies to the next S3 operation”; unrelated Configuration controls are never disabled by S3.
- Safe UI status is a record containing `Status = Configured | NotConfigured | CredentialsUnavailable` and an optional masked key hint such as `AKIA••••7G2P`.
- Because the S3 feature has not shipped from this branch, `storage.json` is deleted with no production migration. Tests must remove any expectation that it remains a supported persistence format.

### Canonical manifest

Introduce one shared `BookOutputArtifactContract` used by output producers, readiness checks, S3, and tests. Its stable display order is:

1. `{BookId} - Cover.pdf`
2. `{BookId} - Interior.pdf`
3. `{BookId} - Cover_thumbnail.pdf`
4. `{BookId} - Cover_thumbnail.png`
5. `{BookId} - Interior_thumbnail.pdf`
6. `back_cover.jpg`
7. `front_cover.jpg`

Do not include `Cover_thumbnail_back.png`, `Cover_thumbnail_front.png`, or `Interior_thumbnail.png`; the current production pipeline does not publish them.

The existing publisher deliberately treats preview PDFs and cover-panel JPEGs as best-effort. Do not describe all seven as already guaranteed output. For this MVP:

- Cover/Interior production continues to report its current warning states;
- S3 readiness is stricter: all seven contract artifacts must exist before Upload;
- every output-producing path refreshes a persisted `BookPublicationPackage` readiness record using the shared contract;
- `Check` still reports all missing rows; `Upload` remains zero-PUT until the package is complete;
- Phase 0 must prove the actual commands that produce each artifact and add recovery copy identifying which Production action the user must run.

Each manifest item carries:

- stable index;
- local file name and absolute `FileReference` kept in Core/Desktop only;
- normalized object key;
- content type;
- local existence, length, and SHA-256 when available;
- remote existence, length, SHA-256 metadata, and public reachability;
- file state and named error code.

Create a task-owned sparse staging package under `{application-root}/.printablebook/s3-staging/{taskId}`, outside each Book's temporary-output root because Cache Cleanup deletes that root. While holding the same-Book output snapshot lease, copy each available source into staging; missing sources become `MissingLocal` rows instead of aborting Check. Hash staged files, then release the lease. Both remote comparison and PUT use only immutable staged files.

Staging rules:

- resolve and validate every staging path beneath the canonical staging root; reject traversal, symlink, or reparse-point escapes;
- compute aggregate source size and available disk space before copying; fail Upload with `s3_staging_insufficient_space` before remote mutation;
- copy/hash work uses the same `SemaphoreSlim(4, 4)` or runs sequentially—never a second limiter;
- partial/terminal staging cleanup is best-effort and cannot replace the real task outcome; stale directories are cleaned only when no S3 task is active in this app process;
- Cache Cleanup never owns or removes this staging root.

### Destination

```text
object key = {normalized-folder}/{UPPERCASE-ASIN}/{local-file-name}
public URL = https://s3.dualstack.{region}.amazonaws.com/{escaped-bucket}/{escaped-object-key}
```

- Escape path segments independently; never escape `/` separators into `%2F`.
- Use `ForcePathStyle = true` and `UseDualstackEndpoint = true` for AWS SDK requests.
- One operation-scoped `AmazonS3Client` is shared by all four file workers. AWS documents service clients as thread-safe; do not create one client per HEAD/PUT.
- `PublicBaseUrl` is removed from the MVP. It conflates API endpoint override and public URL construction and was not part of the original product contract.

## Single-Book scheduling contract

### Policy

Change `BackgroundTaskKind.S3Storage` to:

```text
Lane: Storage
MaximumConcurrency: 1
DuplicatePolicy: ReturnExistingByKey
Conflicts: none at the kind-policy layer; same-kind admission is already enforced by `ReturnExistingByKey`
```

Do not use kind-wide conflicts for unrelated Books. Extend the task/resource guard with a same-Book `OutputSnapshot` lease: output writers for Book A and S3 snapshot creation for Book A exclude one another, while work on Book B remains allowed. Cache Cleanup does not delete published `Output` files and does not conflict with S3.

Define one `IBookOutputLeaseCoordinator` keyed by canonical application root + Book ID:

- every final-output writer acquires the Book write lease immediately before replacing any published artifact and holds it through its atomic publication step;
- S3 acquires the same Book lease only while copying available artifacts into immutable staging;
- an S3 lease conflict returns `book_output_busy` instead of waiting invisibly;
- a multi-Book Processing Session reports/skips the busy Book according to its existing per-Book result model rather than failing unrelated Books;
- tests cover Production→S3 and S3→Production acquisition order. The lease is process-local because multi-instance coordination is outside this phase.

### Key semantics

- Task key: `s3:{normalized-book-id}:{action}`.
- Subject: raw/canonical Book ID for display and lookup.
- Exact repeat of the same Book+action returns the existing task/session.
- Same Book with the other action, or any other Book while S3 is active, throws a conflict and maps to `s3_operation_active`.
- Reuse the manager's existing atomic `ReturnExistingByKey` behavior inside its registry lock: exact same key returns the active task; a different key of the same kind throws before registry insertion. Do not add a service-level “check active, then start” preflight that creates a TOCTOU race.
- Return an admission result that distinguishes `WasCreated` from `JoinedExisting`. The S3 service may prepare a provisional operation context before registration, but it must retain that context only when `WasCreated = true`; joining or rejecting destroys the provisional context immediately.
- Cancelling remains active until the worker unwinds. Another Book cannot start during `Cancelling`.
- “App-wide” means one running desktop process for this phase. Cross-process/multi-instance locking is not claimed.

### Cross-feature behavior

| Active work | New S3 Check/Upload | New active work while S3 runs |
|---|---|---|
| Processing Session for same Book while snapshot lease is held | reject/wait according to existing foreground rule | S3 snapshot rejects with `book_output_busy` |
| Production output writer for same Book while snapshot lease is held | reject with `production_action_active` | S3 snapshot rejects with `book_output_busy` |
| Processing/Production for another Book | allowed | allowed |
| Cache Cleanup | allowed | allowed |
| Library Refresh | allowed | allowed |
| Amazon crawl | allowed | allowed |
| S3 on same Book/action | join existing session | n/a |
| S3 on other Book/action | reject immediately | n/a |

## Four-slot execution contract

### Semaphore ownership

- Define one named constant `MaximumFileConcurrency = 4`.
- Construct exactly one `SemaphoreSlim(MaximumFileConcurrency, MaximumFileConcurrency)` for the lifetime of a Book operation.
- Both preflight and upload/verification use the same gate.
- Every `WaitAsync(cancellationToken)` is paired with `Release()` in `finally` only after acquisition.
- Do not layer `Parallel.ForEachAsync`, another semaphore, or AWS TransferUtility concurrency on top. One limiter owns concurrency.

### Two-phase pipeline

```text
START
  │ validate Book / unique ASIN / captured config + credentials
  │ acquire same-Book output lease; sparse-stage available files; release lease
  │ list prefix once
  ▼
PHASE A: PREFLIGHT (7 work items, max 4 active)
  staged local exists? → length + streaming SHA-256
  remote listed? → HEAD length + metadata SHA + unauthenticated public HEAD
  classify Synced / SyncedButNotPublic / Changed / MissingRemote / MissingLocal / Error
  │
  ├─ Check → publish final Check result, no PUT
  │
  └─ Upload
       ├─ any MissingLocal/Error that invalidates upload → finish all 7 comparisons, zero PUT
       └─ barrier passed
             ▼
PHASE B: UPLOAD (only Changed/MissingRemote, max 4 active)
  PUT immutable staged file with public-read + content type + sha256 + length metadata
  signed HEAD verify length/hash
  unauthenticated HTTP HEAD verify 2xx public URL
  classify Uploaded / Skipped / Failed
```

### Progress and thread safety

- Preserve manifest display order regardless of task completion order.
- Each file worker returns an immutable result tagged by index.
- A small lock protects replacement of the indexed row, completed count, timestamps, and `context.SetView` publication.
- `CompletedCount` counts terminal file rows, not scheduling attempts.
- During preflight it advances `0..7`; during upload expose a separate stage plus `UploadCompletedCount/UploadTotalCount`, or a typed progress record. Do not reset a single counter ambiguously.
- `LastCheckedAtUtc` is set only after the preflight barrier completes.
- `LastUploadedAtUtc` is set only after at least one file has been PUT and verified successfully.
- Per-file remote or local errors are captured as named states. `OperationCanceledException` is never converted to a file error.
- Persist one versioned, secret-free `LatestS3PublicationReceipt` per Book in its workspace. Atomically write `Running` before remote work, update after each verified file/important phase, and write the terminal state in `finally`.
- On startup/load, convert any receipt still marked `Running` to `Interrupted`; an in-flight file becomes `Unknown`, never guessed as uploaded or failed. Retry always performs a fresh seven-key preflight—this receipt is recovery evidence, not a resume journal.
- Receipt fields: schema version, Book ID, normalized ASIN, captured destination, action/outcome, configuration revision, per-file hash/state/error, and started/updated/finished timestamps. The revision hashes Region/Bucket/Folder plus a credential-generation GUID, never secret text.
- Corrupt/unsupported receipt produces a recoverable `Receipt unavailable` state and never blocks Check.

### Cancellation and partial completion

- Cancelling prevents semaphore waiters from entering.
- In-flight hash, S3, and HTTP calls receive the same token.
- Files already verified remain visible in the retained view.
- A cancel during Phase B can leave a mixed-generation remote set because the seven stable keys are independent. The final outcome is `Cancelled` with an explicit “retry to complete publication” warning; retry rechecks the immutable current package and repairs missing/changed objects.
- No resume journal is added in this MVP.

## S3 adapter contract

Replace the current per-call `CreateClient` shape with an operation session:

```text
IS3ObjectSessionFactory.Open(settings)
  → IS3ObjectSession : IAsyncDisposable
      ListPrefixAsync(prefix)
      HeadAsync(key)
      PutAsync(key, source, contentType, sha256, length)
```

The Infrastructure implementation owns one `AmazonS3Client` per session and uses it concurrently. A separate typed `HttpClient` checks public URLs without credentials.

Required request behavior:

- `ListObjectsV2` uses the exact `{folder}/{ASIN}/` prefix and handles continuation tokens even though only existence/membership is needed.
- `HeadObject` maps 404/`NoSuchKey` to missing; reads content length plus case-insensitive `printablebook-sha256` and `printablebook-length` metadata. Every existing managed object also receives unauthenticated public HEAD during Check/Upload preflight, including hash-matching objects that will be skipped.
- `PutObject` streams from disk, sends the exact MIME type, `public-read`, and both metadata fields.
- After PUT, signed HEAD must match local length/hash before public verification starts.
- Public HTTP HEAD accepts 2xx only and uses a finite timeout linked with cancellation.
- Map AWS errors precisely: invalid credentials, access denied, bucket not found, wrong region/redirect, network/service unavailable, ACL unsupported, request failed.
- `AccessControlListNotSupported` maps to `s3_public_acl_unsupported` with guidance to enable ACL-compatible ownership/public policy; never retry privately.
- Map `AmazonS3Exception.ErrorCode` before HTTP status: distinguish `NoSuchBucket` from object `NoSuchKey`; map `AuthorizationHeaderMalformed` and redirect region hints to `s3_region_mismatch`; do not assume every 403 is invalid credentials.
- Use the AWS SDK's finite standard retry policy only; add no outer retry loop that multiplies PUT attempts. Public HEAD has its own finite timeout and verifies expected content length as well as 2xx status.
- `ListObjectsV2` describes objects under a virtual destination prefix. Zero results is a clean empty destination, not a missing-folder error.

## UI plan

### Settings → Configuration

Add a full-width `S3 Storage` fieldset inside the existing scrollable configuration panel:

- first subsection, `Destination settings`: Region, Bucket, Folder, plus “Saved by the page Save button” and an “Applies to next operation” notice if an operation is active;
- second subsection, `Credentials`: Access Key ID and Secret Access Key password inputs, safe credential badge, masked access-key hint, and an explicit `Replace credentials` button;
- `Replace credentials` is `type="button"`, enabled only when both fields are nonblank, clears both DOM values after success, and announces the new masked status without returning either secret;
- each subsection owns its own visible status/error region; errors use `role="alert"` only when newly surfaced, while routine saved/configured feedback uses `role="status"`;
- the existing page Save remains primary for all ordinary settings. S3 activity never disables unrelated Configuration fields.

### Book Detail → Settings

Add one full-width `S3 Storage` fieldset after existing Book settings groups:

- header: configuration badge, normalized ASIN, destination prefix, and latest-result timestamp;
- seven stable rows showing local state/size, remote state, and public status. Do not render raw long URLs; use `Open` and `Copy URL` actions with the full URL in accessible labels;
- stage label and one meter for `Preparing package`, `Comparing 0/7`, `Publishing and verifying N/M`, then a terminal outcome. Upload and verification are interleaved per file and must not be shown as false sequential global stages;
- `Check` secondary, `Upload` primary, `Stop publishing` only while active. After click it remains visible and disabled as `Cancelling…` until unwind;
- `Check` remains enabled with missing local files; `Upload` is disabled until all required local files exist;
- during Phase B show “Some files may already be public; retry completes the set.” A cancelled terminal result repeats the warning and presents `Retry Upload` as primary;
- if another Book owns the active S3 task, show “S3 is active for {Book}” and a `View active Book` action rather than silently queueing;
- changing tabs or Books must not cancel the task or lose its latest result.

### Information hierarchy and three view modes

```text
APP SHELL
└─ global process status
   └─ S3 chip when active: action · owner Book · phase/count
      └─ activate → Books → owner Book → Settings → S3 heading

BOOK DETAIL → SETTINGS
└─ S3 Storage (full width; one job: publish this selected Book)
   ├─ Orientation: Config badge · ASIN · Destination · Last result time
   ├─ Live summary: phase, progress, concise aria-live text
   ├─ Artifact matrix: seven rows, stable order
   ├─ Action/recovery footer
   └─ Visible disabled reason / warning
```

The selected Book group has exactly three mutually exclusive modes:

| Mode | What the user sees | Actions |
|---|---|---|
| Live owner | Current task for this Book, live phase/count, current rows | Stop publishing; Check/Upload disabled |
| Blocked by another owner | “S3 is active for {Book A}”; this Book's rows are explicitly labelled `Last result` with timestamp and captured destination | View active Book; Check/Upload disabled |
| Idle/latest receipt | This Book's latest persisted receipt or warm empty state | Check; Upload when eligible; Retry Upload after partial/cancelled receipt |

Never merge the active session for one Book into another Book's receipt. The shell-level S3 chip keeps the live operation discoverable from Configuration, Production, or another route without recreating a top-level Storage page.

### Interaction-state coverage

| Surface | Loading | Empty/not ready | Error | Success | Partial/active-other |
|---|---|---|---|---|---|
| Configuration destination | controls stay visible; status says `Loading configuration…` | unconfigured badge and direct instruction to enter Region/Bucket/Folder | inline field/group error; page Save remains available | saved timestamp/status | active task notice says edits apply next run |
| Credentials | fields remain blank; safe status loads | `Not configured` plus Replace disabled reason | actionable credential/DPAPI error, never secret text | masked key hint; password fields cleared | active task notice; replacement applies next run |
| Book S3 group | seven skeleton/status rows with actions disabled | warm recovery copy for missing config, invalid/duplicate ASIN, or missing artifacts | affected row text plus one group summary/recovery | `7/7 synced and public`, receipt time, Check/Upload available | live owner, blocked-by-owner, cancelled/mixed, and completed-with-errors modes as defined above |
| Public URL action | hidden while object absent | `Not uploaded` text | `Public verification failed`; Open disabled; Copy still hidden | Open + Copy URL | hash-synced but non-public row is explicit `Synced, not public` |

Every disabled action has visible reason text referenced by `aria-describedby`; status never depends on color or a tooltip alone.

### Responsive and accessibility contract

- `.book-settings-s3 { grid-column: 1 / -1; }`; reuse the existing fieldset/legend pattern, surfaces, borders, spacing tokens, buttons, badges, and typography.
- Do not add an inner vertical scroll area for seven rows; Book Detail remains the single vertical scroll owner.
- At widths `>= 900px`, render a compact matrix with Artifact / Local / Remote / Public / Actions columns.
- Below `900px`, each row becomes a labeled fact stack; filenames and destination prefixes wrap instead of ellipsizing critical identity.
- Below `680px`, footer actions wrap and each action becomes full-width in priority order: Retry/Upload, Check, Stop.
- Active phase meter uses `role="progressbar"` with `aria-valuemin`, `aria-valuemax`, and `aria-valuenow`; preparing without a known total uses text, not a fake percentage.
- One concise `aria-live="polite" aria-atomic="true"` summary announces phase/count changes. Do not announce every row on every poll. Newly actionable terminal failures may use `role="alert"` once.
- Preserve keyboard focus when Stop changes to Cancelling or disappears; `View active Book` focuses the S3 legend/status heading after navigation.
- Existing `:focus-visible`, reduced-motion behavior, semantic button types, and minimum contrast remain mandatory.

### Polling and rerender contract

- Keep one app-global S3 poll owner/timer.
- Render the group with a stable `[data-book-s3]` root and patch it through a dedicated `patchBookS3View()` path.
- A poll tick must never call full `render()`, `renderBooks()`, or `refreshBookDrawerBody()`; those paths replace `innerHTML` and can destroy focus, selection, scroll, and unsaved Book drafts.
- Before a targeted patch, capture active element/action, selection range where applicable, Book drawer scroll, and any row action focus; restore only when the target still exists and is enabled.
- Tests run multiple poll cycles while the user edits another Settings field and assert draft value, caret, focus, selected Book/tab, and drawer scroll remain unchanged.

### User journey

| Step | User does | Intended feeling | UI support |
|---|---|---|---|
| 1 | Opens Book Settings | Oriented | one full-width group names config, ASIN, destination, and last result immediately |
| 2 | Sees missing prerequisite | Knows what to fix | one visible reason and exact destination/Production action, not a disabled button alone |
| 3 | Runs Check | In control | no remote mutation; seven stable rows update with one calm phase summary |
| 4 | Starts Upload | Confident | full preflight, explicit current Book owner, global chip, fixed progress semantics |
| 5 | Switches route/Book | Reassured work continues | global chip remains; selected Book shows its own receipt separately |
| 6 | Encounters failure/cancel | Recoverable, not trapped | partial-publication warning, retained rows/receipt, one primary Retry action |
| 7 | Completes | Certain | `7/7 synced and public`, timestamp, and usable Open/Copy actions |

### Remove the multi-Book surface

- Remove `storage` from top-level route names/navigation.
- Delete the S3 Book grid/cards and per-Book timer map.
- Replace `storagePendingBooks`/`storagePollTimers` with one app-global session owner and timer.
- Keep global configuration state independent from the selected Book, and attach the Book view only to the selected Book settings render.
- Extend the existing shell process-status control or add an adjacent compact S3 status chip; do not add navigation or a dashboard card.

## Bridge contract

Use Book-domain commands:

- `book.s3.start` with `{ bookId, action: "check" | "upload" }`;
- `book.s3.get` with `{ bookId }` but response also identifies any other active Book owner;
- `book.s3.cancel` with `{ bookId }` and task ownership validation;
- a safe settings command or coordinated `settings.save` path for configuration/credential writes.

Every response is typed and secret-free. Invalid JSON/action/book/ASIN/config and background conflicts return stable codes. An active task for a different Book is data the UI can explain, not an unhandled bridge failure.

Prefer an app-global session query (`book.s3.get-active`) plus a selected-Book latest-receipt query over overloading another Book's session into `book.s3.get`. Whatever command names are chosen, the response types must keep `activeSession` and `selectedBookReceipt` separate.

`settings.save` must not reuse the current numeric `[data-setting] → Number(...)` loop for S3 strings. Add an explicit nested `s3StorageConfiguration` serializer/binder for Region/Bucket/Folder. Append the optional S3 record parameter to `GlobalSettings`, normalize it in `JsonGlobalSettingsStore`, and regression-test that saving S3 strings preserves every unrelated numeric/global setting. `Replace credentials` is a separate bridge command and never submits the page form.

## Shutdown ownership

Extend the existing close/restart coordinator to treat S3 as an active operation:

```text
Close / app-update restart requested
  ├─ no active S3 → existing shutdown path
  └─ S3 active
      ├─ explain: public files may already be partially updated
      ├─ Stay → return to app
      └─ Stop and close
          ├─ cancel S3
          ├─ await worker + all child file tasks with finite timeout
          ├─ terminal/interrupted receipt persisted
          └─ close; timeout requires explicit Force close choice
```

- Do not release operation context/Book lease or dispose the S3 session until every child file task has drained.
- App-update restart uses the same gate; it cannot silently terminate an active PUT.
- A forced process termination may still interrupt a PUT; the next launch converts `Running` receipt to `Interrupted` and requires a fresh preflight.

## Error and rescue registry

| Code | Trigger | Caught/mapped at | User rescue |
|---|---|---|---|
| `s3_settings_required` | missing bucket/region/folder or credentials | service start | save global S3 settings |
| `s3_credentials_incomplete` | only one credential supplied | credential coordinator | enter both or clear both |
| `s3_credentials_unavailable` | DPAPI/file decode failure | credential store | re-enter and save credentials |
| `s3_asin_invalid` | ASIN is not ten alphanumeric chars | service start | save a valid Book ASIN |
| `s3_asin_duplicate` | another discovered Book owns the same normalized ASIN | service start | assign a unique ASIN before publishing |
| `s3_operation_active` | another Book/action is queued/running/cancelling | task policy/bridge | wait or cancel the visible owner task |
| `book_output_busy` | same-Book producer owns the output snapshot resource | service start/worker | wait for production to finish, then retry |
| `s3_staging_insufficient_space` | staging package cannot fit on the destination volume | worker preflight | free disk space and retry; zero PUT |
| `s3_receipt_unavailable` | receipt is corrupt or has unsupported schema | receipt store | Check again to replace it; publishing is not blocked |
| `s3_output_files_missing` | Upload preflight has missing local rows | worker result | rebuild named output files; Check remains available |
| `s3_credentials_invalid` | AWS rejects credentials | adapter | replace keys and verify IAM |
| `s3_access_denied` | IAM/bucket policy denies list/head/put | adapter | grant the named minimum permissions |
| `s3_bucket_not_found` | bucket does not exist or is not visible | adapter | correct bucket/region |
| `s3_region_mismatch` | redirect/authorization region mismatch | adapter | correct Region |
| `s3_public_acl_unsupported` | Object Ownership disables public-read ACL | adapter | change bucket ownership/policy; no private fallback |
| `s3_public_verification_failed` | unauthenticated HEAD is non-2xx/timeout | adapter/worker | fix public access policy and retry Check/Upload |
| `s3_remote_verification_failed` | post-PUT length/hash differs | worker | retry; inspect proxy/bucket behavior |
| `s3_local_file_unavailable` | file disappears/locks/changes while read | worker | stop producer, rebuild output, retry |
| `s3_service_unavailable` | network/DNS/service failure | adapter | restore network and retry |
| `s3_cancelled` | user cancels | task manager | rerun; preflight makes retry idempotent |

## Failure modes registry

| Failure mode | Prevention/detection | Expected behavior |
|---|---|---|
| User starts Book B while Book A runs | policy concurrency 1 + reject-on-other-key | no queued Book B task; UI names active owner |
| User double-clicks Upload | exact Book+action duplicate key | both calls observe one task ID |
| User clicks Upload during Check on same Book | different action key + S3-kind conflict | Upload rejected; never mistaken for Check session |
| Four tasks finish out of order | indexed immutable results + locked publication | stable seven-row order and monotonic progress |
| Cancellation while three items wait | token passed to `WaitAsync` | waiters never enter or release unacquired permits |
| Local file disappears after eligibility snapshot | fresh worker preflight | row becomes MissingLocal; Upload sends zero PUT |
| Local file changes between hash and PUT | upload only immutable task-owned staged copies | source changes affect only the next run; the current hash and PUT bytes stay identical |
| One PUT fails while peers succeed | per-file result isolation | completed-with-errors; retry skips verified files |
| App closes/restarts during upload | shutdown gate cancels and drains; running receipt checkpointed | remote set may be mixed; timeout needs explicit Force close; next run shows Interrupted |
| Two Books share one ASIN | library-wide normalized uniqueness check | no remote key collision; action rejected before staging |
| Cache Cleanup runs during S3 | staging lives outside Book temp/cache roots | active staging is untouched; both operations may continue |
| Credentials/config change mid-task | immutable operation context and revision | running task uses captured values; change applies next run |
| Bucket has dotted name | path-style dual-stack | no TLS wildcard hostname failure |
| Bucket rejects ACL | explicit AWS error mapping | clear recovery; no hidden private upload |
| Secret reaches WebView/log | safe DTO boundary + redaction tests | test fails; response/log never contains raw key |

## Implementation phases

### Phase 0 — Freeze contracts and remove false assumptions

1. Run a sandbox-bucket Gate 0 using the intended dotted bucket: prove dual-stack path-style, credentials, `public-read`, signed HEAD, unauthenticated HEAD, and the bucket's Object Ownership/ACL posture before adapter refactoring.
2. Trace and document which existing Production action emits each of the seven required files; add the shared `BookOutputArtifactContract` and failing readiness tests so optional producer behavior is not mistaken for S3 readiness.
3. Add failing contract tests for folder-prefixed key/dual-stack URL, normalized-ASIN uniqueness, and a complete seven-file publication package.
4. Add failing background-policy tests proving one S3 task per app process and immediate rejection of Book B/different action, including truly concurrent Start calls.
5. Add a concurrency-test fake that records active/maximum file operations and can block/release phases deterministically.
6. Record the current separate-route/storage.json contracts as intentionally replaced, not compatibility requirements.

Exit criteria: live AWS compatibility evidence is recorded; every artifact has a known producer/recovery action; tests describe the target before implementation; no ambiguity remains about identity, task ownership, stable-key partial-update behavior, or the four-slot limit.

### Phase 1 — Configuration and credential boundary

1. Add non-secret S3 configuration to `GlobalSettings` with backward-compatible defaults for settings files that lack the new section.
2. Introduce the dedicated versioned DPAPI credential store and separate `Replace credentials` command.
3. Add immutable in-memory operation contexts keyed by opaque IDs, with credential generation/revision and terminal cleanup hooks.
4. Add an admission result/overload exposing `WasCreated`, so duplicate and rejected Starts cannot leak a provisional credential context.
5. Add explicit S3 string binding to `GlobalSettings`/frontend save; remove `PublicBaseUrl` and `storage.json` from active contracts.
6. Add safe configuration/credential status DTOs and secret-redaction tests.

Exit criteria: old settings still load; config persists normally; secrets persist only in `s3.credentials.dat`; no snapshot/bridge/log contains either raw key.

### Phase 2 — Manifest, destination, and reusable S3 session

1. Introduce the shared seven-output contract, publisher/readiness integration, sparse immutable staging package outside cleanup roots, and versioned latest-receipt store.
2. Add folder normalization and segment-safe object key/public URL building.
3. Introduce operation-scoped S3 session factory; configure path-style dual-stack.
4. Implement paged prefix list, metadata HEAD, PUT metadata/content type/public-read, signed verification, public HTTP verification, and precise error mapping.
5. Extract pure request/config builders where needed so Infrastructure behavior is unit-testable without AWS.

Exit criteria: adapter tests prove endpoint flags, prefix, MIME, ACL, metadata, error mapping, and public URL behavior without network access.

### Phase 3 — One active Book and resource-scoped output safety

1. Set S3 lane concurrency to one and use reject-on-other-key semantics.
2. Key exact duplicate requests by Book+action.
3. Reuse atomic `ReturnExistingByKey` admission, return `WasCreated`, and add the process-local same-Book output snapshot lease; do not add a separate active-task pre-check or block unrelated Books/Cache Cleanup.
4. Validate normalized ASIN uniqueness across discovered Books before task registration.
5. Map ownership/resource conflicts to stable bridge states identifying the active Book/process condition.

Exit criteria: no second Book/action can be queued in the current desktop process; exact duplicates join; cancelling continues to hold the global S3 lease; unrelated Book production remains available.

### Phase 4 — Two-phase four-slot worker

1. Split current sequential loop into sparse snapshot/staging, typed preflight, and upload/verification phases.
2. Use one `SemaphoreSlim(4, 4)` shared across both phases.
3. Add the preflight barrier that guarantees zero PUT until all seven rows are checked and Upload eligibility is confirmed.
4. Make progress publication ordered, locked, monotonic, and cancellation-safe.
5. Verify public access for every existing managed object, not only objects uploaded in this run.
6. Persist Running/phase/per-file receipt checkpoints, retain results on partial error/cancellation, and compute final outcome/timestamps from verified results.

Exit criteria: deterministic tests observe maximum active file work exactly four when enough work exists, never five; all PUTs occur after the full preflight barrier; cancellation stops waiters.

### Phase 5 — Move UI into existing workflows

1. Add global S3 fieldset to Configuration and integrate safe save/status behavior.
2. Add the full-width Book S3 fieldset with the three view modes, seven-row responsive matrix, truthful phase meter, Check/Upload/Stop/Retry actions, and active-owner navigation.
3. Replace many-Book frontend maps/timers with one global S3 session model.
4. Add the shell S3 owner chip and targeted `[data-book-s3]` patch path; never full-rerender on a poll tick.
5. Remove the top-level S3 route, navigation item, Book grid, nested file-scroll surface, and obsolete route CSS.
6. Preserve focus/caret/scroll/drafts and latest task view across polls, rerenders, and Book switching.
7. Integrate S3 with close/app-update restart: explain partial publication, cancel, drain with timeout, and require an explicit Force close after timeout.

Exit criteria: UI makes multi-Book upload impossible by construction and still communicates an operation owned by another selected Book.

### Phase 6 — Verification, documentation, and cleanup

1. Complete Core, Desktop, Infrastructure, frontend contract, and cancellation/concurrency tests.
2. Update `docs/architecture.md`, `docs/user-guide.md`, `.gitignore`, and IAM/setup guidance.
3. Run filtered S3 suites, full solution tests, frontend UI contract tests, Release build, and a manual sandbox-bucket smoke test.
4. Confirm public URL returns HTTP 200 for all seven files and retry uploads zero unchanged files.
5. Remove obsolete classes, routes, tests, and CSS only after all replacement paths are green.

Exit criteria: automated gates pass, manual evidence is recorded, and repository search finds no obsolete manifest names, `PublicBaseUrl`, `storage.json`, or multi-Book storage UI state.

## Test plan

### Core

- settings defaults and folder normalization, including whitespace, repeated separators, slash conversion, `.`/`..`, and Unicode/unsafe segments;
- ASIN normalization and invalid values;
- exact seven-file manifest and stable order;
- object key/public URL segment encoding;
- Check with zero, partial, complete, changed, metadata-missing, and missing-local sets;
- Upload skips synced, uploads missing/changed, rejects all writes for any missing local file, and verifies each successful PUT;
- concurrency reaches four and never exceeds four;
- phase barrier proves no PUT begins before all preflights finish;
- cancellation before semaphore acquisition, during hash, HEAD, PUT, signed verify, and public verify;
- partial remote/local errors, deterministic row order, monotonic counters, terminal outcome, timestamps.

### Desktop/background manager/bridge

- policy uses existing atomic `ReturnExistingByKey`; concurrent different keys reject before registry insertion and exact duplicates reuse one context;
- concurrent duplicate Starts prove exactly one accepted context, one worker, and deterministic disposal of every provisional context that was not accepted;
- exact Book+action duplicate joins;
- same Book/different action and other Book reject without queue entry;
- `Cancelling` still blocks a new Book;
- bridge rejects unknown action/missing Book ID and exposes active owner safely;
- all payloads and failures are credential-free.
- operation-context cleanup covers completed, failed, queued-cancelled, running-cancelled, and manager-dispose paths;
- shutdown/restart waits for S3 child tasks and timeout/force-close behavior is explicit.

### Infrastructure

- DPAPI round trip, atomic replacement, corrupt/version-unsupported envelope, no plaintext, and independence of configuration Save versus credential Replace;
- client config has region, `ForcePathStyle`, `UseDualstackEndpoint`;
- paged `ListObjectsV2`, 404 HEAD, metadata casing/absence, request MIME/ACL/hash/length;
- AWS exception mapping including `AccessControlListNotSupported`, wrong region, access denied, bucket missing, and service/network errors;
- public HEAD 2xx/non-2xx/timeout/cancellation.
- AWS ErrorCode-first mapping, finite SDK retry without outer multiplication, expected public content length, and empty-prefix success;
- sparse staging path containment, reparse-point defense, disk-capacity failure, partial cleanup, and Cache Cleanup coexistence;
- Running receipt checkpoints, Interrupted conversion, Unknown in-flight row, corrupt/version-unsupported recovery.

### Frontend

- Configuration fieldset/legend pattern and secret inputs;
- Book Settings fieldset, exact seven rows, disabled/loading/cancelling states, active-other-Book state;
- Check enabled with missing locals, Upload disabled;
- no S3 top-level route or multi-Book action grid;
- one poll timer, no double-start, no result loss after rerender/Book switch;
- poll patches only the S3 subtree and preserves unrelated draft/focus/caret/scroll;
- three selected-Book modes never mix active owner and historical receipt;
- wide matrix, `<900px` fact stack, `<680px` full-width actions, with no nested vertical scroll;
- accessible progress, visible disabled reasons, status/one-shot alert behavior, button labels, and keyboard focus behavior.

### Validation commands

```powershell
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --filter S3Storage
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --filter S3Storage
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --filter "S3Storage|BackgroundTaskManager"
npm --prefix src/PrintableBook.Desktop/Frontend run test:ui
dotnet test PrintableBook.sln
dotnet build PrintableBook.sln -c Release
```

## Manual smoke matrix

1. Save config plus credentials; restart; verify masked status and no secret reflection.
2. Select a Book without ASIN: actions disabled with recovery text.
3. Select a valid Book with one missing file: Check completes with seven rows; Upload remains disabled/zero PUT.
4. Start Upload on Book A, switch to Book B: Book B names Book A as active and cannot start another S3 action.
5. While staging Book A, confirm same-Book output production is rejected but Book B production and Cache Cleanup remain available.
6. Cancel while four file requests are blocked; confirm waiters do not start and UI reaches Cancelled.
7. Upload a complete Book; observe four concurrent file operations, all seven verified, public URLs HTTP 200.
8. Upload again; all seven are skipped/synced and no PUT occurs.
9. Change one local output; exactly one PUT occurs and its metadata/public URL verify.
10. Use an ACL-disabled bucket; verify `s3_public_acl_unsupported` guidance and no private fallback.
11. Cancel after one PUT and confirm the UI/receipt warns about a partially updated remote set; retry repairs it.
12. Close or trigger app-update restart during a blocked PUT; confirm the warning, cancel/drain path, timeout choice, and Interrupted recovery after forced exit.
13. Run Cache Cleanup while Upload uses staging; confirm neither operation deletes/corrupts the other's files.

## Rollout and rollback

- This remains an unmerged MVP branch; no forward migration from `storage.json` is required.
- Keep commits phase-scoped so the prior branch commit can be compared or reverted cleanly.
- Do not remove the old route/store until the replacement UI and credential tests are green in the same branch.
- Rollback is code-only; remote objects are never deleted. Reverting to the previous branch implementation does not understand `settings.json + s3.credentials.dat`, so credentials must be entered again—do not retain a duplicate legacy credential file.

## NOT in scope

- multi-Book upload queue, bulk selection, or configurable Book concurrency;
- configurable per-file concurrency; four is a fixed product invariant for this phase;
- remote deletion or reconciliation of extra objects;
- multipart/resumable upload journal across app restarts;
- bucket creation, folder creation, IAM automation, CloudFront/CDN, presigned private delivery;
- automatic fallback from `public-read` to private objects;
- S3-compatible non-AWS endpoints or arbitrary endpoint override;
- long-term upload history/analytics beyond one latest secret-free receipt per Book.
- cross-process/multi-instance upload and output-writer coordination; the single-Book invariant is guaranteed within one running desktop process.

## Dream-state delta

```text
CURRENT
separate multi-Book Storage page, wrong manifest, 2 Books × sequential files
    ↓
THIS PLAN
global config + selected-Book control, 1 Book × max 4 files, verified/idempotent publish
    ↓
12-MONTH IDEAL
versioned artifact manifest, resumable multipart transfers, private/CDN delivery,
durable audit history, policy-driven storage providers, health/usage telemetry
```

The plan deliberately stops before the 12-month storage platform. It delivers a bounded, recoverable stable-key publisher, while explicitly avoiding a false claim that seven independent S3 PUTs are atomically visible.

## Decision Audit Trail

| # | Phase | Decision | Chosen | Principle | Rationale |
|---|---|---|---|---|---|
| 1 | Intake | Concurrency hierarchy | one Book globally; four files internally | user-locked premise | prevents multi-Book contention while using available network parallelism inside the selected Book |
| 2 | CEO | Implementation approach | contract-first hardening | completeness over patch size | the existing slice is unshipped and its persistence, manifest, endpoint, task, and UI contracts diverge together |
| 3 | Eng | Second Book behavior | reject immediately, never queue | explicit over clever | “one Book at a time” is observable and cannot surprise the user later |
| 4 | Eng | Upload structure | full preflight barrier then PUT phase | failure safety | guarantees a missing local file cannot produce a partial upload started by this run |
| 5 | Eng | Concurrency owner | one `SemaphoreSlim(4, 4)` | single source of truth | avoids nested parallelism and makes max-active behavior directly testable |
| 6 | Design | S3 surface | global Configuration + selected Book Settings | consistency with product hierarchy | configuration belongs globally; actions/results belong to the Book being edited |
| 7 | CEO | Seven-file mismatch | keep seven mandatory for S3 and add a shared readiness/package contract | honor explicit MVP contract | current producers may warn/omit previews, so S3 must surface missing artifacts rather than claim the pipeline already guarantees them |
| 8 | CEO | Remote atomicity | keep stable public keys; document/repair partial publication | honor exact URL contract | versioned release prefixes would change the requested public path and exceed this MVP |
| 9 | CEO | File immutability | sparse-stage available files; require all seven before PUT | failure safety | Check keeps diagnostic value for missing files while Upload hashes and sends the same immutable bytes |
| 10 | CEO | Cross-feature exclusion | same-Book output lease; no global Production/Cleanup block | minimize operational friction | unrelated Books and cache files do not share the staged package resource |
| 11 | CEO | Settings commit boundary | separate page Save from Replace credentials | explicit over clever | avoids pretending two independent files can be atomically committed together |
| 12 | CEO/Eng | Scope of “app-wide” | one running desktop process | right-sized complexity | the user locked one Book in the app, not multi-instance orchestration; process-wide admission is explicit and testable |
| 13 | Design | Polling update | patch only stable S3 subtree | preserve user work | full Book drawer replacement destroys focus, caret, scroll, and unsaved drafts |
| 14 | Design | Active-operation visibility | compact shell S3 chip | keep status discoverable | a Book-scoped group alone disappears when the user changes route or selected Book |
| 15 | Design | Selected Book states | separate live owner, blocked owner, and latest receipt | one job per state | avoids presenting another Book's live task as if it belongs to the selected Book |
| 16 | Design | Artifact rows | seven-row responsive matrix with no inner scroll | dense but readable | seven bounded rows fit the existing Book scroll and raw URLs add noise |
| 17 | Design | Cancel wording | Stop publishing + partial-publication warning + Retry | truthful recovery | cancelling stable-key PUTs may leave some files public and must not imply rollback |
| 18 | Eng | Admission | existing atomic `ReturnExistingByKey` | reuse before rebuild | it already joins exact keys and rejects another active key before registry insertion |
| 19 | Eng | Missing local Check | sparse staging + full remote comparison | preserve diagnostic value | Check must report all seven rows even when Upload must remain zero-PUT |
| 20 | Eng | Staging root | dedicated contained app-root staging | failure isolation | Book temporary output is actively deleted by Cache Cleanup |
| 21 | Eng | Crash recovery | Running/per-file atomic latest receipt | systems over heroes | terminal-only state cannot explain an interrupted PUT after restart |
| 22 | Eng | Shutdown | cancel, drain, timeout, explicit Force close | failure safety | closing or update restart must not silently abandon child uploads |
| 23 | Eng | Sensitive task state | opaque operation-context ID | least exposure | captures an immutable revision without retaining raw credentials in task history |
| 24 | Eng | Duplicate Start context ownership | manager admission returns `WasCreated` | failure safety | exact joins and conflicts stay atomic while unused provisional credential contexts are destroyed deterministically |

## Autoplan operational notes

- The repository plan, restore point, three phase task artifacts, and Engineering test-plan artifact were written successfully.
- The local skill package does not contain the referenced `gstack-review-log` or `gstack-review-read` helper binaries, so no review-dashboard JSONL was written; the terminal review report below is the authoritative review record.
- No production source file, project file, dependency, or test fixture was changed during this planning run.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---:|---|---|
| CEO Review | `/plan-ceo-review` via `/autoplan` | Product boundary and failure truthfulness | 1 | CLEAR AFTER REVISION | Retained stable URLs; added immutable staging, strict seven-artifact readiness, partial-publication disclosure, latest receipt, and separate credential replacement |
| Design Review | `/plan-design-review` via `/autoplan` | Book-scoped S3 interaction and state design | 1 | CLEAR AFTER REVISION | Added three exclusive selected-Book modes, truthful phase language, responsive seven-row matrix, accessible cancellation/retry, and targeted polling without drawer rerender |
| Engineering Review | `/plan-eng-review` via `/autoplan` | Concurrency, persistence, shutdown, and testability | 1 | CLEAR AFTER REVISION | Reused atomic manager admission; added sparse isolated staging, same-Book output lease, opaque immutable contexts, interrupted receipts, precise AWS errors, and shutdown drain semantics |
| DX Review | `/plan-devex-review` applicability gate via `/autoplan` | Public developer onboarding/API/CLI experience | 1 | NOT APPLICABLE | The feature is an end-user desktop workflow with no public API, CLI, SDK, package, or developer onboarding surface; developer TTHW scoring would be misleading |

**CODEX:** The first-pass branch is not safe to extend in place without contract hardening. The revised plan makes task admission atomic, stages immutable bytes outside cleanup roots, guarantees zero PUT before full preflight, limits all per-file work with exactly one four-slot gate, and preserves truthful recovery evidence across cancellation or crash.

**CROSS-MODEL:** CEO, Design, and Engineering voices converged on the same boundary: one active Book per desktop process, seven fixed stable keys, non-atomic remote visibility disclosed to the user, and recovery by fresh preflight rather than a hidden resume queue. The independent Engineering voice proposed cross-process locking; that expansion was deliberately rejected because the user locked app-level behavior and multi-instance coordination is outside this MVP.

**VERDICT:** CEO + DESIGN + ENG CLEARED. DX was executed and correctly exited at its applicability gate. The plan is coherent and ready for phased implementation; this autoplan run does not implement source code.

NO UNRESOLVED DECISIONS
