<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\feat-interior-frame-no-auto-autoplan-restore-20260923-192452.md -->
# Process Interior Pages-Only — Reviewed MVP Plan

Status: Reviewed, ready for implementation approval  
Date: 2026-09-23  
Current branch reviewed: `feat/interior-frame-no-auto` at `78e5651`  
Scope of this phase: planning and review only; no application code changes

## Outcome

Keep the existing `Process Interior` name, but change its terminal responsibility:

> `Process Interior` prepares and validates the current Intro and active Interior pages as final-size rasters, verifies the optional Background, updates the processed-page preview state, and stops without creating or replacing any PDF.

`Build Final Interior` becomes the only action that assembles, exports, validates, and atomically publishes the final Interior PDF and its thumbnail PDF.

## Product Contract

### Process Interior

```text
validate assigned Book + Brand inputs
→ process selected/automatic Intro pages
→ process active Interior pages with saved Frame / No Frame modes
→ maintain the existing deterministic shuffle map
→ logically assemble and validate final raster sizes + optional Background
→ commit the normal Interior preview manifest
→ complete as pages prepared
→ STOP
```

It must not call the Interior PDF exporter or output publisher, and it must not create, delete, replace, relabel, or touch the provenance of an existing Interior PDF.

### Build Final Interior

```text
validate current Book + Brand + Production inputs
→ run/reconcile the same Intro and Interior page preparation
→ process Interior Cover + Book Owner
→ shuffle + logical assembly
→ export main Interior PDF + thumbnail PDF
→ validate + atomically publish
→ record Production provenance and canonical input signature
```

It remains self-contained: a user can run `Build Final Interior` successfully without ever running `Process Interior` first. A prior pages-only run is only a cache warm-up and preview aid.

## Premises Reviewed

| Premise | Verdict | Reason |
|---|---|---|
| The button name can remain `Process Interior` | Accepted | The existing product vocabulary is established; mode-specific explanatory copy removes ambiguity without a rename/migration. |
| Process Interior no longer needs to produce a Base PDF | Accepted | Build Final Interior already owns the production artifact and reprocesses current sources rather than trusting an old PDF. |
| Existing final raster/cache stages can be reused | Accepted | `DiskBackedInteriorPagePipeline` already produces final pages and validates cache inputs. |
| Background needs a new processed copy | Rejected | `background.png` is already a canonical final-size raster; readiness/size validation is sufficient. |
| Process Interior should stop before all ordering work | Rejected | Keeping the current shuffle and logical assembly is the smallest compatible change and reuses Background/final-size validation; it still performs no PDF work. |
| Build Final may require a prior Process Interior run | Rejected | This would create hidden workflow coupling and break the current self-contained production contract. |

Premise gate result: the user's stated problem and requested direction are valid. No user challenge was raised.

## What Already Exists

| Need | Existing implementation to reuse | Planned change |
|---|---|---|
| Final raster preparation | `DiskBackedInteriorPagePipeline` | Reuse unchanged; no second image pipeline. |
| Bounded page work and cancellation | `BoundedInteriorPageBatchProcessor` and `BookPageConcurrencyController` | Reuse unchanged. |
| Intro policies | `InteriorPageProcessingKind.IntroTemplate` / `BrandIntroTemplate` | Preserve custom transform/cache and final-size Brand Intro pass-through. |
| Frame / No Frame | `FrameMode` and per-page requests | Preserve saved modes and cache invalidation. |
| Stable random order | `JsonInteriorShuffleStore` and `InteriorShuffleIndexGenerator` | Keep the existing map generation/reuse in pages-only runs. |
| Final-size and Background validation | `OrderedBookAssembler` | Continue logical assembly for validation; do not export the result. |
| Production PDF generation | `PdfSharpPrintableBookPdfExporter` | Reach only from `ProductionInterior` and legacy `FullBook`. |
| Atomic PDF replacement | `ValidatedBookOutputPublisher` | Keep unchanged; never call it from `InteriorOnly`. |
| Process scheduling | `ProcessingSessionWorker` and `BackgroundTaskManager` | Preserve queueing/locking; project the mode into the session view. |
| Processed page gallery | `PublishedInteriorPreviews` → `BookDesktopSummary.InteriorPages` | Keep serialized data compatible; clarify it as processed preview state. |
| Production output metadata | `ProductionWorkspaceState.InteriorOutput` | Extend freshness calculation to compare the complete effective recipe. |

