<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\feat-process-interior-pages-only-autoplan-restore-20260924-102828.md -->
# PDF Library Card UI — Reviewed MVP Plan

Status: Reviewed — `HOLD_SCOPE`, ready for implementation  
Date: 2026-09-24  
Scope: planning and review only; no application code changes in this phase

## Outcome

Simplify PDF Library so a user can scan Book outputs quickly, preview either PDF without a visible action cluster, and open the shared Book output folder with one obvious button.

The MVP card must answer three questions at a glance:

1. Which Book is this?
2. Are Cover and Interior available, and what are their page count, dimensions, and file size?
3. How do I preview an output or open its folder?

## Confirmed Product Decisions

- The card preview uses the Book Cover image at a visual ratio of `2 / 1`.
- The image must use `object-fit: contain`; never crop or stretch the Cover.
- Cover and Interior remain separate output rows inside one Book card.
- Each output row shows only:
  - output kind: `Cover` or `Interior`;
  - page count;
  - page dimensions;
  - main PDF file size.
- Remove visible `Preview`, `Open original`, and `Copy path` buttons from PDF Library.
- Rename the user-facing `Reveal in Explorer` concept to `Open Folder`.
- Show exactly one `Open Folder` button per Book card, shared by Cover and Interior.
- Preview remains available by activating the Cover or Interior output row itself.
- Preview uses the existing lightweight companion PDF when valid and falls back to the original PDF when the companion is unavailable.
- Do not embed a PDF renderer or inline page viewer in this MVP.

## Premises Confirmed by Review

1. PDF Library is primarily a local artifact browser, not another production workflow screen.
2. The common action is scanning and previewing; file management is secondary.
3. New Cover and Interior publications for one Book belong to `<Book.Directory>\Output`; legacy or corrupt state must not cause the app to open a guessed folder.
4. A full-row preview target is discoverable when it has clear hover, focus, icon, and helper text.
5. Hiding actions from this screen does not require deleting their existing bridge contracts.

## What Already Exists

| Need | Existing capability | Reuse decision |
|---|---|---|
| Book-level card | `renderOutputs()` groups `OutputSummaries` by Book | Keep the Book grouping |
| Cover representative | `bookThumbnailMarkup()` renders the current Book representative image | Reuse; only change PDF Library sizing and fit |
| Cover/Interior facts | `BookOutputSummary` already exposes kind, page count, dimensions, file size, verification and preview state | Reuse without a new persistence model |
| Lightweight preview | `book.output.preview` resolves `_thumbnail.pdf`, then falls back to the main PDF | Reuse from a row click instead of a visible button |
| Local file opening | `ILocalOutputActionService` opens or reveals local artifacts | Extend narrowly for explicit folder-open semantics |
| Canonical output path | Both processing workers publish to `Path.Combine(book.Directory.Value, "Output")` | Derive the folder from the discovered Book, not from a frontend path or arbitrary artifact |
| Search/sort/paging | PDF Library already supports search, newest/name/size sort, Grid/List, and 12 Books per page | Preserve behavior |
| Grid/List styles | `book-workspace.css` already owns PDF Library layouts and responsive breakpoints | Adjust existing selectors; do not introduce a design framework |

## Information Hierarchy

Order inside each card:

```text
[ Cover image, 2:1, contained ]

Book name

Cover
1 page · 17.47 × 8.75 in · 3.2 MB

Interior
48 pages · 8.625 × 8.75 in · 24.6 MB

[ Open Folder ]
```

Hierarchy rules:

- Book name is the only card heading.
- Use `Cover` and `Interior` labels instead of full filenames. Filenames remain available to assistive technology or tooltip text if useful.
- Page count, dimensions, and size share one compact metadata line in that order.
- Hide redundant positive badges such as `PDF ready` and `Verified` when the output is healthy.
- Show a compact status only when the user needs to notice something: `Stale`, `Missing`, `Invalid`, or preview fallback.
- Do not show combined output count or combined file size in the header; each row already carries the useful facts.

## Card Layout Contract

### Grid

- Preserve the current 4/3/2/1 responsive column progression.
- The Cover preview spans the full card width with `aspect-ratio: 2 / 1`.
- Use `object-fit: contain` on a neutral background.
- Card body contains the Book identity, two compact output rows when present, then a full-width secondary `Open Folder` action.
- Keep cards content-driven; do not force equal heights if one Book has only one output.
- A long Book name is one line with ellipsis and full text in `title`/accessible name.

### List

- Preserve the List option for dense inspection.
- Keep the same information and action contract as Grid; only placement changes.
- Cover preview remains `2 / 1`, never square.
- Suggested desktop structure:

