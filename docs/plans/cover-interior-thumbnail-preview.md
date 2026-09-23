<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\plan-book-brand-author-mvp-autoplan-restore-20260923-072611.md -->

# Kế hoạch MVP: lightweight preview PDF cho Cover và Interior

## Trạng thái

- Đã triển khai trên branch `feat/lightweight-pdf-previews`.
- Plan gốc được commit trước implementation tại `419f5eb`.
- Premise cuối cùng đã được user xác nhận: thumbnail là **một PDF đầy đủ**, giống PDF chính về nội dung/bố cục/thứ tự trang, chỉ dùng raster nhỏ hơn để preview nhanh.

## Mục tiêu

Mỗi output PDF chính có tối đa một preview PDF đi kèm:

```text
<Book> - Cover.pdf
<Book> - Cover_thumbnail.pdf

<Book> - Interior.pdf
<Book> - Interior_thumbnail.pdf
```

- `Cover_thumbnail.pdf`: 1 trang giống Cover chính; raster nhúng `2726×1313 px`.
- `Interior_thumbnail.pdf`: đầy đủ mọi trang, đúng thứ tự và page count của Interior chính; raster nhúng trên mỗi trang `600×609 px`.
- Preview PDF dùng cho thao tác Preview nhanh.
- PDF chính vẫn là production/delivery artifact và là source of truth.

## Contract đã chốt

| Nội dung | Quyết định MVP |
|---|---|
| Artifact | Cả Cover thumbnail và Interior thumbnail đều là PDF |
| Cardinality | 1 preview PDF cho mỗi PDF chính, không phải ảnh thumbnail rời |
| Cover parity | 1 trang, cùng nội dung và physical page size với Cover PDF chính |
| Interior parity | Cùng page count, page order, background pages, intro/prefix pages và physical page size với Interior PDF chính |
| Raster target | Cover `2726×1313`; Interior `600×609` cho từng trang |
| Generation | Tạo trong cùng output workflow từ cùng ordered raster inputs |
| Usage | Chỉ Preview dùng `_thumbnail.pdf`; Open/Reveal/Copy original vẫn dùng PDF chính |
| Failure | Preview lỗi không làm PDF chính thất bại; UI fallback về mở PDF chính |
| Compatibility | Không backfill bắt buộc; Book cũ không có preview vẫn load và mở original bình thường |

## Điều “giống bản gốc” nghĩa là gì

Preview PDF phải giữ:

- cùng số trang;
- cùng thứ tự trang;
- cùng nội dung trang;
- cùng Cover/Interior physical `MediaBox`;
- cùng logic Production prefix → Intro → ordered Interior → Background;
- cùng orientation và full-page placement.

Chỉ raster được downsample trước khi nhúng. Không đổi text/content, không bỏ trang, không tạo first-page-only preview và không rasterize lại PDF chính.

Target Cover `2726×1313` không cùng aspect ratio tuyệt đối với source Cover hiện tại `5242×2626`. Để PDF hiển thị giống bản chính, preview raster được resize đúng target pixel matrix rồi vẫn map full-bleed vào **cùng Cover MediaBox** như exporter chính. Không thêm letterbox/padding vào PDF và không đổi physical page geometry.

## Codebase hiện tại

| Sub-problem | Hiện trạng | Hướng tận dụng |
|---|---|---|
| Cover-only export | `ProductionCoverPdfService` → `ExportCoverAsync` → `PublishCoverAsync` | Tạo/publish Cover preview trong cùng orchestration |
| Interior-only export | `WorkspaceBookProcessingQueueBookProcessor` → `ExportInteriorAsync` → `PublishInteriorAsync` | Tạo/publish full Interior preview từ cùng `OrderedBookAssembly` |
| Full-book export | `ExportAsync` tạo Cover + Interior rồi `PublishAsync` | Tạo/publish hai preview tương ứng |
| Page order | `PdfSharpPrintableBookPdfExporter.WriteInteriorPdfAsync` sở hữu prefix/intro/artwork/background ordering | Extract một internal ordered-page sequence dùng chung cho main và preview |
| PDF validation | `IPdfDocumentInspector` + `ValidatedBookOutputPublisher` kiểm page count và physical page size | Validate preview parity trước publish |
| Output state | `BookProcessingState`, `ProductionWorkspaceState` đã lưu output path/time | Thêm optional preview publication metadata, backward-compatible |
| PDF Library | Mỗi output row đã có Open/Reveal/Copy | Thêm explicit Preview action; không liệt kê preview như deliverable thứ ba/thứ tư |
| Bridge security | Output action chỉ cho path trong current snapshot/published artifacts | Authorize typed preview reference từ matching output summary |
| Clear Cache | Giữ published PDFs | Giữ preview PDFs cùng originals |

