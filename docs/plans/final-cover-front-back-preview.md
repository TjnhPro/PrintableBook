<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\PrintableBook\feat-book-information-validation-autoplan-restore-20260928-112811.md -->
# Final Cover Front/Back Preview Plan

## Status

- Planning only; no implementation in this phase.
- Product premise approved by the user on 2026-09-28.
- Target action: **Build Cover PDF**.

## Goal

Extend a successful Production **Build Cover PDF** run so the application also creates two lightweight JPEG preview images from the canonical `final_cover.png` spread:

```text
final_cover.png (5242×2626)
├─ crop [0, 2621)    → resize → back_cover.jpg  (1198×1200)
└─ crop [2621, 5242) → resize → front_cover.jpg (1198×1200)
```

The Cover PDF remains the print artifact and continues to use the original `5242×2626` PNG. The two JPEG files are preview-only assets and must never become PDF inputs or print deliverables.

## Approved Product Contract

| Concern | Contract |
|---|---|
| Source | Canonical Production `final_cover.png` only |
| Source dimensions | Exactly `5242×2626` pixels, unchanged from the current import/build contract |
| Split boundary | Back uses `x=[0,2621)`; Front uses `x=[2621,5242)` |
| Full crop dimensions | Each crop is exactly `2621×2626` before resize |
| Persisted previews | `back_cover.jpg` and `front_cover.jpg` only |
| Preview dimensions | Both files are exactly `1198×1200` |
| Format | JPEG in sRGB, opaque white background, metadata stripped |
| JPEG quality | `90` unless implementation evidence shows an existing shared preview-quality constant should be reused |
| Print path | Cover PDF continues to embed the original `final_cover.png`; it must not use either JPEG |
| Intermediate files | No full-size Back/Front JPEGs and no `*_thumbnail` files are published |
| Consumer | User or downstream workflow opens the Book `Output` folder and uses the two files for lightweight preview |
| UI | Reuse the existing task completion/status surface for one warning; no new card, button, PDF Library row or image viewer |
| Trigger | **Build Cover PDF** attempts the two previews after creating the valid Cover PDF candidate |
| Failure policy | The Cover PDF remains successful; panel-preview failure removes both stale JPGs and surfaces one non-blocking warning |

## Expected Output

Inside the existing Book `Output` directory:

```text
<BookId> - Cover.pdf
<BookId> - Cover_thumbnail.pdf
<BookId> - Cover_thumbnail.png
back_cover.jpg
front_cover.jpg
```

Existing Cover output names and behavior remain unchanged. The two new filenames intentionally omit the Book ID because every Book has its own `Output` directory and the short stable names are intended for downstream preview use.

## Current Codebase Evidence

1. `ProductionAssets` defines `final_cover.png` and requires `5242×2626`.
2. `ProductionCoverPdfService.BuildAsync` validates that source, exports the Cover PDF into a temporary directory, publishes it, then records Book and Production state.
3. `PdfSharpPrintableBookPdfExporter.ExportCoverAsync` generates the print PDF, the lightweight Cover preview PDF and the existing combined-cover thumbnail PNG.
4. `ValidatedBookOutputPublisher.PublishCoverAsync` validates the temporary PDF before replacing Book-local output files.
5. Production freshness is derived from `ProductionCoverPdfService.CreateInputSignature`, currently identified by `production-cover-v1`.

## What Already Exists

| Need | Existing path | Decision |
|---|---|---|
| Validate canonical Cover source | `ProductionCoverPdfService` plus `ProductionAssets` already enforce `final_cover.png` at `5242×2626` | Reuse unchanged |
| Create Cover raster companions | `PdfSharpPrintableBookPdfExporter.ExportCoverAsync` already uses ImageMagick for `Cover_thumbnail.png` | Extend this path; do not add a second public generator |
| Publish validated outputs | `ValidatedBookOutputPublisher` already stages replacements with per-file `.pending` files | Reuse per file and explicitly avoid claiming bundle atomicity |
| Report background progress | `ProductionActionWorker` writes `Step`/`Detail`, which survive in `BackgroundTaskSnapshot` and `BackgroundTaskBridgeSnapshot` | Use this existing bridge for the non-blocking warning |
| Show Production feedback | `app.js` already renders `[data-production-feedback]` as `role=status` or `role=alert` without redrawing the Book drawer | Add a warning tone to this element only |
| Verify bridge behavior | `app-bridge.test.mjs` already covers Production task polling and completion without drawer redraw | Extend the existing test instead of adding a new UI harness |

## Architecture Direction

Keep responsibilities explicit:

```text
ProductionCoverPdfService
  ├─ snapshots and validates final_cover.png for this build
  ├─ IPrintableBookPdfExporter
  │    ├─ Cover.pdf + existing Cover preview artifacts
  │    └─ internal Cover raster helper
  │         └─ best-effort back_cover.jpg + front_cover.jpg candidates
  └─ IBookOutputPublisher
       ├─ validates and publishes the authoritative Cover PDF
       ├─ preserves current best-effort Cover preview behavior
       └─ validates/publishes the panel JPEG pair or removes the stale pair
```

### Reuse the existing Cover raster path

Do not add `ICoverPanelPreviewGenerator`, a DI registration or a second source-image pipeline for this MVP. `PdfSharpPrintableBookPdfExporter` already owns the current ImageMagick Cover preview generation and is the only caller that needs the new renditions.

Refactor that existing implementation into an infrastructure-internal helper or private method which can create the combined Cover preview and, only for `ExportCoverAsync`, attempt the two panel JPG candidates. Extend `CoverPdfExportResult` with named optional Back/Front references so roles are explicit. Do not generate panel JPGs from the already-resized `cover_thumbnail.png`.

### Constants

Define the following once in the Production cover contract rather than scattering literals:

```text
Source size:           5242×2626 (reuse ProductionAssets requirement)
Panel crop size:       2621×2626
Split X:               2621
Panel preview size:    1198×1200
JPEG quality:          90
Back filename:         back_cover.jpg
Front filename:        front_cover.jpg
```

## Processing Contract

1. At build start, copy canonical `final_cover.png` to the unique temporary `production-cover` directory, validate that build-local snapshot, and use it for the PDF plus every preview candidate. Retain the captured canonical input signature so a concurrent replacement makes the completed output stale on the next refresh instead of mixing generations.
2. Load that snapshot once through ImageMagick for the Cover-only preview-raster work after the existing source-size validation.
3. Treat raw raster coordinates as canonical. Do not call `AutoOrient()` before the panel crops because the user-approved boundary is defined against the stored `5242×2626` pixel matrix. Add an orientation-metadata regression test.
4. Clone/crop the left rectangle `(x=0, y=0, width=2621, height=2626)`.
5. Clone/crop the right rectangle `(x=2621, y=0, width=2621, height=2626)`.
6. Reset each cropped image page/canvas origin after crop so no virtual offset leaks into encoding.
7. Resize each crop directly in memory to exactly `1198×1200` with aspect-ratio override. The approved target differs minutely from the crop ratio, and exact output dimensions take precedence over padding or additional crop.
8. Convert to sRGB, flatten transparency over white, strip nonessential metadata, encode JPEG at quality `90`.
9. Write only `back_cover.jpg` and `front_cover.jpg` into the temporary `production-cover` output directory.
10. Reopen both candidates with ImageMagick and verify actual JPEG format, exact dimensions, sRGB colorspace, no alpha, and removal of EXIF/XMP/comment metadata. ICC handling follows the chosen sRGB conversion and must be asserted explicitly.
11. Keep existing combined-thumbnail generation and the new panel pair in separate guarded outcomes even though they share one source decode. A failure in either optional companion must not suppress the other.
12. Treat the two panel files as one optional pair: if either candidate fails, delete both candidates and return Unavailable while keeping the PDF candidate valid.

No full-size half-cover file is written to persistent output. Temporary in-memory crops are disposed immediately after encoding.

## Publication and Consistency

Treat both JPEG previews as a best-effort pair attached to an authoritative Cover PDF build:

- Validate and publish the Cover PDF under the current required-output contract.
- Extend `CoverPdfExportResult` and `PublishedCoverOutput` with a named optional panel-preview pair rather than positional strings.
- If both panel candidates are valid, publish `back_cover.jpg` and `front_cover.jpg` using the existing per-file `.pending` replacement pattern.
- If generation, validation or publication of either panel fails, remove both new candidates and best-effort delete both previously published panel JPGs. Never leave a deliberately usable one-sided pair.
- A panel failure must not fail or roll back a valid Cover PDF.
- Return a typed warning/status from Cover publication through `ProductionCoverPdfResult` and `ProductionActionResult`. In `ProductionActionWorker`, copy that warning to the terminal task snapshot as `Detail = "cover_panel_previews_unavailable"` and `Step = <actionable message>`; the frontend does not read `ProductionActionResult` directly.
- Cancellation before the PDF commit publishes nothing new. After the PDF commit, complete state/result finalization with a non-cancellable token and report Completed or Completed-with-warning; never report Cancelled for an already-current PDF.
- Temporary candidates and `.pending` files are cleaned best-effort. Cleanup failure after the PDF commit is diagnostic only and must not turn the successful PDF build into a failed task.