```text
[160 × 80 Cover]  Book  Cover facts  Interior facts  [Open Folder]
```

- At narrow widths, stack identity, outputs, and action beneath the Cover preview without changing interaction semantics.

## Output Row Interaction

The complete Cover or Interior row is one preview target.

```text
┌──────────────────────────────────────────┐
│ Cover                        Preview ›  │
│ 1 page · 17.47 × 8.75 in · 3.2 MB       │
└──────────────────────────────────────────┘
```

- Mouse: click the row to preview that output.
- Keyboard: row is reachable by Tab and activates with Enter or Space.
- Visual feedback: pointer cursor, hover surface, visible focus ring, pressed state.
- Use a native `<button type="button">` as the row target; its visible child spans contain the output label, metadata and trailing `Preview ›` affordance.
- Build the accessible name from visible row text plus a visually hidden action prefix. Do not replace all visible metadata with a short `aria-label`; connect exceptional status/help with `aria-describedby`.
- The trailing `Preview ›` text is mandatory on desktop and remains part of the row, not a separate button. It may collapse to a familiar open icon at narrow widths only when the accessible name remains explicit.
- A row must not activate when its main artifact is missing or invalid; render it disabled/non-interactive with a short status.
- Unknown output kinds are not rendered. Order known rows as Cover then Interior. If corrupt legacy state contains duplicate rows of one known kind, render only the newest `GeneratedAt` entry with a stable artifact-reference tie-breaker.
- Preview behavior remains server-authoritative:
  - valid companion PDF: open the companion;
  - missing/stale/invalid companion: open the original PDF and surface the existing fallback feedback;
  - main output marked `Missing` or `Invalid`: reject with `output_not_previewable`, even if a stale file still exists;
  - missing original: return `output_not_found` and show page-local refresh/rebuild guidance.

### Action pending contract

- Use a per-target in-flight key: `preview:<bookId>:<artifactReference>` or `folder:<bookId>`.
- Ignore a second activation while the same key is pending; do not block the other output row.
- Set `disabled` and `aria-busy="true"` immediately without redrawing the Library.
- Clear pending state on both success and error, update only the existing row/button and page-local feedback region, then retain or restore focus to the initiating control.
- A double-click must produce exactly one bridge request and one OS launch.

## Open Folder Contract

- One `Open Folder` button appears in the card footer.
- It opens the Book output directory without selecting a specific Cover or Interior file.
- The frontend sends Book identity, not an arbitrary filesystem directory.
- The desktop host resolves the discovered Book from the latest completed snapshot, then derives the canonical directory as `Path.GetFullPath(Path.Combine(book.Directory.Value, "Output"))`.
- The host requires that the canonical directory exists and that at least one current published main artifact exists with that exact normalized parent directory.
- If any current existing Cover/Interior artifact resolves outside the canonical directory, reject with `output_folder_inconsistent`; do not select an arbitrary parent and do not open two Explorer windows.
- If no qualifying published main artifact or directory exists, return `output_folder_not_found`; never open a guessed directory.
- An existing main PDF marked `Invalid` still qualifies for Open Folder because folder access is its recovery path; it does not qualify for Preview.
- Keep old `book.output.open`, `book.output.reveal`, and `book.output.copy-path` handlers for compatibility, but PDF Library no longer renders controls for them.
- Add the explicit command `book.output.open-folder` and `OpenFolderAsync(DirectoryReference)`; do not change `RevealAsync` semantics for other callers.
- `OpenFolderAsync` opens the directory itself with shell execution and never uses Explorer `/select`.

## States

| State | Card behavior | Recovery |
|---|---|---|
| Both PDFs ready | Show both interactive rows and one Open Folder button | None needed |
| Cover only | Show Cover row; omit Interior row | Build Final Interior elsewhere |
| Interior only | Show Interior row; keep Cover representative/fallback visual | Build Cover elsewhere |
| Companion preview missing | Row remains interactive; preview falls back to original | Rebuild output later if needed |
| Main PDF missing after snapshot | Action returns not found; refresh Library | Refresh or rebuild output |
| Stale Interior | Keep preview/open-folder available and show `Stale` | Rebuild Final Interior elsewhere |
| Invalid PDF | Disable preview for that row; keep folder access if another artifact exists | Open Folder and inspect/rebuild |
| Cover image unavailable | Keep fixed 2:1 placeholder without layout shift | Output rows remain usable |
| Empty Library | Keep current build guidance | Navigate to Book production workflow |
| No search result | Keep query-specific empty state | Clear or change search |
| Preview or folder action pending | Disable only that target, show `Opening…`, preserve the rest of the card | Re-enable on success/error |
| Duplicate click while pending | Ignore duplicate request | Wait for the first result |
| Output folder inconsistent | Keep rows visible; show page-local error and do not launch Explorer | Refresh/rebuild to restore canonical output state |
| Unknown-only legacy outputs | Do not render a misleading empty card | Refresh/rebuild known Cover or Interior output |

