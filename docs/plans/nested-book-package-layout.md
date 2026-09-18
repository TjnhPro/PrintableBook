<!-- Autoplan restore point: C:\Users\admin\.gstack\projects\TjnhPro-PrintableBook\plans\2026-09-18-nested-book-package-layout.restore.md -->

# Main/Clone Book Package Layout Plan

Status: Implemented
Date: 2026-09-18

## Final product contract

One outer folder under `sources/` remains one Book.

```text
<Book root>/
├─ Main book/
│  └─ Book cover/
│     └─ <first supported image>  # thumbnail only
└─ Clone book/
   ├─ Book cover/
   ├─ Book interior/
   ├─ Book colored/
   ├─ Source cover/
   └─ ...                         # existing layout and behavior
```

Rules:

1. `Main book` is not a processing root.
2. Under `Main book`, this patch looks only at the direct folder `Book cover`.
3. The first supported image in `Main book/Book cover`, ordered by filename with `OrdinalIgnoreCase`, becomes the Book thumbnail/representative image.
4. No other folder under `Main book` is scanned, counted, validated, compared, shown as source inventory, or added to processing assets.
5. All source discovery and processing assets come from `Clone book`, using the existing `BookSourceLayout.ProcessingFolders` meanings unchanged.
6. The Main thumbnail never becomes a processing Cover candidate.
7. When `Clone book` is absent, the existing flat Book layout continues to work unchanged.
8. When `Clone book` exists, it wins deterministically as the processing root. This version does not add mixed-layout warnings or a source selector.
9. If Main cover is missing or has no supported image, use the existing representative-cover fallback from Clone; processing is not blocked.

The contents of `Main book/Book interior` are irrelevant to this version. The application does not know or report whether it has 0, 40, 42, or any other number of pages.

## Why the roles must stay separate

Current code derives processing cover candidates from `BookSource.GetAssets(BookAssetKind.Cover)`, and the processor selects the full-book cover from that same collection. Therefore the Main thumbnail must not be inserted into `BookSource.Assets`; otherwise a display-only image could become selectable and enter export.

## Architecture

### Data flow

```text
sources/<outer Book>
        |
        v
BookSourceScanner
  |
  +-- Clone book exists? ---------------------------+
  |                                                  |
  | yes                                              | no
  v                                                  v
processing root = Clone book                 processing root = Book root
  |                                                  |
  +---------------- existing ProcessingFolders -----+
        |
        v
BookSource.Assets -> validation -> processing

Main book/Book cover
        |
        +-> first supported filename
        +-> scan metadata.RepresentativeImageReference
        +-> snapshot.RepresentativeCoverReference
        +-> desktop thumbnail only
```

### Scanner metadata

Extend `BookSourceScanResult` with a small optional metadata object:

```csharp
public enum BookSourceLayoutKind
{
    LegacyFlat,
    MainCloneNestedV1
}

public sealed record BookSourceScanMetadata(
    BookSourceLayoutKind LayoutKind,
    DirectoryReference ProcessingRoot,
    FileReference? RepresentativeImageReference);
```

Keep current result factories compatible. Metadata does not carry Main folder inventory, Main Interior assets, warnings, or role mappings. Those are intentionally deferred.

### Layout resolution

```text
Clone book directory exists
  -> MainCloneNestedV1
  -> scan known processing folders below Clone book
  -> optionally inspect Main book/Book cover for thumbnail

Clone book directory absent
  -> LegacyFlat
  -> preserve current scan below outer Book root
```

Child directory matching uses `OrdinalIgnoreCase` through `IFileSystem.EnumerateDirectoriesAsync`. Do not use recursive discovery or fuzzy name matching.

### Snapshot behavior

- For metadata-aware nested scans, `RepresentativeImageReference` has first priority.
- If it is null, reuse existing representative-cover selection from processing assets.
- A selected Clone processing cover does not replace an available Main thumbnail.
- Source folder diagnostics use `metadata.ProcessingRoot` so they show the existing Clone folder names/counts.
- Legacy/stub scanner results with null metadata keep current snapshot behavior.

### Processing behavior

No production processing algorithm changes are planned.

- The processor already rescans before processing.
- The fresh nested scan contains only Clone processing assets.
- Existing Cover membership validation rejects paths absent from scanned Cover assets.
- Main thumbnail metadata is not consumed by the processor.
- Interior source keys remain relative to the outer Book root, such as `Clone book/Book interior/page-001.jpg`, matching all current state readers/writers.

## Thumbnail design

