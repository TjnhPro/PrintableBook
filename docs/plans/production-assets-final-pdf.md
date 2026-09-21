# Production Assets and Final PDF Plan

Status: Reviewed and implementation-ready
Date: 2026-09-21
Base branch: `main`

## Product contract

This feature is additive. The existing **Process Interior** action and its `InteriorOnly` processing contract remain available. A new Production workspace lets the user import three canonical PNG files, process the two Interior prefix pages independently, build the Cover PDF independently, and build the production version of the Interior PDF.

### Brand contract

A valid Brand root must contain:

```text
cover.psd
app_plus.psd
book_owner.psd
```

All three PSD files are existence-only validation targets. They must participate in the Brand fingerprint and be copied with overwrite enabled into `Book/.workspace/templates/`. They must never be decoded as images.

This is the one intentional exception to the otherwise additive rollout: changing the global Brand definition invalidates every previously validated Brand until `book_owner.psd` exists. Release is therefore **asset-first**. Add `book_owner.psd` to every configured Brand and validate both current Brands before deploying the app build. If that prerequisite cannot be met, do not release this contract change; do not silently weaken it to an optional file. The existing Process Interior algorithm remains unchanged, but its existing valid-Brand preflight still applies.

### Workspace contract

```text
Book/.workspace/
├─ generator/                         # reserved for later production tooling
├─ templates/
│  ├─ cover.psd
│  ├─ app_plus.psd
│  └─ book_owner.psd
├─ production/
│  ├─ final_cover.png
│  ├─ interior_cover.png
│  └─ interior_book_owner.png
├─ cache/
│  ├─ production-interior-cover/
│  └─ production-book-owner/
└─ processed/
   └─ production/
      ├─ interior-cover.png
      └─ interior-book-owner.png
```

`generator/` is created as a convention only. This MVP does not read or write generator artifacts.

### Production input rules

| Asset | Canonical filename | Import validation | Processing policy |
|---|---|---|---|
| Final cover | `final_cover.png` | Readable PNG, exactly `5242×2626` px | No crop or resize |
| Interior cover | `interior_cover.png` | Readable PNG; no fixed source dimensions | Existing Interior pipeline, forced No Frame/CropArt |
| Book owner | `interior_book_owner.png` | Readable PNG; no fixed source dimensions | Existing Interior pipeline, forced No Frame/CropArt |

The import command accepts only the Book ID and asset kind. JavaScript never supplies an arbitrary filesystem path. Desktop opens a native file picker, validates the selected PNG, copies it to a temporary sibling file, then atomically replaces the canonical target.

### Cover output

```text
.workspace/production/final_cover.png
→ Output/<BookId> - Cover.pdf
```

The source raster remains `5242×2626` pixels. The PDF page size is exactly `17.47 × 8.75 inch`, or `1257.84 × 630.00 points`. The exporter does not pre-resample or crop the bitmap and does not use PNG DPI metadata; it embeds all source pixels and draws the image over the complete page media box. Because the approved dimensions are rounded, this applies a tiny non-uniform display scale (about `300.06 × 300.11` effective pixels/inch) with no margin. Publication validates one page and the exact rounded physical page size before atomically replacing the prior successful Cover PDF.

### Interior output

There is no `Output/final_interior.pdf`. Both Interior actions publish to the existing pattern:

```text
Output/<BookId> - Interior.pdf
```

**Process Interior** preserves the current order and behavior:

```text
Intro
→ randomized Interior
```

**Build Final Interior** rebuilds from current Book sources, settings, selected Brand, Intro selection, active pages, and shuffle map. It does not prepend to a previously published PDF.

`Process Interior Cover` and `Process Book Owner` are optional preview/check actions. **Build Final Interior never requires or trusts those prior previews**: after both canonical source PNGs exist, the build validates and processes both current sources inside its own processing session before assembly. Missing or stale preview artifacts do not disable the build.

When `HasBackground = true`:

```text
processed interior_cover
→ background
→ processed interior_book_owner
→ background
→ each Intro → background
→ each randomized Interior → background
```

When `HasBackground = false`:

```text
processed interior_cover
→ processed interior_book_owner
→ each Intro
→ each randomized Interior
```

The two actions intentionally overwrite the same Interior output. The latest successful action determines whether the file is a Base Interior or Production Interior. Publication remains temporary-first and atomic, so a failed run never removes the last successful PDF.

## Existing code to reuse

| Sub-problem | Existing implementation | Plan |
|---|---|---|
| Workspace creation | `PhysicalBookWorkspaceFactory` | Extend canonical directories; do not create a second workspace factory |
| Brand validation/fingerprint | `BrandValidationDefinition`, `BrandFingerprintCalculator` | Add `book_owner.psd` to the existing required-file collection |
| Template copy | `BrandTemplateCopyService` | Expand the existing required template list from two to three |
| PNG inspection | `IImageInspector` | Use only during import/action validation, never during startup snapshot refresh |
| No Frame semantics | `ForcedNoFramePolicy` in `DiskBackedInteriorPagePipeline` | Reuse CropArt behavior through a Production processing kind |
| Interior processing | `IInteriorPagePipeline` | Reuse unchanged image stages with isolated cache/output paths |
| Intro and randomized Interior | `WorkspaceBookProcessingQueueBookProcessor` | Extend through an explicit Production mode; keep `InteriorOnly` branch unchanged |
| Background interleaving | `PdfSharpPrintableBookPdfExporter.BuildIntroRasterPages` and interior import | Feed production prefix pages into the ordered prefix list so the same interleaving rule applies |
| PDF validation/publication | `ValidatedBookOutputPublisher` | Add cover-only publication and reuse atomic replacement rules |
| Long-running work | `IBackgroundTaskManager` and keyed workers | Add a Production worker for independent preview/Cover actions; route Final Interior through existing ProcessingSession |
| Book Detail UI | existing drawer tabs, pending flags and bridge polling | Add one Production tab following current interaction patterns |
| PDF Library | `PublishedArtifacts` and output summaries | Record Cover/Interior artifacts through existing snapshot projection |

## Proposed architecture