## Toolbar and Paging

- Preserve Search, Sort, Grid/List, 12 Books per page, and local paging behavior.
- Search continues to match Book name for MVP.
- Sort remains `Newest`, `Name`, and total main-PDF `Size`.
- Removing per-output actions must not reset search, sort, selected view, or current page.
- No new Brand filter is part of this UI phase.

## Accessibility

- Use semantic buttons for output-row preview and Open Folder.
- Keep one clear focus stop per output plus one for Open Folder.
- Minimum interactive height: 40 px; preferred 44 px where layout allows.
- Focus ring must remain visible against both normal and stale/error surfaces.
- Do not rely on color alone for stale, missing, or invalid output.
- Announce preview fallback and output-not-found through the PDF Library-local feedback/status channel.
- Add one PDF Library-local feedback region below the toolbar. Use `role="status"` for success/fallback and `role="alert"` for errors; do not rely only on the sidebar bridge status.
- Do not redraw the complete Library after preview/folder responses; full redraw can lose focus and scroll position.
- Decorative thumbnail images use empty alt text when the Book name is adjacent; otherwise provide the Book name.
- Grid/List toggles retain `aria-pressed`; pagination labels remain explicit.

## Architecture and Change Surface

Expected implementation files:

- `src/PrintableBook.Desktop/Frontend/js/app.js`
  - simplify `renderOutputs()`;
  - make output rows preview targets;
  - emit one `book.output.open-folder` request per card.
- `src/PrintableBook.Desktop/Frontend/css/book-workspace.css`
  - enforce 2:1 contained Cover preview in Grid and List;
  - style compact rows, focus/hover states, and one card footer action.
- `src/PrintableBook.Core/Application/Desktop/IApplicationSnapshotService.cs`
  - reuse existing output facts; no new persisted state expected.
- `src/PrintableBook.Desktop/Bridge/WebViewBridgeRouter.cs`
  - add validated Book-level folder-open routing while retaining old actions.
- `src/PrintableBook.Desktop/LocalOutputActionService.cs`
  - add explicit folder-open behavior.
- Existing frontend, bridge, and UI contract tests.
- User-facing PDF Library documentation.

No database, migration, new PDF parser, embedded browser PDF renderer, or new output metadata abstraction is expected.

Implementation must begin on a fresh feature branch from the updated `main`. The current planning branch belongs to an already merged PR and must not receive the application changes.

## Architecture Flow

```text
ApplicationSnapshot
  └─ BookDesktopSummary.OutputSummaries
       └─ renderOutputs()
            ├─ 2:1 representative Cover image
            ├─ Cover row ─────── book.output.preview ─┐
            ├─ Interior row ──── book.output.preview ─┼─ host validates artifact
            │                                          └─ opens companion or original PDF
            └─ Open Folder ── book.output.open-folder ── host resolves Book output directory
                                                        └─ Explorer opens directory
```

### Action state machine

```text
IDLE
  └─ activate target
       ├─ same key already pending ───────────────> IGNORE DUPLICATE
       └─ add key + disable target + aria-busy ──> PENDING
                                                     ├─ success ─> clear key ─> announce result ─> IDLE
                                                     └─ error ───> clear key ─> announce recovery ─> IDLE
```

### Error flow

```text
Frontend sends IDs only
  └─ Router validates current snapshot
       ├─ invalid/missing identity ──> stable error code ──> page-local alert
       ├─ preview
       │    ├─ invalid main ─────────> output_not_previewable
       │    ├─ valid companion ──────> shell-open companion
       │    └─ bad companion ────────> shell-open original + fallback notice
       └─ folder
            ├─ derive <Book.Directory>\Output
            ├─ missing/no current file ─> output_folder_not_found
            ├─ mismatched parent ───────> output_folder_inconsistent
            └─ shell-open directory ────> success or output_launch_failed
```

### Deployment and rollback

```text
fresh branch from main
  -> contract tests
  -> additive bridge/service command
  -> simplified markup/CSS + pending/feedback behavior
  -> focused tests
  -> full solution tests
  -> manual Windows smoke test
  -> PR

Rollback: revert frontend + additive command/service change together.
Legacy open/reveal/copy contracts remain, so rollback needs no data migration.
```

## Error and Rescue Registry