## Current and Target Architecture

### Current

```text
                    ┌─ InteriorOnly ────────┐
scan → prepare pages → shuffle → assembly ──┼→ Interior PDF export
                    └─ ProductionInterior ──┘→ publish same output filename
                                                   ↓
                                        Base or Production provenance
```

### Target

```text
                                   ┌─ InteriorOnly
scan → prepare pages → shuffle → validate assembly ┤
                                   │   ├─ commit processed previews
                                   │   └─ CompletePreparation (no PDF)
                                   │
                                   └─ ProductionInterior
                                       ├─ process Production prefix
                                       ├─ PDF + thumbnail export
                                       ├─ atomic publication
                                       └─ Production provenance/signature
```

### Twelve-month ideal delta

This phase establishes one clear artifact owner and a reusable preparation boundary. It intentionally does not add versioned raster runs, output history, or a generalized workflow engine. If preview history or concurrent per-Book processing is needed later, run-versioned/content-addressed prepared artifacts would be the next architectural step.

## Detailed Technical Design

### 1. Preserve external mode contracts

- Keep `BookProcessingMode.InteriorOnly = 1` and `ProductionInterior = 2` unchanged.
- Keep bridge payload `mode: "interior-only"` and the button label `Process Interior` unchanged.
- Do not introduce a migration or rename serialized state solely for terminology.
- Keep `InteriorOutputKind.Base` readable for legacy state/PDFs, but never write a new Base Interior PDF from `Process Interior` after cutover.

### 2. Establish a named page-preparation boundary

Refactor only enough of `WorkspaceBookProcessingQueueBookProcessor.ProcessBookAsync` to make the shared preparation sequence readable and single-sourced. Prefer a private/internal `PrepareInteriorPagesAsync` result over a new public framework or interface.

The result should carry only the facts later stages need:

- active Interior source identities;
- Intro processing results;
- normal Interior processing results;
- current shuffle map;
- logically assembled/validated raster references;
- whether Background was enabled and validated;
- Production prefix results only when the caller is `ProductionInterior`.

The shared stage continues to use the existing concurrency controller and page pipeline. No duplicate image transformations are allowed.

### 3. Add the pages-only completion branch

After logical assembly succeeds:

- if mode is `InteriorOnly`, record processed previews for normal Interior pages only;
- mark the processing state `Completed`;
- return an explicit `CompletedPreparation(BookId)` result;
- do not invoke `ExportInteriorAsync`, `PublishInteriorAsync`, `RecordPublishedInterior`, or `RecordProductionInteriorStateAsync`;
- do not modify the current Interior PDF thumbnail reference.

`BookProcessingQueueBookResult.CompletedPreparation` should use the existing result record with both published-output properties `null`. Do not create a result hierarchy solely for a no-artifact success.

### 4. Background contract

When `HasBackground = true`:

- `ProcessingSessionWorker` resolves the assigned Brand's certified `background.png`;
- Brand validation must still be current;
- `OrderedBookAssembler` verifies existence, readability, and exact final raster size;
- the Background remains a source reference in the logical recipe;
- no `background_processed.png`, cache entry, or preview file is created.

When `HasBackground = false`, the Background is not required, resolved, or validated.

### 5. Preview state and partial failure

Keep `PublishedInteriorPreviews` and its serialized JSON shape for backward compatibility, but update comments/API naming so it represents the latest usable processed Interior preview set rather than proof that a PDF was published. Add an alias such as `RecordProcessedInteriorPreviews`; avoid a duplicate state field.