```text
Book Detail / Production tab
        |
        v
native PNG picker -> ProductionAssetImportService -> .workspace/production
                         |
        +----------------+-------------------+
        |                |                   |
        v                v                   v
Build Cover PDF   Process Cover preview   Process Owner preview
        |                |                   |
ProductionActionWorker -> shared Interior pipeline <- ProductionActionWorker
        |                          |
        v                          v
Cover exporter/publisher     processed/production/*.png

.workspace/production canonical sources
        |
        v
Build Final Interior -> ProcessingSessionWorker(mode=ProductionInterior)
        |
        v
fresh preflight + process both prefixes + existing Intro/Interior/shuffle
        |
        v
explicit leading pages + optional background interleaving
        |
        v
Output/<BookId> - Interior.pdf
```

### Processing model

Append `BookProcessingMode.ProductionInterior` without changing the numeric value or behavior of existing modes. Build Final Interior runs through the existing `ProcessingSessionWorker` with one Book and this mode, so current preflight, Brand/Intro/background/settings resolution, cancellation and progress behavior are reused instead of duplicated in a second worker. Production mode supplies two canonical Production sources and causes the processor to:

1. validate the current Book and selected Brand through the existing fresh-snapshot path;
2. process `interior_cover.png` and `interior_book_owner.png` with stable Production page IDs;
3. process current Intro and active Interior pages through the existing flow;
4. reuse the existing shuffle map behavior;
5. pass explicit `ProductionPrefixPages` to ordered assembly, validate them at the final page size, and produce leading pages `[interior_cover, interior_book_owner, Intro…]` before randomized Interior;
6. apply the existing optional background interleaving uniformly to every output artwork page;
7. publish to `<BookId> - Interior.pdf` and record output kind `Production`.

Add `InteriorPageProcessingKind.ProductionInterior` with these invariants:

- `frame` must be null;
- `FrameMode` must be `Disabled`;
- classification policy is `ForcedNoFramePolicy`;
- cache stamp uses a distinct `production-interior` kind;
- output directory is `processed/production`;
- stable page IDs cannot collide with regular `page-*` or `intro-*` IDs.

Use reserved stable IDs such as `production-interior-cover` and `production-book-owner`; do not derive these IDs from imported filenames. `PrintableBookProcessingCommand` receives the canonical Production prefix inputs only for `ProductionInterior` mode. The assembler/export request names the collection `ProductionPrefixPages` or `LeadingPages`; it must not disguise Production pages as Intro pages even though the exporter may reuse the same ordered background-interleaving helper.

### Published output provenance

Extend persisted Book processing state with a backward-compatible nullable Interior output kind:

```text
null        → legacy state; infer Unknown
Base        → last successful Process Interior
Production  → last successful Build Final Interior
```

The snapshot exposes this value so Book Detail and PDF Library can label the current file. Cover publication must merge its artifact into the existing published artifact set rather than removing a previously published Interior, and vice versa.

Artifact recording must merge by typed role (`Cover`, `Interior`) rather than replace the whole list. Provenance belongs to the Interior artifact, not the Book: internal `Unknown` renders as **Legacy** with helper text “Build source was not recorded by this app version.” Cover has no Base/Production badge. Filename, successful-build timestamp and provenance change only after atomic publication succeeds.

### Production state manifest

Persist `.workspace/state/production.json` as a small, versioned manifest; this is Production execution state, not template version detection. It contains:

```text
schemaVersion
assets: canonical filename + length + lastWriteUtc
processed pages: source metadata signature + processing-settings signature + completedAtUtc
cover build: source metadata signature + page geometry + completedAtUtc
interior build: aggregate input metadata/settings signature + completedAtUtc
```

The snapshot computes cheap signatures from metadata already discovered plus canonical serialized settings. It never opens/decodes image pixels and never reads PSD contents. Action-time validation still opens the relevant PNG and is authoritative. The manifest is written atomically only after success; failure/cancellation preserves the previous record. This supports advisory `Stale` status across restart without a new content-hash scan. A same-length external rewrite that also preserves its timestamp is allowed to evade the advisory badge, but Build Final Interior still revalidates and reprocesses on every request.

### Background task policy

Add one `ProductionAction` task kind for the independent page/Cover actions:

```text
Import is short and UI-owned.

ProductionAction:
├─ BuildCoverPdf
├─ ProcessInteriorCover
└─ ProcessBookOwner

ProcessingSession:
└─ BuildFinalInterior (mode = ProductionInterior, one Book)
```

Only one Production action runs at a time. Conflicts with `ProcessingSession` and `CacheCleanup` must be symmetric in both start orders because the task manager checks the policy of the newly requested kind. `ReturnExisting` must not return an unrelated Production action or another Book's task; use reject-new semantics for distinct action/Book keys and return an existing task only for the exact duplicate key. Add Production actions to keyed registration, enum-contract and latest-terminal retention tests. Library Refresh may run because imports, manifest writes and publications use atomic replacement and therefore expose the complete old or complete new state, never a partial file.

## UI specification

Add a `Production` tab to Book Detail after `Overview` and before `Interior settings`.

Information hierarchy:

```text
Production readiness summary
├─ Final Cover card
│  ├─ preview/status
│  ├─ Upload or Replace
│  └─ Build Cover PDF
├─ Interior Cover card
│  ├─ preview/source status/processed status
│  ├─ Upload or Replace
│  └─ Process Interior Cover
├─ Book Owner card
│  ├─ preview/source status/processed status
│  ├─ Upload or Replace
│  └─ Process Book Owner
└─ Final Interior action
   ├─ readiness explanation
   └─ Build Final Interior
```

Interaction states:

Use one snapshot-derived vocabulary: `Missing`, `Ready to process`, `Processing`, `Processed`, `Stale`, `Invalid`, and `Failed`. Stale and provenance are independent: an Interior artifact can be `Production + Stale`. Pending flags may optimistically protect a double-click, but after refresh the snapshot/task state is authoritative; never infer durable state from DOM flags alone.

| Event | Required transition |
|---|---|
| Successful Upload/Replace | Canonical source becomes current; derived preview and related PDF become `Stale` |
| Failed Upload/Replace | Prior source/preview/PDF remains unchanged; card shows `Failed` recovery text |
| Successful independent page process | That processed preview becomes `Processed`; Interior PDF remains unchanged/stale |
| Successful Cover build | Cover PDF becomes current and receives the successful-build timestamp |
| Successful Final Interior build | Interior PDF becomes current with `Production` provenance and timestamp |
| Successful legacy Process Interior | Same Interior filename becomes current with `Base` provenance and timestamp |