| Code path | Failure/exception | Stable bridge result | Rescue action | User impact |
|---|---|---|---|---|
| Preview payload validation | blank Book/artifact identity | `invalid_output_action` | Clear pending; keep focus; explain that the request was rejected | No OS launch |
| Preview snapshot lookup | Book/artifact absent or main file deleted | `output_not_found` | Clear pending; page-local `Refresh or rebuild this output` message | Preview stays closed |
| Preview verification gate | main `VerificationStatus` is `Missing` or `Invalid` | `output_not_previewable` | Disable row; keep Open Folder available when canonical output exists | User inspects/rebuilds instead of opening a bad PDF |
| Preview companion selection | preview missing, stale, invalid or deleted | success with `fallbackToOriginal=true` | Open verified original; announce fallback | Work continues with a larger PDF |
| Folder payload validation | blank/unknown Book identity | `invalid_output_action` | Clear pending; keep focus | No Explorer launch |
| Canonical folder resolution | Book/output directory or qualifying artifact missing | `output_folder_not_found` | Page-local refresh/rebuild message | Folder stays closed |
| Canonical folder resolution | existing known artifact parent differs from `<Book.Directory>\Output` | `output_folder_inconsistent` | Fail closed; request refresh/rebuild | No arbitrary directory opens |
| `OpenAsync` / `OpenFolderAsync` | `Win32Exception`, `InvalidOperationException`, `UnauthorizedAccessException`, or `Process.Start` returns null | `output_launch_failed` | Clear pending; keep focus; offer retry/inspect via Windows manually | Library remains usable |
| Thumbnail image load | image decode/load failure | client placeholder | Replace image in place | Card does not collapse |
| Metadata formatting | null page count/dimensions | no bridge error | Render `—` for only the missing fact | Preview remains usable when main output is valid |
| Cancellation | `OperationCanceledException` | rethrow through existing cancellation path | No stale success message | Action ends without false completion |

## Failure Modes Registry

| Code path | Failure mode | Rescued? | Test? | User sees | Logged/diagnosable? |
|---|---|---:|---:|---|---:|
| Output row markup | Row is mouse-only | Yes: native button | Yes | Visible focus + keyboard activation | Contract test |
| PDF Library CSS | Cover is cropped by inherited `object-fit: cover` | Yes: scoped `contain` rule | Yes | Full Cover with letterboxing | CSS contract + manual check |
| Preview dispatch | Double-click opens two viewers | Yes: per-target in-flight key | Yes | One `Opening…` state | Request-count assertion |
| Folder dispatch | Double-click opens two Explorer windows | Yes: per-Book in-flight key | Yes | One `Opening…` state | Service-call count assertion |
| Preview bridge | Wrong output or invalid main opens | Yes: summary membership + verification gate | Yes | Clear error, no launch | Stable bridge code |
| Folder bridge | Frontend path escapes Book output | Yes: host-derived canonical path | Yes | Clear error, no launch | Stable bridge code |
| Folder bridge | Legacy Cover/Interior parents disagree | Yes: fail closed | Yes | Rebuild/refresh guidance | `output_folder_inconsistent` |
| UI response | Pending state remains stuck after error | Yes: clear on every terminal response | Yes | Action becomes available again | DOM/request assertion |
| UI response | Full redraw loses focus/scroll | Yes: mutate feedback/target in place | Yes | Focus and Library position remain stable | UI contract + manual check |
| Compatibility | Removing visible actions deletes old commands | Yes: keep handlers | Yes | Other callers unchanged | `CanHandle`/bridge regression |
| Output projection | Unknown/duplicate legacy kinds create misleading rows | Yes: normalize known kinds deterministically | Yes | Only Cover/Interior are shown | Render fixture |

No row is left with `Rescued = No`, `Test = No`, and silent user impact.

## Test Plan

### Frontend contract

- Grid and List both render the Cover at `2 / 1` and specify contained image behavior.
- A Book with Cover and Interior renders two output rows and exactly one Open Folder button.
- Output rows display page count, dimensions, and file size.
- Visible `Preview`, `Open original`, and `Copy path` buttons are absent.
- `Reveal in Explorer` copy is absent; `Open Folder` is present.
- Activating Cover and Interior rows sends the exact corresponding artifact identity to `book.output.preview`.
- Open Folder sends Book identity once and does not depend on which output row was last used.
- Output rows are native buttons with visible `Preview ›`, exact accessible names/descriptions, and no nested button.
- Unknown outputs are omitted, known outputs order Cover then Interior, and duplicate known kinds resolve deterministically.
- Two rapid activations of the same row or folder button send one request; another output row remains usable.
- Both success and error clear the matching pending key without redrawing the Library; focus and scroll position remain stable.
- Page-local feedback maps fallback, missing, invalid, inconsistent-folder and launch-failure results to actionable copy.
- Grid/List, search, sort, and paging preserve their current behavior.
- Long names, one-output cards, missing representative Cover, and stale output states render without overflow.