The fixed processed paths can be rewritten page-by-page before a batch finishes. Therefore the safe MVP rule is:

- preflight failure before page preparation begins: leave the previous preview manifest unchanged;
- successful run: replace the complete manifest once, after assembly validation;
- failure or cancellation after Intro/Interior processing begins: clear the preview manifest so UI never presents a mixed old/new set;
- always preserve published PDF artifacts and provenance.

Run-versioned raster directories would allow true rollback of old previews, but are intentionally deferred.

### 6. PDF ownership and provenance

For an `InteriorOnly` run, all of the following must remain byte-for-byte/state-for-state unchanged:

- `<BookId> - Interior.pdf`;
- `<BookId> - Interior_thumbnail.pdf`;
- `PublishedArtifactReferences` entries for Interior;
- `PublishedInteriorKind`;
- `PublishedInteriorAtUtc`;
- `PublishedInteriorPreviewReference`;
- `ProductionWorkspaceState.InteriorOutput`.

A failed/cancelled pages-only run follows the same preservation rule.

### 7. Canonical Production freshness

The current implementation stores an input signature when publishing, but the Desktop snapshot only checks the two Production prefix assets. Extract one deterministic Production Interior signature calculator and use it both when publishing and when projecting `Processed`/`Stale`.

The canonical signature must include:

- Interior Cover and Book Owner source metadata;
- ordered Intro selection and source metadata;
- active Interior source identities and metadata;
- per-page Frame / No Frame mode;
- frame content metadata when at least one active page uses Frame;
- Background enabled/disabled plus source metadata when enabled;
- shuffle seed and output order;
- final raster geometry, density, artwork normalization, border detection, and rendering-policy/schema version.

It must exclude non-output facts:

- `BookProcessingMode`;
- maximum concurrency;
- Cover PDF geometry;
- UI/session timestamps.

Use a versioned signature prefix. Existing older signatures that cannot prove the full recipe should be shown as `Stale` until the next successful Build Final Interior; do not silently call them current.

Snapshot freshness rules:

| Condition | Status |
|---|---|
| Production inputs missing | `Missing` |
| No Production Interior output record | `Ready to process` |
| Stored signature is legacy/incomplete | `Stale` |
| Current canonical signature differs | `Stale` |
| Current signature matches and required artifacts exist | `Processed` |

`Process Interior` does not directly mutate Production state merely to force staleness. The snapshot recomputes freshness from current effective inputs.

### 8. Cache cleanup compatibility

Today cleanup skips a Book with no published artifacts. After this change, a valid pages-only Book may have processed previews/cache but no PDF.

Update cleanup eligibility so a completed Book with a non-empty processed-preview manifest can be cleaned even when `PublishedArtifactReferences` is empty. After successful cleanup, clear the preview manifest; never delete or rewrite an existing PDF record/output as part of this change.

## UI Contract

This phase does not redesign the Book detail layout and does not add Intro/Background cards to the `Interior pages` gallery. Intro remains visible in Interior settings; Background remains a readiness/source concern. The gallery continues to show normal Interior pages only.

### Mode-aware session state

Add processing mode to `ProcessSessionSnapshot` so polling/refresh can distinguish a pages-only run from a Production build.

| State | Process Interior | Build Final Interior |
|---|---|---|
| Header | `Process Interior` | `Build Final Interior` |
| Description | `Prepare Interior pages for preview. This does not build or replace a PDF.` | Existing final-build description |
| Stages | `Preparing → Intro pages → Interior pages → Validating pages → Saving previews` | `Preparing → Processing pages → PDF export → Publishing` |
| Success | `Interior pages prepared. Existing PDF unchanged.` | `Final Interior PDF ready.` |
| Cancelled after processing starts | `Processing cancelled. Processed previews were cleared; existing PDF was kept.` | Existing final-build recovery copy |