- Book grid preview: `aspect-ratio: 1`.
- Compact list: retain existing square geometry.
- Drawer preview: `64×64` desktop and `48×48` compact.
- Representative Book image: `object-fit: contain` on the existing muted surface.
- PDF Library preview ratio: unchanged.
- Missing/unreadable thumbnail: reuse the existing placeholder/error fallback.
- Existing alt text, keyboard actions, focus behavior, and touch targets remain unchanged.

## Error behavior

| Condition | Result |
|---|---|
| Clone contains valid Interior images | Process Clone normally |
| Clone Interior is missing/empty | Existing `book.interior_empty` validation |
| Clone cover missing | Interior-only remains valid; existing full-book validation applies |
| Main or Main Book cover missing/empty | Use Clone representative fallback |
| Multiple supported Main covers | Use first filename deterministically; no chooser/warning yet |
| Unsupported Main-cover files | Ignore when choosing the first supported image |
| Main thumbnail is corrupt/unreadable | Existing frontend image fallback shows placeholder |
| Main thumbnail submitted as processing cover | Existing fresh-scan membership validation rejects it |
| Folder changes after snapshot | Processing rescan observes current Clone state |

Cancellation and filesystem exceptions retain existing behavior. Do not catch and silently ignore scan failures.

## Implementation plan

### Phase 0: branch preflight

1. Preserve this reviewed plan.
2. Refresh local `main` using a fast-forward-only pull.
3. Create `feat/main-clone-source-layout`.
4. Run the baseline full test suite.

Do not implement on the old `fix/no-frame-force-cropart` branch.

### Phase 1: Core scan metadata

Files:

- `src/PrintableBook.Core/Application/Scanning/BookSourceScanResult.cs`
- new `src/PrintableBook.Core/Application/Scanning/BookSourceScanMetadata.cs`

Tasks:

1. Add the two layout kinds.
2. Add processing root and optional representative image metadata.
3. Preserve existing result-factory compatibility.
4. Validate non-null metadata paths remain within the outer Book directory.

### Phase 2: scanner resolution

Files:

- `src/PrintableBook.Infrastructure/Scanning/BookSourceScanner.cs`
- `tests/PrintableBook.Infrastructure.Tests/BookSourceScannerTests.cs`

Tasks:

1. Resolve direct `Clone book` and `Main book` children case-insensitively.
2. Select Clone as processing root when present; otherwise use outer root.
3. Enumerate existing processing folders only below the selected root.
4. For nested layout, inspect direct files only in `Main book/Book cover`, filter supported images, sort, and take the first.
5. Never enumerate another Main folder.
6. Return the Main path only through metadata.

### Phase 3: snapshot projection

Files:

- `src/PrintableBook.Core/Application/Desktop/IApplicationSnapshotService.cs`
- `tests/PrintableBook.Core.Tests/Application/ApplicationSnapshotServiceTests.cs`

Tasks:

1. Prefer explicit representative metadata over legacy fallback.
2. Keep Main thumbnail stable after selecting a Clone processing cover.
3. Run source-folder diagnostics relative to metadata processing root.
4. Retain current behavior for null metadata.
5. Keep Interior state keys outer-root-relative.

### Phase 4: processing safety tests

File:

- `tests/PrintableBook.Infrastructure.Tests/PrintableBookApplicationEndToEndTests.cs`

Tasks:

1. Prove only Clone Interior assets enter processing.
2. Prove a Main thumbnail path is rejected as selected processing cover.
3. Prove processing rescan observes missing/empty Clone Interior.

Change processor production code only if these tests reveal a real gap.

### Phase 5: desktop thumbnail

Files:

- `src/PrintableBook.Desktop/Frontend/css/book-workspace.css`
- `tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs`

Tasks:

1. Add a Book-specific square ratio instead of changing the shared PDF ratio.
2. Use `contain` for Book representative images.
3. Make drawer preview dimensions square.
4. Assert PDF Library geometry remains unchanged.

No new warning UI, role column, Main inventory, or Main/Clone chooser is included.

### Phase 6: documentation

Files:

- `README.md`
- `docs/user-guide.md`
- `docs/architecture.md`

Document exactly this bounded contract:

- Main: first supported image under `Book cover` for thumbnail only.
- Clone: existing source layout and all processing.
- Other Main folders: ignored and not detected.
- Legacy flat Books: still supported.
- State migration from a previously flattened folder: not automatic.

### Phase 7: verification

```powershell
dotnet test tests\PrintableBook.Infrastructure.Tests\PrintableBook.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~BookSourceScannerTests|FullyQualifiedName~PrintableBookApplicationEndToEndTests"
dotnet test tests\PrintableBook.Core.Tests\PrintableBook.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ApplicationSnapshotServiceTests"
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
dotnet test PrintableBook.sln --no-restore
dotnet build PrintableBook.sln --configuration Release --no-restore
```