The Production summary shows the active Brand and the **saved** `HasBackground` value. Build Final Interior readiness is exactly:

- current Book preflight is ready;
- selected Brand is current/validated;
- both canonical Production Interior PNGs exist;
- saved `HasBackground=true` implies an available Brand background;
- no unsaved Interior, Intro or artwork-setting draft exists;
- no conflicting background task is active.

Processed previews are not prerequisites. A draft blocks the action with “Save changes before building Final Interior.” Canonical files are revalidated when the task starts; a file externally corrupted after import fails safely at that boundary.

Action-adjacent copy is explicit:

- Upload changes to Replace when a canonical source exists; successful Replace overwrites that source.
- Build Cover PDF: “Replaces the current Cover PDF only after a successful build.”
- Build Final Interior: “Replaces the current Interior PDF on success; Process Interior can replace it later.”
- Failure text confirms that the previous asset/PDF was kept. These clearly named actions do not add a confirmation dialog.

Accessibility and layout:

- Upgrade the shared Book Detail tab renderer to real tab semantics: `role=tablist`, stable `role=tab` IDs, `aria-selected`, `aria-controls`, roving `tabindex`, a labeled `role=tabpanel`, and Left/Right/Home/End navigation. Preserve Escape close and opener-focus restoration.
- Native picker cancellation is a neutral no-op, not an error toast, and returns focus to the invoking button.
- Buttons expose disabled reasons through visible helper text associated with `aria-describedby`; `title` is secondary only.
- Each task group has persistent inline feedback. Progress/success uses `role=status` with polite announcement, failure uses `role=alert`, and the active group uses `aria-busy=true`. Do not announce the whole drawer on every poll or use color alone.
- Progress stages are `Validating`, `Processing Interior Cover`, `Processing Book Owner`, `Assembling`, `Validating PDF`, and `Publishing` as applicable.
- Completing/importing refreshes Production data without closing the drawer or resetting selected tab, scroll or logical focus. Closing the drawer does not cancel work; reopening reconstructs pending state from the background-task snapshot. Do not expose Cancel unless the reused worker already supports it for this action.
- Wide layout uses three asset groups, medium uses two columns, and compact `<=900px` uses one. Final Interior spans the full width; the tab bar remains horizontally scrollable.
- Preview uses the real imported/processed file only while Production is rendered, with cache-busted URL, `loading=lazy`, `decoding=async`, `object-fit:contain`, `object-position:center`, a neutral background and explicit Source/Processed labels. Final Cover uses an approximately 2:1 surface; Interior previews use the final-page aspect ratio. No startup image decoding, square crop or generated thumbnail.
- Do not introduce a generic dashboard card mosaic; Production sections are task groups within the existing drawer surface.

## Error contract

| Codepath | Failure | Error code/result | User sees | Recovery |
|---|---|---|---|---|
| Native selection | User cancels | cancelled result | No error; unchanged UI | Select again later |
| Import | Unsupported extension/signature | `production_asset_not_png` | “Choose a readable PNG file.” | Choose another file |
| Import cover | Wrong dimensions | `production_cover_size_invalid` | Current and required dimensions | Export `5242×2626` and retry |
| Import | Selected file unreadable/disappears | `production_asset_unreadable` | Source could not be read | Re-export/reselect |
| Import | Copy/replace fails | `production_asset_import_failed` | Prior asset was kept; path/action context | Check permissions and retry |
| Process page | Source missing | `production_asset_missing` | Name the missing canonical asset | Upload asset |
| Process page | Pipeline failure | existing stage context plus Production asset name | Failed stage and retry guidance | Correct source/retry |
| Build cover | Source invalid/stale | cover validation error | Required dimensions and source name | Replace source |
| Build final | Book not ready | existing Book readiness code | Existing preflight guidance | Fix Book settings |
| Build final | Brand not validated/current | existing Brand validation code | Validate selected Brand | Validate Brand |
| Build final | Either Production source missing | `production_interior_assets_missing` | List missing assets | Upload missing source; preview processing is optional |
| Build final | Unsaved drawer settings | `production_unsaved_changes` | “Save changes before building Final Interior.” | Save, then retry |
| Build final | Background requested but unavailable | existing Brand validation failure | Validate/fix Brand background | Correct Brand |
| Publish | Temporary PDF invalid | publication validation failure | Old PDF remains; build failed validation | Inspect logs/retry |
| Task conflict | Base/Production processing active | `processing_active` or explicit task conflict | Identify active action and ask the user to wait | Retry when terminal |

## Implementation phases

### Phase 0 — Branch and baseline

1. Fast-forward local `main`.
2. Create a feature branch.
3. Run the existing full test suite before changing behavior.
4. Inventory every configured Brand and prepare `book_owner.psd` for the asset-first rollout; this is a release prerequisite, not a code fallback.

### Phase 1 — Brand template contract

1. Add `book_owner.psd` to `BrandTemplateFiles.Required`.
2. Extend Brand validation definition, discovery cards, fingerprint scope and copy result.
3. Update Brand/template tests for missing, present, fingerprint invalidation and overwrite behavior.
4. Update UI copy that currently says “copy two PSD files.”

### Phase 2 — Workspace paths and Production asset import

1. Add canonical Production path helpers without exposing user-supplied relative paths.
2. Create `generator`, `production`, and `processed/production` through the existing workspace factory.
3. Add the Production asset enum/definition and import service.
4. Add a Desktop-native PNG picker adapter and bridge command.
5. Validate content readability and Cover dimensions before atomic replacement.
6. Add the atomic, versioned Production manifest and project existence, metadata and advisory staleness into the application snapshot without image decoding during refresh.
7. Preserve active Book drawer tab/focus/scroll across picker and snapshot refresh.

### Phase 3 — Production page processing

1. Add `ProductionInterior` processing kind and invariants.
2. Route cache/output to isolated Production namespaces.
3. Reuse `ForcedNoFramePolicy` and all existing normalization/preparation/canvas stages.
4. Add a Production action service for the two independent page buttons.
5. Return processed preview paths and timestamps through refresh/task results.