### Busy and disabled behavior

- While pages-only processing is active, disable Build Final because both actions share the processing lane.
- Keep its label `Build Final Interior`; do not show `Building…` for another mode.
- Expose the disabled reason as visible/described text: `Process Interior is running.`
- Show `Building…` only for an active `ProductionInterior` session, including after refresh/restart.
- Keep incremental panel updates; do not reintroduce full drawer redraws.

### Copy changes

- Replace `Process Interior can replace it later.` with `Only Build Final Interior replaces the current Interior PDF. Process Interior refreshes processed-page previews only.`
- Change the PDF Library empty guidance from `Process a Book...` to `Build a Cover PDF or Final Interior PDF to make it appear here.`
- Ensure pages-only progress, errors, and fixtures never say `PDF export` or imply that a PDF was created.
- Keep current PDF kind, build timestamp, open/reveal/copy actions, and stale warning visible after a pages-only run.

### Accessibility

- Announce mode and stage changes through the existing live status regions.
- Preserve `aria-busy` only on the action actually running.
- Disabled conflicting actions must have an accessible reason, not a title-only explanation.
- Failure summary should receive focus after a user-triggered run fails.

## Error and Rescue Registry

| Failure | Detection point | User message / rescue | Preview state | PDF state |
|---|---|---|---|---|
| Book unassigned or assignment invalid | fresh snapshot preflight | Assign/reselect a matching Brand, then retry | unchanged | unchanged |
| Brand validation stale/invalid | worker preflight | Validate the assigned Brand, then retry | unchanged | unchanged |
| Intro selection missing/unreadable | worker or Intro batch | Select a valid Intro source/template, then retry | clear only if processing had begun | unchanged |
| Frame missing/invalid for a Frame page | frame staging/page batch | Validate Brand frame or switch the page to No Frame | clear if processing had begun | unchanged |
| Interior page processing fails | page batch | Identify Book/page/stage; fix source and retry | cleared | unchanged |
| Background missing/wrong size | logical assembly | Validate Brand Background or disable Background | cleared | unchanged |
| Cancellation during page work | batch cancellation | Retry; valid cache may be reused | cleared | unchanged |
| Preview state save fails | state store | Retry; persisted state remains authoritative | previous persisted state | unchanged |
| Production export/publish fails | Build Final only | Previous final PDF remains; retry Build Final | unchanged unless final build fully succeeds | previous PDF unchanged |
| Signature cannot be recomputed | snapshot projection | Show `Stale`, never `Processed`; rebuild to refresh | unchanged | remains openable |

All surfaced errors must communicate problem, cause, and corrective action without requiring the user to inspect absolute filesystem paths.

## Failure Modes Registry

| Failure mode | Severity | Mitigation in this plan | Critical gap after plan? |
|---|---:|---|---|
| InteriorOnly accidentally reaches exporter/publisher | Critical | Explicit branch after assembly plus zero-call integration test | No |
| Existing Production PDF is overwritten or relabelled Base | Critical | Preserve all PDF/provenance fields and byte/hash/mtime assertions | No |
| Partial page batch is presented as a complete old manifest | High | Clear manifest after post-start fail/cancel | No for MVP |
| Production status says Processed after an effective input changed | High | Shared versioned canonical signature used by publish and snapshot | No |
| Background is needlessly copied/transformed | Medium | Validate canonical source only | No |
| Build Final becomes dependent on Process Interior | High | Direct-build E2E contract | No |
| Pages-only cache cannot be cleaned without a PDF | Medium | Extend cleanup eligibility to preview-only completed Books | No |
| Legacy state stops loading | High | Preserve enum values, record shape, legacy `Base`, tolerant optional fields | No |
| Session says `Building…` during pages-only work | Medium | Project mode and render mode-aware labels | No |
| Old signature is treated as trustworthy | High | Mark legacy/incomplete signatures Stale until rebuild | No |