### Bridge/desktop contract

- `book.output.open-folder` rejects missing/unknown Book identity.
- It derives `<Book.Directory>\Output` from snapshot discovery and accepts no frontend path.
- It resolves only a canonical directory backed by an existing current published main artifact.
- It opens the canonical Book output folder once when both outputs exist.
- It behaves correctly when only Cover or only Interior exists.
- It rejects an existing artifact outside the canonical parent with `output_folder_inconsistent`.
- It returns `output_folder_not_found` for a missing directory/all files missing and `output_launch_failed` for shell launch errors/null process.
- Preview rejects `Missing`/`Invalid` main outputs but still falls back when only the companion is unavailable.
- Folder access remains allowed for an existing invalid main PDF in the canonical folder.
- Existing preview fallback behavior remains unchanged.
- Legacy open/reveal/copy bridge commands remain accepted.

### Manual UI verification

- Compare Grid at 4, 3, 2, and 1 columns.
- Verify List at desktop and narrow window widths.
- Confirm Cover artwork is fully visible with letterboxing when necessary.
- Tab through two output rows and Open Folder; verify visible focus and Enter/Space activation.
- Preview Cover and multi-page Interior; confirm the lightweight companion opens.
- Remove a companion PDF and confirm preview falls back to the original.
- Open one Book folder from cards containing both PDFs, Cover only, and Interior only.
- Rapidly double-click each action and verify only one viewer/Explorer window opens.
- Force a bridge error and verify the initiating control is enabled and focused again without card redraw.

### Coverage diagram

```text
CODE PATHS                                             USER FLOWS
[+] renderOutputs()                                    [+] Scan PDF Library
  ├─ [TEST] normalize Cover/Interior                     ├─ [TEST] Grid/List hierarchy parity
  ├─ [TEST] 2:1 contained representative                 ├─ [TEST] 4/3/2/1 responsive CSS contract
  ├─ [TEST] known/unknown/duplicate kinds                └─ [MANUAL] visual letterboxing/overflow
  └─ [TEST] ready/invalid/missing row states

[+] output action dispatcher                           [+] Preview Cover or Interior
  ├─ [TEST] first activation sends exact IDs             ├─ [TEST] correct artifact request
  ├─ [TEST] same-key duplicate ignored                   ├─ [TEST] double-click opens once
  ├─ [TEST] different row remains available              ├─ [MANUAL] Enter/Space in WebView2
  └─ [TEST] success/error clears pending                  └─ [TEST] focus/feedback recovery

[+] WebViewBridgeRouter                                [+] Open Folder
  ├─ [TEST] payload/Book/snapshot validation             ├─ [TEST] Cover-only / Interior-only / both
  ├─ [TEST] preview verification + fallback              ├─ [TEST] canonical folder opens once
  ├─ [TEST] canonical folder containment                 └─ [TEST] missing/inconsistent/launch error
  └─ [TEST] stable response codes

[+] LocalOutputActionService
  ├─ [TEST] opens file via shell
  ├─ [TEST] opens directory without /select
  └─ [TEST] null/exception propagates to router mapping
```

### Test commands

```powershell
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
node src/PrintableBook.Desktop/Frontend/test-ui.mjs
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj
dotnet test PrintableBook.sln
```

## Implementation Phases

### Phase 1 — Lock the view contract

- Start a fresh feature branch from updated `main` and bring this plan onto it without application changes from the merged branch.
- Update frontend fixtures/tests first for the simplified card structure, known-output normalization, pending states and page-local feedback.
- Define exact row labels, metadata formatting, one Open Folder action, and absence of old visible actions.
- Acceptance: failing tests express the final hierarchy and action contract before implementation markup changes.

### Phase 2 — Simplify card markup and styling

- Refactor only the PDF Library render helpers inside `renderOutputs()`.
- Apply the 2:1 contained Cover contract to Grid and List.
- Convert Cover/Interior rows into semantic preview targets.
- Add per-target in-flight guards and update controls/feedback in place; never redraw the full page for an output action response.
- Acceptance: all states render correctly without changing search/sort/paging, and duplicate activations send one request.

### Phase 3 — Add shared Open Folder behavior