### Phase 4 — Cover PDF action

1. Add a cover-only exporter request/result that embeds the original raster without pre-resampling and draws it edge-to-edge on a `17.47 × 8.75 inch` page.
2. Add cover-only publisher validation and atomic replacement at `Output/<BookId> - Cover.pdf`.
3. Add typed artifact merge so Cover publication preserves existing Interior metadata and Interior publication preserves Cover metadata.
4. Run the action through the Production background worker.

### Phase 5 — Final Interior mode

1. Append `BookProcessingMode.ProductionInterior` and route Build Final Interior through the existing `ProcessingSessionWorker` for exactly one Book.
2. Resolve canonical Production assets server-side and pass them through `PrintableBookProcessingCommand` only in Production mode.
3. Validate and process both prefix sources on every build, regardless of independent preview state, before existing Intro/Interior batches.
4. Extend ordered assembly with explicit `ProductionPrefixPages`/`LeadingPages`, validate them, and return `[interior_cover, interior_book_owner, Intro…]`; do not relabel them as Intro or duplicate exporter interleaving logic.
5. Keep current Intro selection, active-page filtering, shuffle seed/map, settings and Brand background behavior.
6. Publish to the existing Interior filename and record `Production` provenance only after publication succeeds.
7. Merge typed artifact metadata and update the Production manifest/provenance only after publication succeeds.
8. Continue recording normal Interior preview pages without mixing Production prefixes into the current artwork page editor.

### Phase 6 — Book Detail Production UI

1. Add the Production tab and three asset task groups.
2. Add upload/replace, independent process, Cover build and Final Interior build actions.
3. Add snapshot-derived state, pending guards, saved-settings readiness, disabled reasons, inline feedback and cache-busted previews.
4. Show Base/Production/Legacy provenance beside the Interior artifact in Book Detail and PDF Library, independently from `Stale`.
5. Add shared tab semantics, picker focus restoration and tab/scroll/focus preservation through refresh.
6. Reuse existing button, typography, spacing, focus and responsive patterns.

### Phase 7 — Documentation

Update:

- `README.md` workflow and quick start;
- `docs/user-guide.md` with exact upload/build order;
- `docs/architecture.md` with workspace and processing diagrams;
- `docs/pdf-engine.md` with rounded Cover page geometry and final Interior ordering;
- Brand validation documentation with `book_owner.psd`.

### Phase 8 — Verification

Run focused tests first, then:

```powershell
dotnet build PrintableBook.sln --no-restore
dotnet test PrintableBook.sln --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
node src/PrintableBook.Desktop/Frontend/test-production-ui.mjs
```

Manual smoke test with a real Book verifies import, replacement, individual previews, both `HasBackground` orders, rounded Cover PDF size, action collision protection, output provenance and preservation of the last successful PDFs after forced failures.

Before release, copy `book_owner.psd` into both configured Brands, validate both Brands with the new definition, and confirm one legacy Base Interior run still succeeds. The app build does not ship ahead of those assets.

## Test matrix

### Brand and workspace

- Missing `book_owner.psd` makes Brand invalid.
- Adding/changing/removing it invalidates the persisted Brand fingerprint.
- Template copy copies and overwrites exactly all three PSD files.
- Workspace factory creates the new canonical directories idempotently.

### Import

- Picker cancellation changes nothing.
- Non-PNG, corrupt PNG and disappearing source fail with actionable results.
- Cover rejects every size except `5242×2626`.
- Interior assets accept readable PNGs without fixed input dimensions.
- Replace is atomic and retains the previous asset when copy/validation fails.
- Book ID and asset-kind validation prevent writes outside the discovered Book workspace.
- Successful import updates the Production manifest atomically; failure retains its prior signatures.
- External metadata changes survive restart as advisory `Stale` without any image decode during snapshot refresh.

### Processing

- Both Production pages force CropArt through No Frame.
- Production cache/output never collide with regular Interior or Intro page IDs.
- Unchanged input/settings hit cache; changed input/settings invalidate expected stages.
- Independent processing returns the correct Final Interior Page size.
- Missing/corrupt sources preserve prior processed results and return an error.

### Cover PDF

- Export contains exactly one page.
- Page size is exactly `17.47 × 8.75 inch` within the existing inspector tolerance.
- Original `5242×2626` raster is drawn without pre-resize/crop.
- Embedded image dimensions remain `5242×2626`, the media box is exact, and the image covers all four page edges.
- Invalid temporary PDF never replaces the prior Cover PDF.
- Publishing Cover preserves existing Interior output metadata.

### Final Interior

- `HasBackground=true` produces `2 + Intro + Interior` artwork pages and the same number of backgrounds.
- `HasBackground=false` produces only the artwork pages.
- Exact order is Production cover, optional background, Book owner, optional background, Intro units, randomized Interior units.
- Current Intro selection, active-page filtering and shuffle map remain unchanged.
- Build uses current sources/settings/Brand rather than an old published PDF.
- Build succeeds directly after both source uploads without running either optional preview action, and reprocesses changed sources/settings.
- Failure at every stage preserves the prior Interior PDF and prior provenance.
- Base processing records `Base`; Production processing records `Production`; legacy state projects internal `Unknown` and UI `Legacy`.
- Running Process Interior after Production intentionally replaces the file and provenance with Base.
- Artifact metadata survives the sequence Base Interior -> Cover -> Production Interior -> Base Interior without losing the counterpart file.

### Desktop/bridge

- Production tab renders empty, ready, stale, pending, success and error states.
- Rapid double-click sends only one command.
- Each disabled action explains the missing prerequisite.
- Native picker cannot be supplied an arbitrary target path by WebView input.
- Task completion refreshes Production data/preview URLs while preserving the selected tab, scroll and logical focus.
- Picker cancellation restores focus and neither announces nor records an error.
- Closing/reopening the drawer preserves a running task through the task snapshot.
- Tablist semantics and Left/Right/Home/End keyboard behavior cover all existing and new Book Detail tabs.
- Production/ProcessingSession/CacheCleanup conflict tests pass in both start orders; exact duplicate requests may return the same task, while different action/Book requests are rejected rather than aliased.
- Existing `process.start` still sends `mode: interior-only` and has unchanged behavior.
- A throwing/counting `IImageInspector` proves startup/discovery/snapshot performs zero Production image inspections.