True atomic visibility across stable filenames is not promised. The safety contract is narrower: the print PDF is authoritative, and the two panel JPGs are either both returned as ready or treated as unavailable. A manifest/generation-directory transaction is deferred until a real downstream consumer requires atomic bundle reads.

The phrase "treated as unavailable" describes application state, not a filesystem transaction. If Windows keeps one old JPG locked, best-effort cleanup can physically leave that file behind. The warning remains visible and no result contract may advertise the pair as ready. The user must close the viewer and rebuild; true all-or-nothing visibility requires the deferred manifest/generation design.

## User-visible Interaction Contract

This is an application-UI change only in the sense that an existing status line gains a warning state. There is no new layout, navigation, control or preview surface, so no mockup is required.

| State | Existing control behavior | Feedback text/tone | Accessibility |
|---|---|---|---|
| Queued/running | **Build Cover PDF** is disabled and reads `Working…` | Existing progress `Step`; neutral/info tone | Existing `aria-busy=true`, `role=status` |
| Full success | Controls re-enable after refresh | `Cover PDF built. back_cover.jpg and front_cover.jpg were saved to Output.` | `role=status`; do not move focus |
| PDF success, preview pair unavailable | Controls re-enable and Cover remains Processed | `Preview warning: Cover PDF built, but back_cover.jpg and front_cover.jpg are unavailable. Do not use existing copies until you close any open preview files, check the Output folder permission, and run Build Cover PDF again.`; amber warning tone | `role=alert`; message includes severity, problem, likely causes and recovery; do not rely on color alone |
| Authoritative PDF failure | Existing failure behavior | Existing error message/red tone | `role=alert`; prior output remains |
| Cancelled before PDF commit | Existing cancellation behavior | `Production action cancelled.` | `role=status` |

Implementation detail for the chosen **A** direction:

- Replace the binary `productionFeedbackError` presentation assumption with a small `info | warning | error` tone, or an equivalent additive warning flag, while keeping existing error behavior intact.
- Add `.production-feedback.is-warning` using the established amber palette already used by `.catalog-warning`; do not invent a second alert component.
- Give queued/running feedback a neutral `info` tone; reserve green for terminal success, amber for partial success and red for failure.
- On a terminal `Completed` task, `observeProductionAction` must inspect the stable `Detail` code before overwriting `Step` with the generic completion copy.
- Preserve the chosen warning text through the subsequent application refresh; the existing string replacement of `" Refreshing status…"` must not erase or downgrade it.
- Keep the existing persistent feedback region, add `aria-live="polite"` and `aria-atomic="true"`, and switch to `role=alert` only for warning/error. No focus stealing or automatic folder opening.
- Keep the region in its existing Production-workspace position per decision A. An in-card duplicate/move is rejected for this MVP because it changes layout and creates action-specific rendering logic for a shared Production task surface.
- Use comfortable line-height and a readable text measure; verify wrapping at desktop and the existing `900px` breakpoint.

### Design review scorecard

| Dimension | Score | Review result |
|---|---:|---|
| Information architecture | 8/10 | Reuses the shared Production feedback region chosen in A; action-specific in-card placement is deferred |
| Interaction states | 10/10 | Running, full success, partial success, failure and cancellation are explicit |
| Visual consistency | 9/10 | Reuses the existing feedback box and established amber warning palette |
| Accessibility | 9/10 | Semantic live feedback, text label and no color-only meaning; verify announcement in WebView2 |
| Responsive behavior | 10/10 | No new layout; existing feedback box wraps naturally |
| Content clarity | 10/10 | Message states what succeeded, what failed and how to recover |
| Scope discipline | 10/10 | No new card, button, viewer or PDF Library row |

## Persistence and Freshness

- Do not add the JPEGs to `BookProcessingState.PublishedArtifactReferences`; that collection drives PDF output behavior.
- Do not expose the JPEGs as PDF Library artifacts in this phase.
- `ProductionWorkspaceState` continues to record the Cover PDF as the authoritative build result; do not add the optional JPGs to Cover freshness.
- Keep `production-cover-v1`. Existing processed Covers remain processed because missing optional JPGs do not make the print artifact stale.
- Do not backfill previews at startup. Existing Books load normally; their next explicit Build Cover PDF run creates the JPEGs.
- An older application ignores the extra `.jpg` files without migration.

## Error and Rescue Contract