- Add the Book-level bridge command and explicit desktop folder-open service method.
- Derive and validate the canonical `<Book.Directory>\Output` path from the latest completed snapshot.
- Enforce main-output preview validity while retaining companion-to-original fallback.
- Map folder-not-found, folder-inconsistent and shell-launch failure to stable codes.
- Retain existing output action commands for compatibility.
- Acceptance: one Explorer window opens the correct canonical Book output directory and unsafe/inconsistent paths fail closed.

### Phase 4 — Integrate feedback and recovery

- Map all terminal bridge responses to the PDF Library-local status/alert region.
- Clear pending state and retain focus for success, fallback and failure without resetting scroll, page, search, sort or view.
- Acceptance: every action has an actionable visible result and no target can remain permanently busy.

### Phase 5 — Regression coverage and documentation

- Cover frontend interaction, bridge validation, fallback, accessibility, and responsive contracts.
- Update README/user guide wording to match the simplified action model.
- Run focused Node/UI/.NET tests, then the full solution suite.
- Complete a manual Windows/WebView2 smoke test for PDF association, Explorer launch, keyboard activation and double-click suppression.

## NOT in Scope

- Embedded PDF renderer or in-app page carousel.
- Rendering PDF pages into new image thumbnails at view time.
- Removing legacy bridge actions globally.
- Editing, renaming, deleting, moving, or exporting PDFs from PDF Library.
- Build/rebuild buttons inside PDF Library.
- Bulk folder opening or bulk file operations.
- Brand filter, tag system, output history, or version comparison.
- Changes to PDF generation, thumbnail dimensions, publication, or freshness rules.
- Database or persistence changes.

## Success Criteria

- A user can identify Cover and Interior facts without reading filenames.
- Cover imagery is never cropped or stretched in Grid or List.
- Each Book card presents no more than three focusable primary targets: Cover preview, Interior preview, and Open Folder.
- Preview keeps using lightweight companion PDFs with safe original fallback.
- One Open Folder action serves the complete Book output set.
- Duplicate activations never open more than one viewer or Explorer window.
- Missing, invalid or inconsistent artifacts fail with page-local recovery guidance and no full-page redraw.
- Search, sort, pagination, and legacy output contracts continue to work.
- No production workflow or PDF-generation behavior changes.

<!-- AUTONOMOUS DECISION LOG -->
## Decision Audit Trail

| # | Phase | Decision | Classification | Principle | Rationale | Rejected |
|---|---|---|---|---|---|---|
| 1 | CEO | Use `HOLD_SCOPE` | Scope | P2, P5 | The user already defined a narrow local-library MVP; completeness means closing its error paths, not adding features | Embedded renderer, history, filters |
| 2 | CEO | Add a new Book-level folder command | Architecture | P1, P3 | Reinterpreting `RevealAsync` would create hidden compatibility risk | Rename/reuse artifact-level reveal |
| 3 | Design | Keep each output row as the preview control with mandatory trailing affordance | UX | P3, P5 | Removes button clutter while keeping the action discoverable | Invisible click target; separate Preview button |
| 4 | Design | Remove the assistant-added Brand subtitle from the required card hierarchy | Scope | P2, P5 | Brand was not requested for this UI and does not help inspect the two PDFs | Add new Brand context/filter |
| 5 | Engineering | Derive `<Book.Directory>\Output` from discovery and verify artifact parents | Security | P1, P3 | The host owns the path; frontend-supplied or arbitrary artifact parents are not trusted | Pick first artifact parent |
| 6 | Engineering | Fail closed when current artifact parents disagree | Error handling | P3 | Silent selection would hide corrupt legacy state | Open the first/most recent parent |
| 7 | Engineering | Reject invalid main PDFs at the bridge, not only in markup | Correctness | P3 | Server-authoritative behavior must match disabled UI | Frontend-only guard |
| 8 | Engineering | Add per-target in-flight keys and in-place feedback updates | Reliability | P1, P3 | Prevents duplicate OS launches and avoids the existing redraw/focus class of bugs | Global lock; full render after response |
| 9 | Testing | Extend existing Node/.NET harnesses; add no UI framework | Maintainability | P4, P5 | Existing harnesses can prove markup, dispatch and bridge contracts; native keyboard behavior gets a manual WebView2 smoke test | New DOM dependency for this MVP |
| 10 | Delivery | Implement on a fresh branch from updated `main` | Sequencing | P1 | The current branch's PR is merged; mixing the next feature obscures review history | Continue application work on merged feature branch |

## CEO Review Summary

### Premise challenge

The premise holds: PDF Library is an artifact browser, and the current four-button cluster makes scanning slower. The highest-leverage change is not a new PDF experience; it is making the two artifacts themselves the preview targets and moving file management to one Book-level folder action.