## Implementation Phases

Each phase should be a separate, clearly named commit when implementation is authorized.

### Phase 1 — Lock contracts and state semantics

Files:

- `src/PrintableBook.Core/Application/Processing/BookProcessingQueueProcessor.cs`
- `src/PrintableBook.Core/Domain/Processing/BookProcessingState.cs`
- `tests/PrintableBook.Core.Tests/Processing/BookProcessingStateTests.cs`
- `tests/PrintableBook.Core.Tests/Processing/BookProcessingQueueProcessorTests.cs`

Tasks:

- Add `CompletedPreparation(BookId)` with no published artifact.
- Add the processed-preview method alias/comment without changing serialized shape.
- Define helper behavior for clearing previews after post-start failure/cancellation.
- Preserve legacy enum values and state defaults.

Exit criteria:

- Old state fixtures load unchanged.
- A pages-only success can be represented without a fake PDF output.
- State tests prove PDF/provenance fields are untouched by preview-only mutations.

Suggested commit: `feat(processing): add pages-only completion contract`

### Phase 2 — Split Process Interior from PDF publication

Files:

- `src/PrintableBook.Core/Application/Processing/WorkspaceBookProcessingQueueBookProcessor.cs`
- focused processor/E2E test doubles and tests

Tasks:

- Extract the smallest named shared preparation method/result.
- Keep Intro, active Interior, shuffle, and logical assembly validation.
- Branch `InteriorOnly` after assembly validation.
- Commit the complete normal-Interior preview manifest and return preparation success.
- Clear preview manifest on post-start failure/cancel.
- Keep `ProductionInterior` and `FullBook` export/publication paths intact.

Exit criteria:

- Exporter and publisher spy call counts are zero for `InteriorOnly`.
- Intro/Interior final rasters and preview manifest exist after success.
- No new Interior PDF/thumbnail appears for a Book without one.
- Existing PDF bytes, mtimes, thumbnail, and provenance remain unchanged.

Suggested commit: `feat(processing): stop Process Interior before PDF export`

### Phase 3 — Make Production freshness authoritative

Files:

- `src/PrintableBook.Core/Application/Processing/WorkspaceBookProcessingQueueBookProcessor.cs`
- `src/PrintableBook.Core/Application/Production/ProductionWorkspaceState.cs`
- `src/PrintableBook.Core/Application/Desktop/IApplicationSnapshotService.cs`
- new small pure signature calculator in `PrintableBook.Core/Application/Production/`
- related Core/Infrastructure tests

Tasks:

- Extract and version the canonical Production Interior recipe signature.
- Include all output-affecting inputs and exclude execution-only facts.
- Save the signature after a successful Production publish.
- Recompute and compare it during snapshot projection.
- Mark legacy/incomplete signatures Stale until rebuilt.

Exit criteria:

- Each input class independently transitions Processed → Stale.
- An identical recipe remains Processed.
- A pages-only run with unchanged inputs does not mark Production stale merely because it ran.

Suggested commit: `fix(production): verify full Interior output freshness`

### Phase 4 — Project mode-aware processing UX

Files:

- `src/PrintableBook.Core/Application/Desktop/IProcessSessionService.cs`
- `src/PrintableBook.Core/Application/BackgroundTasks/Workers/ProcessingSessionWorker.cs`
- `src/PrintableBook.Desktop/Frontend/js/app.js`
- `tests/PrintableBook.Desktop.Tests/BridgeMessageContractTests.cs`
- `tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs`

Tasks:

- Carry the processing mode in the session snapshot/view.
- Render mode-specific stages, terminal messages, and busy labels.
- Fix Production and PDF Library ownership copy.
- Preserve incremental UI update behavior and accessibility states.

Exit criteria:

- Pages-only UI never announces PDF export or `Building…`.
- Production builds still announce export/publish correctly.
- Refreshing during an active session restores the correct mode-specific UI.