| Failure | User-visible outcome | Preservation rule | Rescue |
|---|---|---|---|
| Source missing | Build fails with existing missing Final Cover error | Preserve previous Cover outputs and JPEGs | Import Final Cover and retry |
| Source not `5242×2626` | Build fails with expected/actual size | Preserve previous outputs | Replace source with correct dimensions |
| Crop/resize/encode error | Cover PDF succeeds with one panel-preview warning | Delete both panel candidates and stale published pair best-effort | Verify source readability and rebuild Cover |
| Generated JPEG has wrong size/format | Cover PDF succeeds with one panel-preview warning | Candidate pair is not published; stale pair is removed best-effort | Rebuild after fixing generator/source |
| Panel JPG locked or permission denied | Cover PDF succeeds; warning names the unavailable preview pair | Treat both panel previews as unavailable | Close viewer/fix permission and rebuild Cover |
| Cover PDF output locked or permission denied | Build fails under the existing authoritative-output behavior | Preserve existing Cover PDF | Close viewer/fix permission and retry |
| Cancellation before PDF commit | Build reports cancellation | No new outputs become current | Retry Build Cover PDF |
| Cancellation after PDF commit | Build completes, with a companion warning if preview work did not finish | Newly committed PDF and state remain current | Rebuild later if previews are unavailable |
| Cleanup failure after successful publication | Build remains successful but logs cleanup warning | Published artifacts remain current | Clear cache/temp later |

## Compatibility Boundaries

The change must be additive and must not alter:

- Final Cover import validation.
- Cover PDF dimensions, raster source, MediaBox or print quality.
- Existing `<BookId> - Cover_thumbnail.pdf` behavior.
- Existing `<BookId> - Cover_thumbnail.png` used by PDF Library.
- Existing Cover processed/stale status semantics and `production-cover-v1` signature.
- Interior processing/build flows.
- Brand assignment, Book metadata or filtering.
- Existing output discovery for Books without the new JPEG files.

## Implementation Phases

### Phase 1 — Contracts and constants

- Introduce typed Back/Front preview output references.
- Add shared split, size, filename and JPEG-quality constants.
- Extend Cover build/publication request and result contracts additively.
- Add a typed non-blocking panel-preview warning/status through Cover export, publication, service and Production action result contracts.

### Phase 2 — Existing Cover exporter integration

- Refactor the existing Cover preview raster logic just enough to reuse one ImageMagick source load in `ExportCoverAsync`.
- Implement two-crop, direct-resize JPEG generation as an internal helper, without a public service or DI registration.
- Make disposal, cancellation and temporary-file cleanup explicit.
- Validate generated format and exact pixel dimensions.
- Unit-test boundary pixels with a synthetic source whose left/right halves and seam pixels are distinct.

### Phase 3 — Best-effort publication

- Keep PDF export sourced from the byte-identical build-local snapshot of the canonical original PNG; never use either JPEG as PDF input.
- Publish the authoritative PDF using current behavior.
- Publish or invalidate the optional Back/Front pair after the PDF commit.
- Propagate one typed non-blocking warning when the pair is unavailable.

### Phase 4 — Existing task status warning

- Reuse the existing background-task status/completion presentation.
- On success, keep the normal Build Cover PDF completion message.
- In `ProductionActionWorker`, map the typed warning to terminal task `Detail` code `cover_panel_previews_unavailable` plus the approved actionable `Step` message.
- In `app.js`, preserve that terminal warning instead of replacing it with the generic completion copy, and render it with the existing feedback element's amber warning tone.
- Do not add a new card, button or PDF Library interaction.

### Phase 5 — Tests and documentation

- Add unit, publisher and service-level integration coverage.
- Update `docs/pdf-engine.md` with the preview-only JPEG contract.
- Update `docs/user-guide.md` Build Cover PDF output description.
- Record that no UI control consumes the panel previews yet.
- Capture desktop/narrow acceptance screenshots for queued, success, partial-success warning, failure and cancellation states; manually verify the live-region announcement once in WebView2.

## Test Diagram