## Rollout and rollback

- No migration is required. New state fields are nullable/backward compatible.
- Existing Books without `.workspace/production` continue to load and process normally.
- Asset-first release gate: both configured Brands contain `book_owner.psd` and are revalidated before the new app build is distributed. This intentional global validation change must be called out in release notes.
- Rollback is a normal git revert. Existing Production folders/files remain ignored by the older app; existing Base Interior behavior remains usable.
- Post-change smoke checks verify one legacy Book, one Production-ready Book, a Brand missing `book_owner.psd`, and both background settings.

## NOT in scope

- Photopea or Photoshop automation.
- Reading, modifying or version-detecting PSD contents.
- Processing `app_plus.psd`.
- Generator behavior beyond creating the reserved directory.
- Automatic Production import from watched folders.
- Arbitrary Production asset names or user-configurable ordering.
- Multiple Cover sizes, trim profiles, spine calculation or DPI metadata interpretation.
- Replacing or removing the existing Process Interior action.
- Production prefix pages in the existing Interior artwork editor.
- Drag-and-drop import, in-app crop/edit and generated preview thumbnails.
- Output history, undo and batch Production across multiple Books.
- New cancellation UI where the reused background worker has no cancellation contract.
- A broad Book drawer/dialog redesign beyond the shared tab semantics and focus preservation directly exercised by the new tab.

## Decision audit trail

| # | Decision | Status | Rationale |
|---|---|---|---|
| 1 | Keep `Process Interior` unchanged and additive | User-approved | Protect current workflow while Production matures |
| 2 | Publish Cover as `Output/<BookId> - Cover.pdf` | User-approved | Matches the existing output pattern |
| 3 | Publish Production Interior to the existing Interior filename | User-approved | There is one current Interior deliverable per Book |
| 4 | Apply background after every artwork page only when `HasBackground` is true | User-approved | Exact requested order while preserving Book setting |
| 5 | Require/copy/fingerprint `book_owner.psd` | User-approved | Book Owner template becomes part of the Brand contract |
| 6 | Force No Frame/CropArt for both Production Interior inputs | User-approved | Reuses the established explicit classification override |
| 7 | Use exact rounded Cover PDF geometry `17.47 × 8.75 inch` | User-approved | User explicitly chose two-decimal physical dimensions |
| 8 | Rebuild Final Interior from current inputs instead of prepending an old PDF | Recommended architecture | Prevents stale Brand/settings/Intro/shuffle content |

## `/autoplan` review outcome

Final verdict: **HOLD_SCOPE — READY TO BUILD**.

The requested workflow is internally consistent and can be added without changing the contract of the existing **Process Interior** action. The review deliberately rejects three tempting expansions: a second image algorithm, a general-purpose production asset framework, and automatic replacement of the legacy workflow. The implementation should add the smallest explicit types needed for these three assets and four actions, while reusing the current pipeline, publisher, state store and task manager.

| Review | Result | Scope decision |
|---|---|---|
| CEO/product | Pass | The feature solves the production handoff gap; retain the additive boundary |
| Design/UX | Pass with specified states | One new Book Detail tab, progressive readiness, no dashboard redesign |
| Engineering | Pass with P1 safeguards | Isolate Production identity/cache; rebuild from sources; publish atomically |
| Developer experience | Pass with documentation work | Keep one canonical contract, stable error codes and focused test fixtures |

No unresolved product question remains. The earlier decision checkpoint already fixed the output names, background behavior, input-size policy, classification override and rounded Cover geometry.

Review closure:

| Review concern | Resolution in this plan |
|---|---|
| Three-file validation could block legacy processing | Asset-first release gate for both configured Brands |
| Preview buttons vs Final build prerequisite | Preview actions are optional; Final build always processes canonical sources |
| Rounded inches vs exact 300 DPI | Rounded media box is authoritative; source is embedded without pre-resampling and drawn edge-to-edge |
| Duplicate Final build orchestration | Reuse `ProcessingSessionWorker` with appended Production mode |
| Ambiguous prefix assembly | Explicit typed Production prefix/leading-page collection |
| One-way task conflicts | Symmetric policies and both-order tests |
| Artifact metadata replacement | Typed Cover/Interior merge after successful publication |
| Staleness without startup image work | Atomic metadata/settings manifest; zero snapshot image decoding |
| UI draft/pending/focus ambiguity | Saved-settings guard, snapshot-derived state and preserved tab/focus/scroll |

## Outcome and dream-state delta

Current state:

- a Brand supplies `cover.psd` and `app_plus.psd`;
- a Book can copy those templates and build the existing Base Interior;
- there is no first-class place to import or process the three Production images;
- the current Interior PDF has no visible provenance.

Dream state after this plan:

- a Brand validation result truthfully covers all three PSD templates used by the production workflow;
- the user can see whether each canonical Production asset is absent, ready, stale, processing or failed;
- the user can independently rebuild the Cover, either prefix page, or the complete Production Interior;
- every build consumes current sources/settings and atomically replaces only its own deliverable;
- the existing Base Interior remains a safe, unchanged fallback;
- startup discovery remains metadata-only and does not regress into image decoding.

Success is measured by deterministic output order and geometry, preservation of the prior successful PDF on failure, no behavior change to `InteriorOnly`, and actionable readiness/error text in Book Detail.

## User journey storyboard

| Step | User action | System response | Visible state |
|---|---|---|---|
| 1 | Opens a Book and selects Production | Reads snapshot metadata only | Three asset groups show missing/current/stale status |
| 2 | Uploads or replaces an asset | Native picker validates and atomically imports | Preview and source metadata refresh; cancellation is silent |
| 3 (optional) | Processes Interior Cover and Book Owner for preview | Queued Production action reuses No Frame/CropArt pipeline | Each card shows progress, then processed preview or recovery text |
| 4 | Builds Cover PDF | Validates `5242x2626`, embeds the original raster on the rounded physical page and publishes | Cover output becomes current without changing Interior |
| 5 | Builds Final Interior | Re-scans current Book/Brand/settings, processes current sources, assembles the fixed order and publishes | Interior output is labeled `Production` |
| 6 | Runs legacy Process Interior later | Existing request remains `interior-only` | Same filename is replaced and labeled `Base` |