Suggested commit: `fix(ui): clarify Process Interior pages-only behavior`

### Phase 5 — Support preview-only cache cleanup

Files:

- `src/PrintableBook.Core/Application/BackgroundTasks/Workers/CacheCleanupWorker.cs`
- `tests/PrintableBook.Core.Tests/Application/BackgroundTasks/CacheCleanupWorkerTests.cs`

Tasks:

- Treat a completed Book with processed previews as cleanup-eligible without requiring a PDF.
- Clear the preview manifest after cleanup.
- Preserve published PDF state/output validation where present.

Exit criteria:

- A pages-only Book can release heavy cache.
- Cleanup never deletes or forgets an existing final PDF as a side effect.

Suggested commit: `fix(cache): clean preview-only Interior workspaces`

### Phase 6 — Regression suite and documentation cutover

Files:

- `tests/PrintableBook.Infrastructure.Tests/PrintableBookApplicationEndToEndTests.cs`
- `tests/PrintableBook.Desktop.Tests/BackgroundTasks/ProcessingSessionTaskManagerIntegrationTests.cs`
- `README.md`
- `docs/architecture.md`
- `docs/pdf-engine.md`
- `docs/background-process-session.md`
- `docs/user-guide.md`
- `docs/plans/production-assets-final-pdf.md` (mark obsolete statements as superseded; preserve decision history)

Tasks:

- Replace tests that assert InteriorOnly creates a PDF or overwrites Production with Base.
- Add preservation, direct-build, cache-reuse, failure/cancel, Background, and multi-book coverage.
- Update current docs so only Build Final Interior is described as the Interior PDF owner.
- Search the repository for obsolete behavior promises.

Exit criteria:

- Focused and full suites pass.
- No active UI/user document claims that Process Interior creates or replaces a PDF.
- Historical plan text is clearly marked as superseded where retained.

Suggested commit: `test(docs): lock pages-only Interior processing contract`

## Test Diagram

```text
InteriorOnly
├─ fresh Book
│  ├─ Intro prepared/validated
│  ├─ active Interior final rasters exist
│  ├─ optional Background validated
│  ├─ preview manifest committed
│  └─ no PDF output/result
├─ existing Production PDF
│  ├─ same path/bytes/mtime/thumbnail
│  ├─ same Production provenance/build time
│  └─ freshness depends only on recipe change
├─ failure/cancel after work starts
│  ├─ previews cleared
│  └─ PDF/provenance preserved
└─ multi-Book queue
   ├─ sequential Books
   ├─ per-Book result/failure
   └─ pages-only stages only

ProductionInterior
├─ no prior Process Interior → succeeds
├─ prior Process Interior → valid cache reused
├─ changed input → cache/signature invalidated
├─ export/publish failure → old PDF preserved
└─ success → Production signature/provenance current
```

The full test artifact is stored at:

`C:\Users\admin\.gstack\projects\coloringbook\admin-feat-interior-frame-no-auto-test-plan-20260923-193905.md`

## Verification Commands

```powershell
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
dotnet test PrintableBook.sln
```

Also run a text audit for `Process Interior` near `PDF`, `replace`, `Base`, and `PDF export`, then manually smoke one new Book and one Book with an existing Production PDF.

## Performance and Concurrency

- Preserve the existing maximum page concurrency and one-Book-at-a-time queue.
- Logical assembly reads image metadata but does not load an entire PDF or create duplicate Background pages.
- Removing PDF and thumbnail export makes the pages-only action materially faster and reduces disk I/O.
- Canonical freshness should use existing file metadata/signatures and bounded snapshot concurrency; do not hash all large raster contents on every refresh unless existing metadata cannot establish identity.
- No new watcher, daemon, database, or background queue is needed.

## Compatibility and Rollback