```text
Build Cover PDF
├─ canonical source replacement mid-build → build-local snapshot regression test
├─ source missing/wrong size ─────────────── existing service tests
├─ PDF export from original PNG ─────────── existing exporter tests + regression assertion
├─ split boundary
│  ├─ lossless crop geometry → exact seam test before JPEG encoding
│  ├─ x=0..2620 → Back ─────────────────── generator pixel-content test
│  └─ x=2621..5241 → Front ─────────────── generator pixel-content test
├─ direct resize to 1198×1200 ───────────── generator dimension test
├─ JPEG encoding/quality/profile ─────────── generator format/readability test
├─ both panel candidates valid ──────────── publisher integration test
├─ successful publish/rebuild ───────────── service end-to-end test
├─ one candidate invalid/missing ────────── pair invalidation + PDF success test
├─ panel publication I/O failure ────────── PDF success + warning + pair cleanup test
├─ warning transport
│  ├─ optional companion failure isolation → exporter cross-outcome tests
│  ├─ typed publication warning → worker Step/Detail ─ worker test
│  └─ terminal snapshot → amber feedback ────── bridge/UI test (no redraw)
├─ cancellation before PDF commit ───────── preserve old outputs test
├─ cancellation after PDF commit ────────── finalize state/result test
└─ state/signature
   ├─ Cover PDF remains authoritative ───── state-store regression test
   └─ optional JPG absence stays non-stale ─ snapshot/freshness regression test
```

### Required assertions

1. `back_cover.jpg` is readable JPEG and exactly `1198×1200`.
2. `front_cover.jpg` is readable JPEG and exactly `1198×1200`.
3. Back contains source columns `0..2620`; Front begins at source column `2621`.
4. No overlap, missing seam column or left/right swap.
   Verify crop geometry before lossy JPEG encoding; use tolerance-based color assertions on the final resized JPEG.
5. No `back_cover_thumbnail.*`, `front_cover_thumbnail.*`, or persistent full-size panel images exist.
6. Cover PDF still embeds/uses the original Final Cover path and retains its existing `17.47×8.75 inch` geometry.
7. Existing combined Cover preview PDF/PNG still publish.
8. A panel-preview failure does not fail or roll back the newly valid Cover PDF, never returns a one-sided pair as ready, and warns if physical cleanup is incomplete.
9. A successful rebuild replaces both panel previews with content from the new source.
10. Existing Book/Production state and Interior artifacts remain unchanged except for the Cover build timestamp/signature already owned by this action.
11. A warning survives `ProductionActionResult → ProductionActionWorker → BackgroundTaskSnapshot → BackgroundTaskBridgeSnapshot → observeProductionAction` and remains visible after the snapshot refresh.
12. Full success, partial success, failure and cancellation use the expected feedback tone/role without a full drawer redraw.
13. A canonical source replacement during the build cannot mix PDF and preview generations; the next refresh marks the completed Cover stale when the source signature changed.
14. A transparent, metadata-bearing source is flattened to white, encoded as sRGB JPEG and stripped of EXIF/XMP/comment data.
15. Existing combined-thumbnail failure and new panel-pair failure are isolated from one another.

## Engineering Review Details

### Exact contract shape

Use named records rather than positional nullable strings:

```text
CoverPanelPreviewPair(BackCover, FrontCover)
CoverCompanionOutcome(Status = Ready | Unavailable, Pair?, WarningCode?)

CoverPdfExportResult
├─ CoverPdf
├─ existing PreviewPdf / thumbnail data
└─ PanelPreviewOutcome

PublishedCoverOutput / ProductionCoverPdfResult / ProductionActionResult
├─ authoritative Cover output
└─ PanelPreviewOutcome
```

`Ready` requires both references; `Unavailable` requires no pair and may carry a stable warning code. Keep raw exceptions in diagnostics and keep end-user English copy at the worker/UI boundary so Infrastructure does not own presentation text.

### Ownership boundaries

- Exporter owns JPEG format and exact-dimension validation because it already owns ImageMagick and the candidate files.
- Publisher checks that both named candidates exist, stages both, publishes them, and returns ready only after both replacements succeed; it does not decode the images a second time.
- Service records the authoritative PDF state whether the optional pair is Ready or Unavailable.
- Worker translates `cover_panel_previews_unavailable` into the approved user message and reports it through task `Step`/`Detail`.
- Frontend interprets only stable task codes and presentation tone; it never infers preview readiness from Cover freshness.

### Exception and cancellation boundaries

- Catch only expected companion failures (`MagickException`, `IOException`, `UnauthorizedAccessException` and failed candidate validation). Do not swallow programming errors or `OperationCanceledException` before the authoritative PDF commit.
- Once the PDF has committed, complete state/result finalization with `CancellationToken.None`; companion cancellation/failure becomes the non-blocking warning outcome. Record the PDF state exactly once and do not retry inside the same run.
- Move temporary-directory deletion after result capture and make it best-effort after commit; diagnostics may record cleanup failure, but the task stays Completed.
- Before replacing either stable JPG, validate both candidates and stage both `.pending` files. If replacement two fails, delete staged files and best-effort delete both stable JPGs; return Unavailable.
- Do not claim that failed cleanup removed a locked file. The warning is the source of truth until the next successful build.