The primary path requires no knowledge of workspace paths. Paths remain available only through existing open/reveal/copy actions and diagnostics.

## Engineering evidence and review findings

The plan is anchored to these current code paths:

- `WorkspaceBookProcessingQueueBookProcessor.cs:152-213` already owns Interior processing, shuffle, assembly, export, publication and state completion. Production extends this orchestration instead of assembling from an old PDF.
- `DiskBackedInteriorPagePipeline.cs:121-132` already maps `ForcedNoFramePolicy` to `EffectiveArtworkClassification.ForcedNoFrame()`. Production must select that policy rather than add another detector.
- `PdfSharpPrintableBookPdfExporter.cs:35-52` already accepts Intro pages, ordered Interior pages and an optional background. Extend the request with explicitly named leading Production pages while keeping background interleaving centralized.
- `ValidatedBookOutputPublisher.cs:34-67` validates before its two-step pending-file replacement. Cover-only and Production Interior publication must preserve that failure behavior.
- `BrandTemplateCopyService.cs:5-10` is the canonical required-template registry. Adding `book_owner.psd` there prevents validation, copy and UI wording from diverging.
- `PhysicalBookWorkspaceFactory.cs:16-24` is the canonical workspace creator. New directories belong there, not in UI or action handlers.

### Prioritized findings

| Priority | Confidence | Finding | Required response |
|---|---:|---|---|
| P0 | 99% | The global three-file Brand definition makes existing Brands invalid and can block the unchanged legacy processor | Enforce asset-first rollout: install `book_owner.psd` and revalidate both Brands before shipping the app |
| P1 | 98% | Prepending the last Interior PDF would silently reuse stale Intro, shuffle, Brand or settings | Build Production Interior from current sources through the existing processor |
| P1 | 97% | Production page IDs/cache paths can collide with regular Interior IDs if the existing kind is reused unchanged | Add an explicit processing kind plus stable reserved IDs and isolated cache/output roots |
| P1 | 96% | Cover publication could erase or replace Interior state if implemented through the current two-output call | Add a cover-only publication operation that merges artifact state |
| P1 | 95% | Persisting provenance before the file replacement succeeds can label an old PDF as new | Update artifact/provenance state only after successful validated publication |
| P1 | 94% | Allowing WebView to submit a source/target path creates path traversal and wrong-Book write risk | Keep source selection native and resolve every destination from Book ID plus closed asset enum |
| P1 | 94% | Task conflicts are checked from the newly requested task's policy, so a one-way rule still permits the reverse start order | Update Production, ProcessingSession and CacheCleanup policies symmetrically and test both directions |
| P1 | 93% | A second Final Interior worker would duplicate current Book/Brand/settings/Intro preflight | Use existing ProcessingSession with appended Production mode |
| P2 | 93% | Inspecting Production PNG dimensions during snapshot refresh would reintroduce startup image work | Snapshot projects existence, length and timestamp; action-time validation owns decoding |
| P2 | 92% | `17.47 x 8.75` is intentionally rounded and therefore not mathematically identical to exactly 300 DPI | Treat physical dimensions as the PDF contract; embed all source pixels without a resized intermediate |
| P2 | 90% | Two actions target the same Interior filename | Show Base/Production/Legacy provenance beside the artifact and serialize conflicting actions |

### Cover geometry invariant

`5242 / 300 = 17.4733...` and `2626 / 300 = 8.7533...`, but the approved PDF page is deliberately rounded to `17.47 x 8.75 inch`. The exporter must therefore:

1. create a page of exactly `1257.84 x 630.00 pt`;
2. place the complete `5242x2626` raster edge-to-edge in that page rectangle;
3. create no resized/cropped intermediate bitmap;
4. validate the rounded physical page size, not an assertion of exact 300-DPI equivalence.

## State and data-flow diagrams

### Production asset state

```text
Missing
  |
  | successful import
  v
Ready to process --------- process ----> Processed (matches source/settings)
  ^                              |                         |
  | failed replace keeps old     | failed process          | source/settings changes
  +------------------------------+-------------------------v
                                                       Stale
                                                         |
                                                         +---- process ----> Processed
```

`Processing` is the transient task state; `Invalid` and `Failed` keep the last good artifact and add a recovery message. Cover has `Missing -> Ready -> Published/Stale`; it skips the image-processing state. A failed operation never transitions a previously current artifact to missing.

### Build Final Interior data flow

```text
Book ID + selected Brand + settings
                 |
                 v
       fresh discovery/preflight
                 |
       +---------+----------+
       |                    |
       v                    v
Production PNGs       Intro + active Interior
       |                    |
No Frame/CropArt       existing pipelines
       |                    |
       +---------+----------+
                 v
          existing shuffle map
                 |
                 v
[cover, owner, Intro] + randomized Interior + optional background
                 |
                 v
       temporary Interior PDF
                 |
       inspect count and page size
                 |
                 v
atomic publish -> artifact/provenance state -> refreshed snapshot
```

### Action/task state machine

```text
Requested -> Queued -> Running -> Succeeded
    |          |          |
    |          |          +----> Failed
    |          +---------------> Cancelled
    +--------------------------> Rejected (invalid/conflict/not ready)
```

Only `Succeeded` may advance published-artifact metadata. Cancellation and failure leave the prior canonical source, processed page and published PDF intact whenever one existed.

### Error and recovery flow

```text
failure
  |
  +-- validation/readiness -> stable error code -> corrective UI text -> retry action
  |
  +-- task conflict --------> identify active task -> wait/cancel --------> retry
  |
  +-- processing/export ----> stage + asset context -> preserve old output -> retry
  |
  +-- publication inspect --> reject temp PDF -> preserve old PDF/state ----> diagnose/retry
```

### Rollback flow

```text
feature regression?
  |
  +-- Production-only path affected -> stop using new tab -> use Process Interior
  |
  +-- release rollback required ----> revert feature commits
                                         |
                                         +-> old app ignores retained production/ files
                                         +-> Base Interior workflow remains usable
```

## Error and rescue registry