## Artifact và persistence contract

### Published files

```text
<FinalOutputRoot>/
├─ <Book> - Cover.pdf
├─ <Book> - Cover_thumbnail.pdf
├─ <Book> - Interior.pdf
└─ <Book> - Interior_thumbnail.pdf
```

- Preview nằm cạnh PDF chính vì nó là published companion artifact, không phải processing cache.
- Preview không được thêm vào `PublishedArtifactReferences` như một production deliverable độc lập.
- Preview được liên kết typed với Cover hoặc Interior qua state/snapshot.
- Canonical filenames do publisher dựng; WebView/user input không được cung cấp destination path.

### Staging

```text
<Book>/.workspace/output-temp/<run>/
├─ main/
│  ├─ cover.pdf
│  └─ interior.pdf
└─ preview/
   ├─ cover_thumbnail.pdf
   └─ interior_thumbnail.pdf
```

- Mọi candidate được ghi trong run-specific staging.
- Main và preview dùng sibling staging directories vì publisher hiện xóa parent directory ngay sau publish; cleanup ownership phải không xóa preview candidate trước khi companion được publish.
- Publisher dùng `.pending` + atomic replace cho từng canonical file.
- Không bao giờ để UI chọn `.pending` hoặc temp file.

### Optional state metadata

Thêm một value object tương đương:

```text
PublishedPreviewPdf
- artifactKind: Cover | Interior
- previewReference
- mainOutputReference
- publishedAtUtc
- mainFingerprint
- previewFingerprint
- rasterWidth
- rasterHeight
- pageCount
```

- `BookProcessingState` lưu optional Cover/Interior preview publication metadata cho snapshot và bridge authorization.
- `ProductionOutputState` có thể lưu optional matching preview metadata để Cover/Production status dùng cùng publication marker.
- Fields additive; JSON cũ thiếu fields vẫn deserialize. Không database/migration job.

## Architecture

### Thành phần additive

- Core/Application:
  - `PreviewPdfRasterSize` constants: Cover `2726×1313`, Interior `600×609`.
  - `IPreviewPdfExporter` với Cover, Interior và full-book requests dùng cùng input lists/page sizes như exporter chính.
  - Export results trả typed preview candidate paths, không trộn vào `PublishedArtifactReferences`.
  - Published results/state thêm optional typed preview references.
- Infrastructure:
  - `PdfSharpPreviewPdfExporter` hoặc cùng adapter hiện có implement interface mới.
  - Magick.NET downsample từng raster vào temp/in-memory buffer trước khi `XGraphics.DrawImage` lên cùng physical page size.
  - Internal `BuildOrderedInteriorPageSequence` dùng chung cho main/preview để không duplicate ordering logic.
  - Publisher có methods publish preview companion độc lập sau main publication.
- Desktop:
  - `BookOutputSummary` thêm `ArtifactKind`, `PreviewArtifactReference`, `PreviewFileSizeBytes`, `PreviewState` và `PreviewGeneratedAt`.
  - PDF Library thêm Preview action; existing original actions giữ nguyên.
  - Bridge validate preview path bằng latest snapshot typed mapping.

Không thêm PDF rasterizer, database, background daemon, watcher, CDN hoặc generic rendition system.

### Interior page sequence duy nhất

Main và preview phải consume cùng một sequence đã resolve:

```text
ProductionPrefix page 1
[Background nếu configured]
ProductionPrefix page 2
[Background]
...
Intro page 1
[Background]
...
Ordered Interior artwork 1
[Background]
Ordered Interior artwork 2
[Background]
...
```

Không rebuild order từ `pageResults.First()`, `PageId`, source folder hay snapshot. Preview parity phải được bảo đảm tại exporter boundary.

## Generation và publication contract

### Cover-only

```text
validate final_cover.png
→ export/validate main Cover candidate
→ attempt export/validate Cover preview candidate từ cùng final_cover.png
   → expected preview failure: record failure và tiếp tục không có candidate
→ publish main Cover PDF
→ nếu candidate hợp lệ: publish Cover preview PDF
→ persist one shared publishedAt marker + both fingerprints
```

### Interior-only / Production Interior

```text
build OrderedBookAssembly once
→ export/validate main Interior candidate
→ attempt export/validate Interior preview candidate from same ordered sequence
   → expected preview failure: record failure và tiếp tục không có candidate
→ publish main Interior PDF
→ nếu candidate hợp lệ: publish Interior preview PDF
→ persist one shared publishedAt marker + parity metadata
```

### Full-book