### Expected files to change

| Area | Likely files |
|---|---|
| Core contracts | `IPrintableBookPdfExporter.cs`, `IBookOutputPublisher.cs`, `ProductionCoverPdfService.cs`, `ProductionActionWorker.cs` |
| Raster generation | `PdfSharpPrintableBookPdfExporter.cs` |
| Publication | `ValidatedBookOutputPublisher.cs` |
| Existing status UI | `Frontend/js/app.js`, `Frontend/css/book-workspace.css` |
| Tests | Exporter, publisher, service, worker, bridge snapshot and `app-bridge.test.mjs` test files |
| Docs | `docs/pdf-engine.md`, `docs/user-guide.md` |

This crosses Core, Infrastructure and Desktop because the existing Build Cover PDF flow already crosses those layers, but it introduces no new public service and no DI registration. Implement sequentially: contracts first, exporter second, publisher/service third, task/UI fourth, then documentation and full regression tests. Shared contracts and test fixtures make parallel worktrees more costly than useful.

## Failure Modes Registry

| Codepath | Realistic production failure | Covered by test | Handling | User-visible |
|---|---|---:|---|---:|
| Decode/crop/encode | ImageMagick cannot decode source or allocate image | Yes | Convert expected failure to Unavailable; clean candidates | Yes |
| Pixel geometry | Seam off by one or sides reversed | Yes | Boundary-color regression test blocks ship | Test-time only |
| Candidate validation | Wrong format or `1198×1200` mismatch | Yes | Reject pair; preserve PDF | Yes |
| Pair publication | One destination is locked | Yes | No ready pair; best-effort remove both; actionable warning | Yes |
| Warning propagation | Completed task overwrites warning with generic text | Yes | Stable Detail code + terminal Step mapping | Yes |
| Refresh after completion | UI rerender clears terminal warning | Yes | Preserve feedback during snapshot refresh | Yes |
| Cancellation before commit | User cancels while exporting PDF | Yes | Existing cancellation path; publish nothing new | Yes |
| Cancellation after commit | Cancellation arrives during companions/state save | Yes | Finish state/result; PDF remains current; companion warning | Yes |
| Temp cleanup | Windows retains handle on temp file | Yes | Diagnostic-only after PDF commit | Warning only if previews affected |
| Existing thumbnail | Raster refactor changes combined thumbnail orientation/size | Yes | Regression assertion on current PNG contract | No change expected |

Critical gaps after these additions: **0**. A physically locked stale JPG remains an acknowledged filesystem limitation, but it is not silent and is not advertised as a ready pair.

## Implementation Tasks

- [ ] **T1 (P1, human: ~2h / CC: ~20min)** — Core contracts — Add named preview-pair and companion-outcome result contracts.
  - Surfaced by: Engineering review — warning and readiness must cross existing layers explicitly.
  - Files: Core exporter/publisher/service/action result contracts.
  - Verify: Core unit tests compile and assert valid Ready/Unavailable outcomes.
- [ ] **T2 (P1, human: ~4h / CC: ~40min)** — Cover exporter — Generate, validate and clean the two JPEG candidates from one preview-raster source decode.
  - Surfaced by: Architecture/performance review — avoid a duplicate generator and decode path.
  - Files: `PdfSharpPrintableBookPdfExporter.cs` and exporter tests.
  - Verify: exact size/format, seam, orientation metadata, disposal and existing thumbnail regressions.
- [ ] **T3 (P1, human: ~4h / CC: ~40min)** — Output publisher — Publish the optional pair after the PDF and downgrade expected companion failures to Unavailable.
  - Surfaced by: Failure review — stable filenames cannot be group-atomic and locked files must not fail print output.
  - Files: `ValidatedBookOutputPublisher.cs` and publisher tests.
  - Verify: rebuild, first/second replacement failure, stale-pair cleanup, cancellation boundary and cleanup-directory failure tests.
- [ ] **T4 (P1, human: ~3h / CC: ~30min)** — Background task/UI — Carry terminal warning through snapshot polling and show the approved amber feedback state.
  - Surfaced by: Design/engineering review — frontend cannot consume `ProductionActionResult` directly.
  - Files: `ProductionActionWorker.cs`, `app.js`, `book-workspace.css`, worker/bridge/UI tests.
  - Verify: partial-success message survives refresh, uses `role=alert`, and causes no drawer redraw.