- Existing Books without processed previews or PDFs continue to load.
- Existing Base, Production, and Legacy output provenance remains readable.
- Old Process Interior PDFs remain in the library until Build Final replaces them or the user removes them outside this feature.
- New code never silently deletes a legacy Base PDF.
- Rollback is code-only: no destructive data migration is introduced.
- If the release is rolled back, old code can still read the preserved state fields and outputs.

## NOT in Scope

- Rename `Process Interior` or the serialized `InteriorOnly` mode.
- Require Process Interior before Build Final Interior.
- Create a processed Background copy.
- Add Intro/Background tiles or a redesigned prepared-content gallery.
- Add prepared-preview freshness badges/history.
- Add run-versioned/content-addressed raster directories.
- Add output history or metadata versioning.
- Change Cover PDF workflows or Production asset import.
- Change Frame / No Frame behavior.
- Refactor the whole processor into a workflow framework.
- Implement the existing `PageId` path-containment hardening TODO.

## Alternatives Considered

| Approach | Effort | Risk | Decision |
|---|---:|---:|---|
| Add one early return immediately after page batches | Low | Skips existing Background/final-size validation and changes shuffle timing | Rejected |
| Branch after existing logical assembly | Low | Processor remains large, but behavior delta is small and validation is reused | Selected for MVP |
| Build a new preparation workflow/service graph | High | Broad refactor and migration risk | Rejected |
| Preserve old previews through run-versioned output folders | Medium/High | More storage, cleanup, and migration work | Deferred |
| Keep generating Base PDF but hide it | Low | Does not solve duplicate ownership or wasted work | Rejected |

## Review Synthesis

### CEO/Product review

Score: 9/10 after decisions. The direction is correct and reversible. The key product requirement is one authoritative PDF-producing action, with legacy outputs preserved and no hidden prerequisite before Build Final.

### Design review

Score: 8/10 after decisions. Required fixes are mode-aware progress, correct busy labels, explicit `PDF unchanged` feedback, and removal of misleading PDF ownership copy. The proposed larger Intro/Background preview gallery was declined to hold MVP scope.

### Engineering review

Score: 9/10 after decisions. The lowest-risk boundary is after existing logical assembly. Critical engineering additions are a no-artifact completion result, safe partial-preview handling, full Production recipe freshness, and preview-only cache cleanup.

### DX review

Skipped: this change has no public API, CLI, SDK, installation, or contributor onboarding surface. Internal names and docs are covered by Engineering and Documentation phases.

### Independent voice availability

Independent skill-aligned CEO, Design, and Engineering agents completed read-only reviews. Separate local `codex exec` voices were attempted but degraded because the Windows read-only sandbox/process launch and upstream stream were unstable; no findings from incomplete runs were treated as authoritative.

### Cross-phase themes

- **Single artifact owner:** Product, Design, and Engineering all identified misleading dual PDF ownership as the central problem.
- **Preservation over replacement:** All phases require existing PDFs/provenance to survive pages-only success, failure, and cancellation.
- **Truthful state:** Design and Engineering independently flagged generic progress labels and incomplete Production freshness as trust risks.
- **Hold scope:** Reuse current gallery/layout and processing primitives; add only direct blast-radius fixes.

## Decision Audit Trail