| Surface | Detection/observability | Retry | Fallback | Escalation context |
|---|---|---|---|---|
| Asset import | Stable bridge error plus asset kind; log destination, never arbitrary UI target | Reselect/import | Prior canonical file remains | Book ID, asset kind, exception category |
| Page processing | Background task stage and asset name | Retry page action | Prior processed page remains | Page ID, cache invalidation stage, source timestamp |
| Cover export | Task stage and source dimensions | Rebuild | Prior Cover PDF remains | Expected/actual dimensions and physical size |
| Final assembly | Existing processing log plus Production step names | Rebuild Final Interior | Run existing Process Interior | Brand, setting fingerprint, page counts, shuffle seed |
| PDF validation | Inspector result in task error | Rebuild | Prior PDF and provenance remain | Expected/actual count and page geometry |
| Snapshot refresh | Existing refresh task diagnostics | Refresh again | Display last coherent snapshot | Root/discovery exception without image decode |

User-visible errors must follow: **what failed -> why -> what remains safe -> next action**. Logs may include physical paths; normal UI messages should prefer Book and asset names.

Diagnostics use the operation names `production.asset.import`, `production.page.process`, `production.cover.build`, and `production.interior.build`, with Book/asset/action, outcome, stage and duration. Do not add user-selected source paths to ordinary structured fields. An orphan action-specific `.pending` file is cleaned before the next same action, not by adding work to startup discovery.

## Failure modes registry

| Failure mode | Prevention | Detection | Containment | Recovery test |
|---|---|---|---|---|
| Partial import | Temporary sibling + atomic move | Canonical file absent/unchanged after exception | Only one Book asset | Inject copy failure; assert old bytes |
| Wrong Cover geometry | Import-time and build-time validation | Inspector reports actual pixels | Cover action only | Import/build invalid dimensions |
| Corrupt PNG after import | Revalidate at action boundary | Image inspector failure | Failing asset/action only | Replace source with corrupt bytes |
| Stale processed prefix | Production manifest metadata/settings comparison | Snapshot projects stale preview | Preview remains advisory; Build Final reprocesses deterministically | Change source/settings after process |
| Cache collision | Reserved IDs and processing kind | Tests inspect distinct paths/stamps | Production cache namespace | Same basename across regular/Production |
| Concurrent Base/Production builds | Task conflict policy | Start request rejected with active task | Current process continues | Race two start commands |
| Publication interrupted | Temp output + pending move + validate first | No state completion; pending cleanup | Prior PDF remains | Cancel/fail before each move |
| Legacy state lacks provenance | Nullable/backward-compatible field | Internal `Unknown`, UI `Legacy` | UI only; processing remains available | Deserialize old fixture |
| Brand gains missing required PSD | Required registry and fingerprint invalidation | Brand becomes not validated | Asset-first release gate prevents unexpected outage | Add/remove `book_owner.psd` |
| Library refresh sees half-write | Atomic imports/publications | File metadata is coherent | Snapshot has old or new file, never partial | Refresh during import/publish |
| Orphan pending file after crash | Action-specific pending name and cleanup | Next same action detects it | Startup path remains unchanged | Seed pending file, rerun action |

## Performance and resource budgets

- Application startup and `snapshot.refresh` must not call `IImageInspector` for Production assets. Budget impact should be metadata enumeration only.
- Import may decode one selected PNG once for validation; Cover build may revalidate once at its action boundary.
- Page processing uses the existing configured batch/concurrency controls; do not add nested unbounded parallelism.
- PDF export streams/imports in existing order and uses the current maximum page concurrency.
- Refresh after a successful task is one coalesced refresh, not one refresh per stage.
- Preview uses the canonical/processed file URL with cache busting; do not generate duplicate thumbnails.
- Add a focused benchmark or stopwatch assertion only if the snapshot implementation introduces new filesystem enumeration. Avoid timing-flaky unit thresholds; compare operation counts and ensure zero image inspections.

## Developer-experience review

Although this is a desktop end-user feature rather than a public SDK/CLI, all eight DX lenses were evaluated:

| Lens | Result | Required plan response |
|---|---|---|
| Getting started | Needs doc update | User guide gives Brand preparation, three imports and recommended build sequence |
| Information architecture | Pass | One Production tab and one canonical workspace tree |
| API ergonomics | Pass with constraint | Closed asset/action enums; bridge payloads use Book ID, not paths |
| Errors | Pass with registry | Stable codes and recovery-oriented messages |
| Examples | Needs doc fixture | Add one example tree and expected page order for both background settings |
| CLI | Not applicable | No new CLI surface in this MVP |
| Trust/signals | Pass with provenance | Base/Production/Legacy plus staleness and last successful output |
| Interactive developer support | Existing diagnostics | Task logs and bridge tests cover integration; no new playground needed |

Contract names must be defined once and reused in backend/frontend projections. Do not duplicate canonical filenames as unrelated string literals in bridge handlers, UI renderers and tests.

## Test coverage map

```text
Production feature
├─ Core unit tests
│  ├─ Brand required templates/fingerprint
│  ├─ asset/action enum validation
│  ├─ processing-mode invariants
│  └─ provenance state backward compatibility
├─ Infrastructure unit/integration tests
│  ├─ atomic import and overwrite
│  ├─ No Frame/CropArt Production pipeline and cache isolation
│  ├─ Cover exporter geometry/no intermediate resize
│  ├─ ordered background interleaving
│  └─ validated cover/interior publication preservation
├─ Desktop/bridge tests
│  ├─ picker cancellation and command contracts
│  ├─ disabled reasons/pending guards
│  ├─ task conflict/progress/result refresh
│  └─ Base/Production/Legacy labels
└─ Manual smoke tests
   ├─ legacy Book remains unchanged
   ├─ full Production happy path
   ├─ HasBackground true/false order
   └─ injected failures preserve last successful artifacts
```

Critical-path integration test:

```text
valid Brand + Book + three imported PNGs
 -> process both prefix pages
 -> Build Final Interior with fixed shuffle seed
 -> inspect exact ordered page identities/count/size
 -> assert published filename and Production provenance
 -> run Process Interior
 -> assert filename unchanged, Base order restored and provenance becomes Base
```

## Implementation work packages