- [ ] **T5 (P2, human: ~2h / CC: ~20min)** — Service and docs — Preserve v1 freshness and document preview-only output behavior.
  - Surfaced by: Compatibility review — optional JPEG absence must not make Cover stale.
  - Files: `ProductionCoverPdfService.cs`, snapshot/state tests, `docs/pdf-engine.md`, `docs/user-guide.md`.
  - Verify: focused service/state tests and full `dotnet test`.

## Performance and Storage Budget

- Decode the `5242×2626` PNG once for the ImageMagick preview-raster group, not once per panel. The PDF library may still read the original independently to embed it in the authoritative PDF.
- Process the two crops sequentially unless profiling proves parallel work beneficial; one decoded image plus one active crop bounds peak memory and keeps behavior predictable.
- Never persist the `2621×2626` intermediates.
- Strip metadata and use JPEG quality `90` to prioritize preview clarity while materially reducing disk usage versus four PNG/full-size artifacts.
- No startup or PDF Library decoding work is added.

## Rollout and Rollback

Rollout requires no state migration. After release, users rebuild a Cover when they need the two previews. For rollback, revert the code; existing JPEG files are harmless untracked output companions and can remain until a later successful rebuild or manual cleanup. Do not delete them automatically during downgrade.

## NOT in Scope

- Using Back/Front JPEGs for print or PDF creation.
- Publishing full-size half-cover images.
- Additional `*_thumbnail` variants.
- UI preview controls, cards or PDF Library rows for these JPEGs.
- Splitting Interior files.
- Custom JPEG quality setting in UI/settings.
- Backfilling every existing Book automatically.
- User-configurable split point or output dimensions.
- Changing the canonical Final Cover format from PNG.
- Manifest/versioned-generation publication for atomic multi-file readers; no current consumer requires it.
- Moving or duplicating Production feedback inside the Final Cover card; decision A keeps the existing shared status region.
- A reusable raster-rendition service or file-operation abstraction; the MVP uses focused internal helpers and existing filesystem integration tests.

## Definition of Done

- When panel-preview generation succeeds, Build Cover PDF creates exactly the two additional preview files at the approved names and dimensions.
- The split is correct at pixel boundary `2621` and Back/Front are not reversed.
- The Cover PDF and existing preview companions remain behaviorally unchanged.
- An authoritative Cover PDF failure or cancellation before the PDF commit preserves the prior PDF; after commit, state finalization completes and companion failure/cancellation does not roll the valid PDF back.
- Existing Books and old workspace state load without migration.
- Panel-preview failure leaves the successful Cover PDF authoritative, removes the stale pair best-effort and emits one actionable warning.
- Automated tests cover image correctness, publication, replacement, pair invalidation and unchanged Cover freshness.
- User and PDF-engine documentation describe the two JPEGs as preview-only.

## Autoplan Review Summary

### Premise challenge and dream-state delta

The feature is worth shipping because the first consumer is explicit: the user opens each Book's `Output` folder and needs lightweight front/back previews without touching the print PDF. The approved half split and `1198×1200` output are product constraints, not inferred print geometry. The plan deliberately rejects making optional previews authoritative because that would block valid print output and force workspace migration.

The dream state would publish immutable output generations behind one atomic manifest and let UI/downstream consumers select a generation. That is unnecessary for this MVP because the files are manually consumed and stable filenames are required. The smallest complete implementation therefore exposes honest best-effort consistency, a clear warning, and deterministic rebuild recovery.

### Cross-phase themes

- **Optional artifacts must not corrupt authoritative success.** CEO, engineering and design reviews independently converged on PDF success plus an actionable companion warning.
- **Use existing paths.** Every phase rejected a new generator/service/UI component in favor of the current exporter, publisher, background-task snapshot and Production feedback region.
- **Be precise about partial success.** Engineering and design both required the warning to survive refresh and explain that stale physical JPGs may remain locked.
- **One build, one source generation.** Engineering added a build-local snapshot so PDF, combined thumbnail and panel previews cannot mix different canonical-file generations.

### Review completion summary

| Phase | Result | Score / evidence |
|---|---|---|
| CEO | Scope reduced and premise accepted | One internal raster path; previews best-effort; no freshness migration |
| Design | Existing status surface retained per A | 66/70 across seven dimensions; no mockup required |
| Engineering | Architecture locked | 0 critical gaps after changes; code/test diagrams and failure registry present |
| DX | Skipped | No public API, CLI, onboarding or developer-facing workflow is introduced |
| Parallelization | Sequential | Shared contracts and overlapping tests make separate worktrees counterproductive |

### Dual-voice consensus