```text
build assembly once
→ main Cover + main Interior candidates
→ attempt Cover preview + Interior preview candidates độc lập
→ validate main candidates; capture expected preview failures độc lập
→ publish main outputs
→ publish available preview companions independently
→ persist current preview metadata for each successful companion
```

### Failure boundary

- Main PDF validation/publication failure: không publish preview candidate; giữ previous main + previous current preview/state.
- Main PDF publish thành công nhưng preview export/validation/publication lỗi:
  - main workflow vẫn Completed/Processed;
  - clear current preview metadata cho output đó;
  - old preview file có thể còn trên disk nhưng snapshot/bridge không được authorize hoặc dùng;
  - UI Preview fallback mở main PDF.
- Cover preview failure không ảnh hưởng Interior preview và ngược lại.
- Sau main publication, preview finalization không dùng user cancellation để tạo nửa state; expected failure được record rồi state finalization vẫn hoàn tất.

## Preview PDF validation

Mỗi preview phải qua các checks sau trước khi được đánh dấu current:

1. File tồn tại, readable và là PDF hợp lệ.
2. Page count bằng main PDF.
3. First-page physical size bằng expected Cover/Interior page size trong tolerance hiện có.
4. Interior page order/content mapping test bằng distinguishable fixtures.
5. Mỗi downsampled raster được inspector xác nhận trước khi embed; exporter tests kiểm image XObject/recipe dùng target matrix:
   - Cover `2726×1313`;
   - Interior `600×609`.
6. Preview byte size phải nhỏ hơn corresponding main PDF cho production output. Nếu không nhỏ hơn, coi preview invalid và fallback main; không làm main fail.
7. State marker, main fingerprint và preview fingerprint phải khớp current files.

`length + last-write ticks` chỉ là local file fingerprint/cache key, không phải cryptographic content hash. State publication marker ngăn old preview được ghép với app-managed main publish mới.

## Memory và performance contract

- Downsample từng page theo streaming/bounded pipeline; không giữ toàn bộ Interior preview rasters trong RAM.
- Không tạo một `Task.WhenAll` mới cho tất cả thumbnail pages.
- Preview exporter tôn trọng `MaximumPageConcurrency`, nhưng mặc định ưu tiên sequential/bounded low concurrency vì main export hiện đã memory-heavy.
- Dispose Magick/PdfSharp image, stream và imported page document ngay khi page đã được append.
- Temporary downsampled rasters bị xóa sau success/failure.
- Benchmark fixtures: 1 Cover, Interior 40 pages và 100 pages.
- Thu thập: main bytes, preview bytes, compression ratio, preview generation time, peak working set và time-to-open quan sát được.
- Không hardcode compression-ratio SLA ở MVP; invariant bắt buộc là preview nhỏ hơn main và UI không chậm hơn do load preview.

## Snapshot và UI contract

### DTO

`BookOutputSummary` thêm ở cuối record:

```text
ArtifactKind: Cover | Interior
PreviewArtifactReference?: string
PreviewFileSizeBytes?: long
PreviewState: Ready | Missing | Stale | Invalid
PreviewGeneratedAt?: DateTimeOffset
```

Frontend không suy luận loại output bằng filename nữa.

### PDF Library

Mỗi Cover/Interior row vẫn đại diện **PDF chính**. Preview không xuất hiện như một output row riêng.

Actions:

- `Preview`: mở `_thumbnail.pdf` khi `PreviewState=Ready`.
- Nếu preview unavailable: `Preview` fallback mở PDF chính và hiện feedback nhẹ “Preview unavailable; opened original PDF.”
- `Open original`: mở PDF chính.
- `Reveal in Explorer`: select PDF chính.
- `Copy path`: copy path PDF chính.

Grid/list/narrow layout phải wrap actions mà không overflow. Không thêm thumbnail image, modal, embedded PDF viewer hoặc navigation mới.

### Bridge authorization

- Không thêm preview vào broad published-deliverable allowlist.
- `book.output.preview` nhận BookId + main artifact reference; server resolve preview từ latest snapshot.
- Không nhận arbitrary preview path từ DOM payload.
- Existing `book.output.open/reveal/copy-path` tiếp tục chỉ authorize main `PublishedArtifacts`.

## Clear Cache, compatibility và rollback

- Clear Cache giữ main PDFs và preview PDFs vì cả hai nằm trong final output root, không phải heavy processing cache.
- Book cũ không có preview state/file:
  - load bình thường;
  - original actions hoạt động;
  - Preview fallback main;
  - preview được sinh ở lần build/process tiếp theo.