| # | Phase | Decision | Classification | Principle | Rationale | Rejected |
|---:|---|---|---|---|---|---|
| 1 | CEO | Keep `Process Interior` name and bridge mode | Mechanical | Explicit over clever | User explicitly chose the name; explanatory copy is sufficient | Rename/migration |
| 2 | CEO | Make Build Final the only new Interior PDF writer | Mechanical | Completeness | Removes destructive dual ownership and matches user intent | Continue Base PDF publication |
| 3 | Eng | Branch after logical assembly, before PDF export | Taste | Pragmatic + DRY | Reuses shuffle, size, and Background validation with the smallest behavior delta | Stop before shuffle/assembly |
| 4 | Eng | Background is validated source, not derived raster | Mechanical | DRY | It is already canonical final size | `background_processed.png` |
| 5 | Eng | Add `CompletedPreparation` with null outputs | Mechanical | Explicit over clever | Represents success honestly without hierarchy/refactor | Fake PDF output/new result hierarchy |
| 6 | Eng | Clear previews after post-start failure/cancel | Taste | Completeness | Fixed output paths may already contain a partial mixed run | Pretend old manifest remains trustworthy |
| 7 | Eng | Recompute full canonical Production signature | Mechanical | Completeness | Current status ignores multiple output-affecting inputs | Prefix-only freshness/unconditional stale |
| 8 | Design | Project processing mode into session UI | Mechanical | Explicit over clever | Prevents wrong stages and busy labels after refresh | Local transient flag only |
| 9 | Design | Keep normal Interior gallery only | Taste | Pragmatic | Intro/Background preview expansion is not required to solve PDF ownership | New grouped gallery/freshness UI |
| 10 | Eng | Clean preview-only Books without requiring a PDF | Mechanical | Boil lakes | Direct compatibility break in cache cleanup | Leave cache stranded |
| 11 | Docs | Mark prior behavior statements superseded | Mechanical | Completeness | Active documentation must match the cutover without erasing history | Leave contradictory docs |

## Autoplan Decisions Applied

- Chose the complete artifact-preservation contract, including thumbnail/provenance fields, not only the main PDF path.
- Reused the existing assembler for Background/final-size validation instead of creating another validation abstraction.
- Kept serialized modes/state backward compatible and avoided a database or migration.
- Added the direct blast-radius cleanup fix because preview-only Books otherwise become uncleanable.
- Added canonical freshness because a preserved PDF must not be presented as current after recipe changes.
- Rejected a larger preview-gallery redesign and run-versioned raster storage for this MVP.
- Produced a standalone failure-injection test artifact and an implementation sequence with commit boundaries.

## Implementation Task Checklist

- [ ] **P1 / Phase 1 — Contracts:** add pages-only completion and processed-preview state semantics.
- [ ] **P1 / Phase 2 — Boundary:** stop `InteriorOnly` after validated assembly; zero exporter/publisher calls.
- [ ] **P1 / Phase 2 — Preservation:** keep existing PDF, thumbnail, timestamps, provenance, and Production output record unchanged.
- [ ] **P1 / Phase 2 — Recovery:** atomically replace previews on success; clear them after post-start fail/cancel.
- [ ] **P1 / Phase 3 — Freshness:** compute/compare the full versioned Production Interior recipe.
- [ ] **P1 / Phase 4 — UX:** add session mode and truthful stages/messages/busy states.
- [ ] **P2 / Phase 5 — Cleanup:** support completed preview-only workspaces.
- [ ] **P1 / Phase 6 — Tests:** replace old Base-overwrite assertions and add the full preservation/failure matrix.
- [ ] **P2 / Phase 6 — Docs:** make Build Final the sole Interior PDF owner everywhere current behavior is documented.

## GSTACK REVIEW REPORT

| Review | Result |
|---|---|
| CEO / Product | PASS — problem, scope, non-goals, rescue behavior, and six-month trajectory reviewed |
| Design | PASS — mode-aware hierarchy, states, copy, accessibility, and no-redraw behavior specified |
| Engineering | PASS — boundary, state, cache, signature, failure atomicity, performance, and tests specified |
| DX | SKIPPED — no developer-facing product surface |
| Restore point | `C:\Users\admin\.gstack\projects\coloringbook\feat-interior-frame-no-auto-autoplan-restore-20260923-192452.md` |
| Test plan | `C:\Users\admin\.gstack\projects\coloringbook\admin-feat-interior-frame-no-auto-test-plan-20260923-193905.md` |
| External review degradation | Local Codex CLI runs incomplete due Windows sandbox/stream instability; independent review agents completed |
| Deferred scope | Grouped Intro/Background gallery, preview freshness/history, run-versioned rasters, workflow framework |

NO UNRESOLVED DECISIONS