Real-folder smoke source: `D:\Download\122-cbc-comfycatlife-coloringworld (3)`.

Expected:

- layout: `MainCloneNestedV1`;
- thumbnail: `Main book\Book cover\page-001.png`;
- processing root: `Clone book`;
- processing Interiors: 40 Clone JPG files in this sample;
- Main Interior: never enumerated;
- Main thumbnail in processing Cover candidates: no.

## Test matrix

### Infrastructure

- Legacy flat layout unchanged.
- Clone present selects nested processing root.
- Clone/Main casing variations resolve correctly.
- Main cover returns deterministic first supported image in metadata.
- Missing/empty Main cover returns null representative.
- Multiple Main images select first sorted supported filename.
- Unsupported Main-cover files are ignored.
- Main Interior and every other Main child are never returned.
- Main thumbnail is absent from Cover assets.
- Clone Interior assets are sorted without duplicates.

### Core snapshot

- Explicit Main representative wins over selected Clone cover.
- Null representative uses existing fallback.
- Nested folder diagnostics point at Clone processing folders.
- Interior source keys include the `Clone book` prefix.
- Legacy scanner results remain compatible.

### Processing

- Interior-only processes only Clone pages.
- Full-book accepts a Clone Cover asset.
- Full-book rejects the Main thumbnail path.
- Rescan after Clone removal/emptying fails closed.

### Desktop

- Book card and drawer are square.
- Portrait Main cover uses `contain`.
- Missing image uses current placeholder.
- PDF Library ratio remains unchanged.

## Acceptance checklist

- [x] One outer folder produces one Book.
- [x] Only `Main book/Book cover` is inspected under Main.
- [x] First supported Main Book-cover image becomes thumbnail.
- [x] No other Main folder is enumerated, validated, counted, or displayed.
- [x] Clone uses the existing folder contract for all processing.
- [x] Main thumbnail is not a processing Cover candidate.
- [x] Main thumbnail stays stable after Clone cover selection.
- [x] Legacy flat Books behave unchanged.
- [x] Book previews are square and preserve the portrait cover.
- [x] PDF Library geometry is unchanged.
- [x] Focused tests, full suites, Node tests, and Release build pass.
- [x] Real-folder smoke matches the bounded contract.

## Scope decisions

Included:

- Clone processing root;
- one Main Book-cover thumbnail lookup;
- display/processing cover separation;
- Clone-based source diagnostics;
- Book-only square/contain preview;
- legacy compatibility and regression coverage.

Deferred:

- detection of `Main book/Book interior` or any other Main folder;
- Main/Clone page comparison or missing-page warnings;
- role-aware Main inventory;
- mixed-layout warnings or source selection;
- multiple-Main-cover warning/chooser;
- manifests or arbitrary package mappings;
- automatic state migration from flattened paths.

## Decision audit trail

| Decision | Status | Rationale |
|---|---|---|
| Clone is the only processing root when present | Approved by user | Clone retains existing source structure |
| Main inspection is limited to direct `Book cover` files | Approved by user | Other Main folders are future scope |
| First supported Main cover becomes thumbnail only | Approved by user | Prevents display assets entering export selection |
| Do not inspect or reason about Main Interior | Approved by user | Removes the incorrect page-008/page-042 assumption |
| Main thumbnail stays stable after Clone cover selection | Autoplan decision | Separates identity from processing configuration |
| Book preview square with `contain` | Design review decision | Avoids cropping title artwork |
| Clone wins if flat folders also exist | Scope-reduction decision | Warnings/selector are deferred |
| Outer-root-relative Interior keys remain | Engineering decision | Keeps current state readers/writers consistent |

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|--------|---------|-----|------|--------|----------|
| CEO Review | `/plan-ceo-review` via `/autoplan` | Scope and strategy | 1 | CLEAR | Reduced to Main thumbnail plus Clone processing |
| Codex Review | `/codex review` | Independent second opinion | 0 | UNAVAILABLE | Local Codex CLI authentication failed with HTTP 401 |
| Eng Review | `/plan-eng-review` via `/autoplan` | Architecture and tests | 1 | CLEAR | Minimal metadata boundary and processing isolation |
| Design Review | `/plan-design-review` via `/autoplan` | UI/UX gaps | 1 | CLEAR | Square Book preview uses contain; PDF unchanged |
| DX Review | `/plan-devex-review` | Developer experience gaps | 0 | SKIPPED | No developer-facing product surface |

**VERDICT:** CEO + DESIGN + ENG CLEARED — scope matches the explicit user contract and is implementation-ready pending approval.

NO UNRESOLVED DECISIONS