### Alternatives considered

| Approach | Result | Decision |
|---|---|---|
| Rename `Reveal in Explorer` and keep one artifact-scoped button | Small diff, but whichever PDF owns the button becomes an accidental source of truth | Rejected |
| Add `book.output.open-folder`, keep legacy commands, simplify the card | Explicit semantics, additive compatibility, host-owned path validation | Chosen |
| Introduce a generic output-action capability model/view model | Flexible, but adds abstraction without another consumer | Rejected for MVP |

### Dream-state delta

The 12-month ideal could include in-app multi-page viewing, version history and richer library organization. This plan deliberately stops at a fast, safe local artifact browser. The additive bridge command and semantic output rows do not block that future, while avoiding infrastructure the current workflow does not need.

### Temporal check

- First use: Cover, Book name and two output labels explain the card within seconds.
- Repeated use: row-wide targets and one folder action reduce pointer travel and visual noise.
- Failure: invalid/moved artifacts produce local recovery text without breaking Library state.
- Rollback: no persistence or generated-output format changes exist.

Verdict: **HOLD_SCOPE — READY TO BUILD**.

## Design Review Summary

- UI classification: desktop app workspace, not a marketing page.
- No `DESIGN.md` exists; review used the existing CSS tokens/components and universal app-UI/accessibility rules.
- The gstack mockup binary was unavailable, so no generated visual was approved. The ASCII Grid/List contracts are the visual source for MVP implementation.

| Pass | Before | After | Decision added |
|---|---:|---:|---|
| Information architecture | 8/10 | 10/10 | Cover → Book → Cover/Interior rows → one Open Folder action |
| Interaction states | 6/10 | 9/10 | Pending, duplicate, invalid, inconsistent-folder and response recovery specified |
| User journey | 7/10 | 9/10 | Scan, preview, fallback, inspect/rebuild path specified |
| AI-slop risk | 8/10 | 9/10 | Existing cards remain because each card is a Book interaction; decorative badges/actions are removed |
| Design-system alignment | 7/10 | 8/10 | Existing tokens and Grid/List patterns reused; no new framework |
| Responsive/accessibility | 7/10 | 9/10 | 2:1 at every width, native buttons, visible focus, status/alert behavior |
| Unresolved decisions | 6/10 | 10/10 | Six material ambiguities resolved in this review |

Overall design completeness: **7/10 → 9/10**. The remaining point is live visual validation, which belongs after implementation.

## Engineering Review Summary

### Verified findings folded into the plan

1. `app.js:119-124, 1735-1738` tracks requests but has no duplicate suppression. Per-target in-flight keys are now required.
2. `WebViewBridgeRouter.cs:531-570` validates artifact membership/existence but not main-PDF verification status. Preview now requires a valid main summary.
3. `LocalOutputActionService.cs:17-21` uses Explorer `/select`; the new folder method must shell-open the directory itself.
4. `ProcessingSessionWorker.cs:172-176` and `ProductionActionWorker.cs:85-89` already define the canonical Book `Output` directory. Folder resolution now reuses that invariant.
5. `app.js:1971-1988` provides only broad bridge feedback. PDF Library now requires local actionable messaging and no full redraw.
6. `IApplicationSnapshotService.cs:816-821` can project `Unknown`; rendering now filters/orders known output kinds deterministically.

### Performance and security

- Performance impact is negligible: a maximum of 12 rendered Books per page and at most two known output artifacts checked for an action.
- No new PDF inspection, database query, cache or background task is introduced.
- Folder path authority stays in the desktop host. The frontend sends only Book identity.
- Windows path comparison is normalized and case-insensitive; parent equality is exact after `Path.GetFullPath`.

### Parallelization

| Lane | Work | Dependency |
|---|---|---|
| A | Frontend markup/CSS, pending state, page-local feedback, Node/CSS tests | Phase 1 contracts |
| B | Bridge command, canonical resolver, service method, .NET tests | Phase 1 contracts |
| C | Integration regression, docs, full suite, Windows smoke test | Merge A + B |

Lanes A and B can run in parallel worktrees because they touch separate primary modules. Lane C is sequential. If one agent owns all work, preserve the phase/commit order instead.

### Stale diagram audit

No existing ASCII diagram in the touched frontend, bridge or service files describes PDF Library actions. The new plan diagrams are authoritative; `docs/architecture.md` remains accurate because output publication and snapshot boundaries do not change.

## Implementation Tasks