- Không startup scan/backfill hàng loạt.
- Old app có thể thấy `_thumbnail.pdf` trong filesystem nhưng không dùng nó; production state/source không bị ảnh hưởng.
- Rollback bằng revert code; orphan preview PDFs không ảnh hưởng main PDFs và có thể xóa thủ công sau.
- Reversibility: `5/5`.

## Error & Rescue Registry

| Codepath | Failure | Rescue | User thấy |
|---|---|---|---|
| Preview raster read | source page mất/corrupt | Preview failed; main semantics hiện có | Main hoặc workflow error hiện có |
| Downsample | decode/resize/OOM | Dispose/dọn temp; main continues | Preview fallback main |
| Preview PDF write | disk/permission/lock | Dọn candidate/pending | Main usable |
| Preview validation | page count/size/order mismatch | Reject companion | Main usable; preview unavailable |
| Main publish | validate/move fail | Không publish preview | Previous current output giữ nguyên |
| Preview publish | replace fail sau main | Clear preview metadata | New main + fallback main preview |
| State persist | partial companion state | Fail closed by shared marker/fingerprints | Không authorize stale preview |
| Bridge request | arbitrary/stale path | Resolve server-side; reject | Safe error/feedback |
| Legacy Book | no preview | Optional state null | Original opens normally |
| Clear Cache | processing rasters removed | Preview PDFs preserved | Preview vẫn nhanh |

Không có row `unrescued + untested + silent` trong planned scope.

## Test plan

### Exporter unit/integration

- Cover preview: 1 page, same physical page size, raster target `2726×1313`.
- Interior preview: same page count/physical page size, every page downsample target `600×609`.
- Distinguishable Production prefix, Intro, artwork và Background fixtures prove exact ordering parity.
- Cover/Interior content edge markers remain visible in preview; no accidental crop/padding/page omission.
- 1, 40 và 100-page cases.
- Corrupt input, write failure, cancellation, cleanup and bounded-memory behavior.

### Publication/state

- Cover-only success publishes main + `_thumbnail.pdf` and shared marker.
- Interior-only and Production Interior success publish full preview.
- Full-book publishes both companion previews.
- Main failure preserves previous current pair.
- Preview failure after main success clears preview metadata and never exposes old companion.
- One companion failure does not block the other.
- Preview must be smaller than main or be marked Invalid.
- Same-path rebuild changes fingerprints and maps to new preview.

### Snapshot/bridge/UI

- `BookOutputSummary.ArtifactKind` maps without filename inference.
- Ready preview action opens typed preview.
- Missing/stale/invalid preview action opens original with feedback.
- Open original/Reveal/Copy always target main PDF.
- Arbitrary DOM-supplied preview path rejected.
- Cover-only, Interior-only, both and legacy-no-preview matrices.
- Grid/list and `<=980`, `<=760`, `<=680` widths keep actions usable.
- Output count/total size continue counting production PDFs only; preview size displayed as secondary preview metadata if useful.

### Cleanup/regression

- Clear Cache removes heavy processed cache but preserves four published PDFs where present.
- Interior production, Cover production, Book metadata/Brand assignment and existing output validation behavior unchanged.
- Relaunch with old JSON state succeeds.

### Commands dự kiến

```powershell
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
dotnet test PrintableBook.sln
```

## Phases triển khai

### Phase 1 — Preview PDF contract và ordered sequence

- Thêm target sizes, typed requests/results/state records.
- Extract internal ordered Interior page sequence dùng chung cho main/preview.
- Implement downsample + PDF writing + parity validation.

### Phase 2 — Cover preview integration

- Wire Cover-only và full-book Cover export.
- Publish `<Book> - Cover_thumbnail.pdf` sau main Cover.
- Persist/clear current Cover preview metadata theo failure boundary.

### Phase 3 — Interior preview integration

- Wire Interior-only, Production Interior và full-book Interior export.
- Publish full `<Book> - Interior_thumbnail.pdf` từ same ordered sequence.
- Test full page parity, background insertion, isolation và memory bounds.

### Phase 4 — Snapshot, bridge và PDF Library

- Project typed preview state/reference vào matching main output row.
- Thêm server-resolved `book.output.preview`.
- Add Preview + Open original actions; preserve Reveal/Copy original behavior.
- Responsive and fallback tests.

### Phase 5 — Cleanup regression, docs và benchmark smoke

- Lock Clear Cache preservation behavior.
- Update architecture/user guide with main-vs-preview distinction.
- Run 1/40/100-page benchmark and portable smoke: build, preview, rebuild, partial failure, relaunch, cleanup.

## Implementation Tasks