| ID | Priority | Work package | Main locations | Depends on | Can run with |
|---|---|---|---|---|---|
| WP-1 | P0 | Add `book_owner.psd` contract, fingerprint, copy and tests | Core Brands, discovery, Brand tests | none | WP-2 design |
| WP-2 | P0 | Add workspace paths, asset definitions, Production manifest and import tests | Core abstractions, Infrastructure workspaces/state | none | WP-1 |
| WP-3 | P0 | Add native picker adapter and import bridge | Desktop host/router/contracts | WP-2 | WP-4 after contract agreed |
| WP-4 | P0 | Add Production page kind, cache/output isolation and processing tests | Core Processing, Infrastructure Processing | WP-2 | WP-3 |
| WP-5 | P0 | Add Cover-only export/publish and geometry tests | PDF exporter, publisher, state store | WP-2 | WP-4 |
| WP-6 | P0 | Add Production Interior mode/order/provenance through existing ProcessingSession | processor, assembler/export request, existing worker/state | WP-4, WP-5 contract | none on shared processor files |
| WP-7 | P1 | Add independent Production worker plus symmetric conflict policy/bridge results | background task registrations, policies and router | WP-3, WP-5, WP-6 | WP-8 markup prep |
| WP-8 | P1 | Add Production tab, states, previews and provenance labels | frontend components/services/state/CSS/tests | WP-3 and response schemas | WP-7 backend tests |
| WP-9 | P1 | Update docs and end-to-end smoke checklist | README and docs | stable contracts from WP-1..8 | final verification |
| WP-10 | P0 | Run focused/full verification and regression audit | all test projects | WP-1..9 | none |

P0 means required for a safe MVP; P1 means required before merge but may be implemented after the backend vertical slice. No P2 feature work is included.

### Dependency graph

```text
WP-1 ------------------------------+
                                    |
WP-2 -> WP-3 ------------------+    |
  |                             v    v
  +----> WP-4 ----> WP-6 ----> WP-7 ----> WP-8 ----> WP-9 ----> WP-10
  |                  ^
  +----> WP-5 -------+
```

Parallel work is safe only when files do not overlap. WP-4/WP-5 may proceed in parallel after their contracts are fixed. WP-6 owns shared processing orchestration and should land before task routing and UI integration to reduce merge risk.

## Documentation and diagram audit

The implementation PR must update every diagram or example that claims to describe the complete workspace, Brand contract, processing modes or output publication:

- workspace tree: add `generator`, `production`, Production cache/processed paths and `book_owner.psd`;
- workflow diagram: show Base and Production Interior as parallel actions targeting one filename;
- PDF diagram: show the optional background after every artwork page and rounded Cover geometry;
- Brand setup: change two required/copied PSDs to three;
- Book Detail screenshots only if the repository normally tracks UI screenshots.

Search documentation and UI copy for `cover.psd`, `app_plus.psd`, “two PSD”, `InteriorOnly`, `.workspace/processed`, and output filename examples before merge. Stale diagrams are a release blocker because they would give operators the wrong Brand and output contract.

## Acceptance checklist

- [ ] Existing `process.start` with `mode: interior-only` produces byte/order-equivalent Base behavior.
- [ ] Both configured Brands receive `book_owner.psd` and validate before the app build is released.
- [ ] Brand validation fails when any of the three PSDs is absent and succeeds when all exist.
- [ ] Template copy overwrites exactly the three PSDs and never image-decodes them.
- [ ] Production import accepts only readable PNGs and atomically replaces canonical files.
- [ ] `final_cover.png` requires exactly `5242x2626`; the other two have no fixed input dimensions.
- [ ] Both Production Interior pages use forced No Frame/CropArt and isolated cache identities.
- [ ] Build Final Interior succeeds directly from two uploaded sources and always reprocesses them; preview actions remain optional.
- [ ] Cover PDF is one page at exactly `17.47 x 8.75 inch` and uses every original source pixel without a resized intermediate.
- [ ] Final Interior order matches the approved contract with `HasBackground=true` and `false`.
- [ ] Both Interior actions publish only `Output/<BookId> - Interior.pdf` and show correct provenance.
- [ ] Failed/cancelled import, processing, export or publication preserves the prior successful artifact and state.
- [ ] Cover/Interior publication merges typed artifacts; Base -> Cover -> Production -> Base retains the counterpart output and correct provenance.
- [ ] Production manifest is atomic/backward compatible and drives advisory staleness without image decoding.
- [ ] Startup/discovery/snapshot performs zero Production image inspections.
- [ ] Production actions conflict safely with Base processing and cache cleanup in both request orders and never alias a different Book/action task.
- [ ] Book Detail covers empty, pending, success, error and stale states with keyboard/focus/live-region behavior.
- [ ] All focused, full .NET and bridge/frontend tests pass.
- [ ] README/user guide/architecture/PDF documentation agree with the final implementation.

## GSTACK REVIEW REPORT

```text
Plan: Production Assets and Final PDF
Review order: CEO -> Design -> Engineering -> Developer Experience
Decision mode: automatic, using completeness, consistency, clarity,
               maintainability, user value and performance where relevant
Final verdict: HOLD_SCOPE / READY TO BUILD

Accepted:
- additive Production workspace and UI
- third required Brand template: book_owner.psd
- native, canonical, atomic PNG import
- reuse of No Frame/CropArt Interior processing
- independent Cover/prefix actions
- source-based Production Interior rebuild
- optional background after every artwork page
- existing output filename with Base/Production/Legacy provenance
- rounded Cover page geometry: 17.47 x 8.75 inch
- existing ProcessingSession for source-based Final Interior rebuild
- metadata-only Production manifest for advisory staleness

Rejected/deferred:
- replacement of legacy Process Interior
- Photopea/PSD processing and app_plus.psd processing
- automatic watched-folder import
- a duplicate image-processing or PDF-assembly pipeline
- configurable asset names/order/sizes
- startup image decoding

Release gates:
- book_owner.psd is installed and validated in both configured Brands before app rollout
- old InteriorOnly regression suite remains green
- deterministic order/geometry and atomic failure tests pass
- task conflict and legacy-state compatibility tests pass
- documentation and diagrams contain no two-template or old-workspace claims

UNRESOLVED DECISIONS: 0
``` 

NO UNRESOLVED DECISIONS