| Topic | Independent voices | Decision |
|---|---|---|
| New public generator | Both rejected it as premature | Reuse exporter-internal raster helper |
| Preview failure policy | Both rejected blocking the PDF | Completed PDF plus typed companion warning |
| Warning transport | Both found `ProductionActionResult` alone invisible to UI | Worker projects warning into existing task `Step`/`Detail` snapshot |
| Pair atomicity | Both found stable filenames cannot be group-atomic | Stage both, return Ready only for both, warn and clean best-effort otherwise |
| Cancellation | Both found pre/post-commit behavior underspecified | Honor before commit; finalize state after commit and report Completed |
| UI placement | Design outside voice preferred in-card placement; user chose A | Keep existing shared Production feedback region for MVP |
| Source race | Engineering outside voice found multi-read generation risk | Use one build-local source snapshot |

## Decision Audit Trail

| # | Phase | Decision | Classification | Principle | Rationale | Rejected |
|---|---|---|---|---|---|---|
| 1 | CEO | Name manual `Output`-folder use as the first consumer | Auto-decided | User outcome first | Makes the artifact useful without inventing UI | Defer until UI consumer exists |
| 2 | CEO | Keep exact user-approved split and size | User-approved | Respect explicit constraints | Geometry is a product contract | Infer spine/bleed geometry |
| 3 | CEO | Make panel previews best-effort | Auto-decided | Preserve authoritative work | A JPEG issue must not block print PDF | Treat JPGs as required build output |
| 4 | CEO | Reuse the existing exporter raster path | Auto-decided | Smallest complete diff | One caller and one existing ImageMagick path | New `ICoverPanelPreviewGenerator` |
| 5 | CEO | Keep `production-cover-v1` and current freshness | Auto-decided | Backward compatibility | Optional JPG absence must not stale old Books | State migration/signature v2 |
| 6 | Engineering | Model pair readiness explicitly | Auto-decided | Explicit over clever | Prevents nullable positional-string ambiguity | Two unrelated nullable paths |
| 7 | Engineering | Admit best-effort physical consistency | Auto-decided | Honest failure contract | Two stable filenames cannot be atomically swapped | Claim group atomicity |
| 8 | Engineering | Separate pre/post-PDF cancellation | Auto-decided | State matches disk | Never report Cancelled after PDF became current | One cancellation policy for all stages |
| 9 | Engineering | Snapshot canonical source per build | Auto-decided | Determinism | Prevents mixed PDF/JPG generations | Reopen mutable canonical path repeatedly |
| 10 | Engineering | Isolate existing thumbnail and panel failure domains | Auto-decided | Additive compatibility | A new optional output cannot suppress the old one | One shared catch/outcome |
| 11 | Design | Keep existing Production feedback region | User choice A | Scope discipline | Adds a warning state without layout churn | Move/duplicate feedback into Cover card |
| 12 | Design | Add neutral, warning and error tones with explicit copy | Auto-decided | Accessible recovery | Color and generic completion text are insufficient | Reuse green/error binary state |
| 13 | DX | Skip developer-experience phase | Auto-decided | Relevance | No developer-facing installation/API/CLI changes | Invent DX work unrelated to feature |

No TODO is added: the atomic manifest/generation design is intentionally documented under NOT in Scope and has no current consumer or urgency.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---:|---|---|
| CEO Review | `/plan-ceo-review` via `/autoplan` | Scope & strategy | 1 | CLEAR | Reduced one-use abstractions; made JPEGs best-effort; kept Cover freshness compatible |
| Codex Review | Outside voices via `/autoplan` | Independent challenge | 2 | CLEAR | Warning transport, pair consistency, cancellation, source race and UI-state findings absorbed |
| Eng Review | `/plan-eng-review` via `/autoplan` | Architecture & tests | 1 | CLEAR | 8 substantive issues resolved; 0 critical gaps remain |
| Design Review | `/plan-design-review` via `/autoplan` | UI/UX gaps | 1 | CLEAR | Seven-state dimensions reviewed; existing surface retained per A; warning/accessibility contract added |
| DX Review | `/plan-devex-review` via `/autoplan` | Developer experience gaps | 0 | SKIPPED | No developer-facing API, CLI, setup or migration scope |

**CODEX:** Two read-only outside-voice passes were absorbed; the rejected in-card placement remains documented as the explicit A decision.

**CROSS-MODEL:** Independent reviewers agreed on reusing the exporter, keeping PDF success authoritative, transporting warnings through task snapshots, and documenting best-effort pair consistency.

**VERDICT:** CEO + DESIGN + ENG CLEARED — ready for implementation after user review.

NO UNRESOLVED DECISIONS