- [ ] **T1 (P1, human: ~1h / agent: ~10m)** — Contracts — Encode final card/action behavior in existing Node and .NET fixtures before implementation.
  - Files: `tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs`, `tests/PrintableBook.Desktop.Tests/BookWorkspaceLayoutContractTests.cs`, `tests/PrintableBook.Desktop.Tests/BridgeMessageContractTests.cs`
  - Verify: focused Node and Desktop test commands fail only for the new contract.
- [ ] **T2 (P1, human: ~3h / agent: ~25m)** — Frontend — Render normalized Cover/Interior rows as native preview buttons and enforce 2:1 contained imagery in Grid/List.
  - Files: `src/PrintableBook.Desktop/Frontend/js/app.js`, `src/PrintableBook.Desktop/Frontend/css/book-workspace.css`
  - Verify: exact hierarchy/action-count assertions and responsive CSS contracts pass.
- [ ] **T3 (P1, human: ~2h / agent: ~20m)** — Frontend — Add per-target duplicate suppression and page-local success/error feedback without full redraw.
  - Files: `src/PrintableBook.Desktop/Frontend/js/app.js`, `tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs`
  - Verify: rapid duplicate/error-reset tests pass; different rows remain independent.
- [ ] **T4 (P1, human: ~3h / agent: ~30m)** — Bridge — Add `book.output.open-folder`, canonical Book-output validation and main-preview verification.
  - Files: `src/PrintableBook.Desktop/Bridge/WebViewBridgeRouter.cs`, `src/PrintableBook.Core/Application/Desktop/IApplicationSnapshotService.cs`, `tests/PrintableBook.Desktop.Tests/BridgeMessageContractTests.cs`
  - Verify: success plus every stable error code and legacy-command regression pass.
- [ ] **T5 (P1, human: ~1h / agent: ~10m)** — Desktop — Add `OpenFolderAsync(DirectoryReference)` using shell execution without `/select`.
  - Files: `src/PrintableBook.Desktop/LocalOutputActionService.cs`, interface/fake implementations in Desktop tests
  - Verify: correct directory passed once; launch failures reach the router mapping.
- [ ] **T6 (P2, human: ~1h / agent: ~10m)** — Docs — Update PDF Library instructions to describe row preview and one Open Folder action.
  - Files: `README.md`, `docs/user-guide.md`, review any affected wording in `docs/architecture.md`
  - Verify: no user documentation advertises the removed visible buttons.
- [ ] **T7 (P1, human: ~1h / agent: ~15m)** — Verification — Run focused tests, full solution tests and manual Windows/WebView2 smoke scenarios.
  - Verify: all commands in Test Plan pass; one PDF viewer/Explorer window opens per rapid action.

## Review Completion Summary

| Review area | Result |
|---|---|
| CEO/product | HOLD_SCOPE; premise valid; additive command chosen |
| Design | 7/10 → 9/10; no unresolved UI decisions; live visual QA remains post-implementation |
| Engineering | 6 verified gaps folded into architecture/tests; 0 critical gaps remain in plan |
| Developer experience | Skipped; this is end-user UI with no developer-facing product surface |
| Outside voice | Independent subagent completed and agreed on HOLD_SCOPE; Codex CLI timed out before a final verdict |
| TODO proposals | 0; deferred items are either explicitly out of scope or not independently valuable yet |
| Test artifact | Written under the local gstack project directory for later QA use |

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---:|---|---|
| CEO Review | `/plan-ceo-review` via `/autoplan` | Scope & strategy | 1 | CLEAR | HOLD_SCOPE; additive Book-level folder action |
| Codex Review | outside voice | Independent second opinion | 1 | PARTIAL | CLI timed out; independent subagent found 8 contract gaps, all resolved or explicitly rejected |
| Eng Review | `/plan-eng-review` via `/autoplan` | Architecture & tests | 1 | CLEAR | 6 verified gaps folded; 0 critical gaps |
| Design Review | `/plan-design-review` via `/autoplan` | UI/UX gaps | 1 | CLEAR | 7/10 → 9/10; 6 decisions added; mockup binary unavailable |
| DX Review | `/plan-devex-review` | Developer experience gaps | 0 | SKIPPED | No developer-facing scope |

**CROSS-MODEL:** Primary review and independent subagent agreed on HOLD_SCOPE, canonical folder validation, duplicate suppression, local recovery feedback and bridge-enforced invalid-output rules. The subagent suggested a new DOM harness or pure-helper extraction; the reviewed plan keeps existing Node/.NET harnesses plus a manual native-keyboard smoke test to avoid an MVP-only dependency.

**VERDICT:** CEO + DESIGN + ENG CLEARED — plan is ready to implement on a fresh branch from `main`.

NO UNRESOLVED DECISIONS