- [x] **T1 (P1, human: ~5h / CC: ~60m)** — PDF — Shared ordered sequence + bounded low-resolution preview exporter + parity tests.
- [x] **T2 (P1, human: ~3h / CC: ~35m)** — Cover — Cover-only/full-book companion publication and state provenance.
- [x] **T3 (P1, human: ~5h / CC: ~60m)** — Interior — All Interior modes, page-order parity, failure isolation and 40/100-page smoke tests.
- [x] **T4 (P1, human: ~4h / CC: ~45m)** — Desktop — Typed snapshot mapping, secure preview command and PDF Library actions/fallback.
- [x] **T5 (P2, human: ~2h / CC: ~25m)** — QA/Docs — Cleanup regression, compatibility, docs and smoke validation.

Sequential implementation is preferred: T2/T3 share exporter/publication contracts; T4 depends on their state model; T5 closes the feature.

## NOT in scope

- PNG/JPEG thumbnail artifacts.
- Per-page thumbnail files for Interior Pages.
- First-page-only Interior preview.
- PDF rasterization from already-published PDFs.
- Embedded PDF viewer, zoom/lightbox/editor.
- User-configurable size/DPI/quality.
- Multiple preview resolutions, WebP/JPEG variants or generic rendition platform.
- Background backfill/watcher/daemon.
- Replacing production PDF with preview PDF in Reveal/Copy/delivery workflow.
- Refactor lớn processing pipeline hoặc output storage.

## Decision Audit Trail

| # | Phase | Quyết định | Lý do |
|---:|---|---|---|
| 1 | User clarification | Thumbnail là full PDF companion | Sửa hiểu nhầm ban đầu về image/per-page thumbnail |
| 2 | Product | Interior preview giữ toàn bộ pages/order | Preview phải giống Interior chính, chỉ nhẹ hơn |
| 3 | Architecture | Generate từ same ordered raster inputs | Không cần PDF rasterizer và bảo đảm parity |
| 4 | Architecture | Same physical page size; smaller embedded raster | Visual/layout giống main PDF |
| 5 | Reliability | Main success không phụ thuộc preview success | Preview optimization không phá production |
| 6 | Persistence | Typed companion state, không broad deliverable list | Chặn stale preview và tránh nhầm file giao hàng |
| 7 | UI | Explicit Preview; original actions giữ main | Nhanh nhưng không ghi đè semantics hiện tại |
| 8 | Performance | Bounded/streaming page processing | Tránh nhân đôi peak memory với Interior dài |
| 9 | Compatibility | No mandatory backfill | Additive, legacy Books vẫn hoạt động |

## Autoplan review

### CEO review

- Premise correction applied: scope đổi từ image thumbnail sang full companion PDF sau user clarification.
- Narrowest complete product: two typed preview PDFs; không mở rộng embedded viewer/rendition platform.
- Failure/rescue registry: 10 paths, 0 critical gap.
- Reversibility: 5/5; no migration.

### Design review

- Reuse PDF Library output rows, không tạo row/card riêng cho preview companions.
- Preview action explicit; original actions không bị ghi đè âm thầm.
- Missing/stale preview fallback main với visible lightweight feedback.
- Responsive action wrapping và all states đã đưa vào test matrix.

### Engineering review

- Same ordered source sequence là invariant cốt lõi.
- Main/preview có failure boundary riêng nhưng shared publication provenance.
- Bounded page downsampling tránh duplicate full-document memory.
- Typed snapshot/bridge authorization tránh arbitrary-path và preview-as-deliverable bugs.
- 5 implementation tasks, 0 planned critical gap.
- DX review skipped: không có public API/CLI/onboarding developer-facing mới.

## Unresolved decisions

Không còn quyết định blocking trước implementation.

## GSTACK REVIEW REPORT

| Review | Trigger | Runs | Status | Findings |
|---|---|---:|---|---|
| CEO Review | `/plan-ceo-review` via `/autoplan` | 1 | CLEAR | Corrected full-PDF scope; 0 critical gaps |
| Independent Codex | `/codex review` via `/autoplan` | 1 | PARTIAL / INTEGRATED | Repository inspection findings on ordering, provenance and failure boundaries integrated |
| Design Review | `/plan-design-review` via `/autoplan` | 1 | CLEAR | Explicit Preview without duplicate output rows |
| Eng Review | `/plan-eng-review` via `/autoplan` | 1 | CLEAR | Export/publication/state/performance matrix closed |
| DX Review | `/plan-devex-review` via `/autoplan` | 0 | SKIPPED | No developer-facing scope |

- **VERDICT:** CEO + DESIGN + ENG CLEARED — plan-only phase complete.

NO UNRESOLVED DECISIONS
