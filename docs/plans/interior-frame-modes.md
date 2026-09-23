<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\plan-interior-frame-modes-autoplan-restore-20260923-103325.md -->

# Kế hoạch MVP: chuẩn hóa Interior thành Frame / No Frame

## Trạng thái tài liệu

- Branch triển khai: `feat/interior-frame-no-auto`, tạo từ `main` tại `395f2e7` (`v0.2.3`).
- Implementation và automated regression đã hoàn tất ngày 2026-09-23; manual artifact smoke vẫn là release gate riêng.
- Review `autoplan` đã hoàn tất qua CEO, Design, Engineering, DX và outside voice; giữ scope hẹp, additive/minimal diff, không refactor pipeline.

## Kết quả cần đạt

- Interior thông thường chỉ có đúng hai lựa chọn user-facing: `Frame` và `No Frame`.
- `No Frame` là mặc định cho Book/page mới, page không có override và mọi state legacy `Auto`.
- `Frame` luôn có nghĩa là frame được áp dụng; nếu Brand frame thiếu hoặc không còn hợp lệ thì chặn/fail rõ ràng, không âm thầm xuất bản No Frame.
- `No Frame` tiếp tục dùng semantics hiện tại: bỏ detector cho quyết định frame và đi theo forced `CropArt` policy.
- Intro template và Production Interior vẫn bị ép No Frame bằng policy nội bộ; chúng không trở thành lựa chọn thứ ba của Interior thông thường.

## Quyết định sản phẩm đã chốt

| Chủ đề | Quyết định |
|---|---|
| Default mới | `No Frame` |
| Số trạng thái | Đúng 2: `Frame`, `No Frame` |
| Legacy `Auto` | Hard cutover sang `No Frame` |
| Missing override | Resolve thành `No Frame` |
| Visual drift | Chấp nhận thay đổi output khi reprocess; PDF đã publish không tự đổi |
| `Frame` thiếu asset | Chặn và báo lỗi; không fallback |
| Cảnh báo migration | Có cảnh báo không chặn và release note rõ ràng |
| Detector/classifier | Giữ lại cho nhánh `Frame`; không refactor/xóa trong MVP |
| Brand thiếu `frame.png` dù toàn bộ page là No Frame | Giữ validation Brand hiện tại trong MVP; Book vẫn chưa Ready |
| Downgrade về app cũ | Không được hỗ trợ sau khi state contract v2 được ghi; release/update flow phải cảnh báo và không tự rollback binary |

## Hiện trạng codebase

- `FrameMode` đang có `Auto`, `Enabled`, `Disabled`.
- `BookProcessingState.GetInteriorFrameMode()` trả `Auto` khi không có override; `SetInteriorFrameMode(..., Auto)` xóa override.
- Workspace state dùng `JsonStringEnumConverter`, vì vậy xóa enum `Auto` trực tiếp sẽ làm JSON legacy `"auto"` có nguy cơ không deserialize được.
- Queue processor còn fallback `FrameMode.Auto` khi chưa có prior state.
- Pipeline hiện có ba behavior:
  - `Auto`: detected policy và chỉ apply frame khi detector khuyến nghị.
  - `Enabled`: detected policy và apply frame nếu file tồn tại.
  - `Disabled`: forced No Frame/CropArt và bỏ detector.
- `Enabled` hiện có thể âm thầm tạo output không frame khi file frame biến mất vì `ShouldApplyFrame` phụ thuộc `frameAvailable`.
- Frontend mặc định filter `assetFrameMode = "auto"`; normalizer, filter chips, bulk selector, badge và event handlers đều chứa ba giá trị.
- Intro/Production đã dùng `Disabled` như policy bắt buộc và cần được giữ tách biệt với lựa chọn normal Interior.

## Contract trạng thái đích

| User label | Giá trị nội bộ đề xuất | Default CLR | Persistence | Processing |
|---|---|---:|---|---|
| `No Frame` | `FrameMode.Disabled = 0` | Có | Sparse: không lưu override; missing resolve No Frame | forced `CropArt`, không chạy detector quyết định frame, không overlay |
| `Frame` | `FrameMode.Enabled = 1` | Không | Canonical writer lưu explicit `"enabled"` theo page | dùng detected preparation hiện tại, bắt buộc overlay Brand frame hợp lệ |

Không giữ `Auto` trong enum/runtime contract của normal Interior. Tên nội bộ `Enabled` / `Disabled` được giữ để giảm diff; UI tiếp tục hiển thị `Frame` / `No Frame`.

## Migration và persistence

### Versioning và provenance

- Thêm `FrameModeContractVersion = 2` vào workspace state. State không có version là legacy v1; state có version lớn hơn app hỗ trợ phải fail rõ, không đoán.
- Bổ sung load-result additive, ví dụ `BookWorkspaceStateLoadResult(State, ContractVersion, LegacyFrameContractDetected, ExplicitLegacyFrameKeys, Diagnostics)`. Giữ wrapper `LoadAsync()` trả `State` cho caller không cần metadata để giới hạn diff; snapshot dùng load-result đầy đủ.
- Với state v1, missing key từng mang nghĩa `Auto`. Vì vậy snapshot phải join provenance với danh sách normal Interior vừa discover: mọi page v1 không có explicit `Enabled`/`Disabled`, cùng các page explicit `Auto`, đều được tính vào cảnh báo hard-cutover. Intro/Production không được tính.
- Warning là dữ liệu transient trong `BookDesktopSummary`, không chèn vào `BookProcessingState` và không persist.
- Canonical save ghi contract v2 và chỉ giữ explicit `enabled`; `disabled`/missing cùng nghĩa No Frame. Downgrade vẫn unsupported vì binary cũ hiểu missing là Auto.

### Ma trận đọc legacy

| Giá trị cũ | Kết quả runtime | Ghi lại lần save kế tiếp |
|---|---|---|
| Missing key / missing dictionary trong state v1 | `Disabled` / No Frame + legacy provenance | Không ghi override |
| `"auto"` (không phân biệt hoa/thường) | `Disabled` / No Frame + legacy provenance | Không ghi override |
| numeric integer `0` (legacy Auto) | `Disabled` / No Frame + legacy provenance | Không ghi override |
| `"disabled"` hoặc numeric integer `2` | `Disabled` / No Frame | Không ghi override |
| `"enabled"` hoặc numeric `1` | `Enabled` / Frame | Ghi canonical `"enabled"` |
| Unknown string/integer, fractional/overflow number, `null`, bool, object/array | Fail load bằng lỗi chẩn đoán rõ có Book/state-file context | Không tự sửa dữ liệu không thuộc legacy contract |

- Source key rỗng/whitespace là corrupt. Key chỉ khác nhau về case được collapse nếu cùng normalized value; nếu conflict thì fail như corrupt thay vì chọn theo thứ tự JSON.
- State v2 chỉ chấp nhận canonical string `"enabled"`/`"disabled"`; legacy numeric/`auto` chỉ được dung nạp ở v1 để contract không tiếp tục nới lỏng vô hạn.

### Cách triển khai persistence

- Thêm compatibility reader/converter tập trung tại workspace state store; không rải mapping legacy qua UI và pipeline.
- Sau deserialize, normalize dictionary case-insensitive về đúng hai mode; canonicalize ở cả load và save, không chỉ constructor.
- Writer phát contract v2, chỉ lưu explicit `enabled` override và tuyệt đối không ghi `disabled`, `auto` hoặc numeric enum.
- Không chạy migration hàng loạt. State được canonicalize khi Book được load rồi save theo workflow bình thường.
- Nếu load state v1, gắn transient migration notice vào snapshot sau khi đối chiếu asset discovery, đồng thời ghi diagnostic đủ Book/source; không mở modal migration.
- Unknown/corrupt value không thuộc ma trận legacy phải fail rõ thay vì âm thầm biến dữ liệu hỏng thành No Frame.

## Luồng dữ liệu và ranh giới trách nhiệm

```text
workspace-state.json
        |
        v
[versioned compatibility reader + load result]
  legacy/missing Auto -----> Disabled (No Frame)
  Enabled -----------------> Enabled (Frame)
        |
        v
[BookProcessingState: binary invariant]
        |
        +--> [Application snapshot DTO] --> [Bridge: "enabled"|"disabled"]
        |                              --> [UI: Frame|No Frame]
        |
        +--> [Queue processor]
                 |
                 +--> No Frame --> forced CropArt --> no overlay
                 |
                 +--> Frame --> detected preparation
                                --> require validated Brand frame descriptor
                                --> overlay OR fail clearly
```

Ranh giới invariant:

- Compatibility chỉ tồn tại ở read boundary; canonical writer luôn phát state v2.
- Domain, snapshot, bridge, draft state, queue request và pipeline normal Interior chỉ nhìn thấy hai giá trị.
- Wire DTO dùng string explicit; không serialize trực tiếp ordinal của enum và không đổi global enum serializer của các contract khác.
- `frameMode` chỉ có trên normal Interior summary (hoặc nullable cho asset khác), không dùng `Auto` làm sentinel cho Cover/Representative.
- Intro/Production forced No Frame là processing policy nội bộ, không được đưa vào selector/filter của normal Interior.

## UI/UX contract

### Information architecture

- Giữ nguyên Book Detail → Interior artwork workspace; không thêm màn hình, wizard hay modal.
- Giữ interaction hiện tại: card dùng để chọn artwork, còn mode được áp dụng qua bulk toolbar; toolbar chỉ cung cấp `Frame` / `No Frame`.
- Filter frame mode có explicit `All`, `Frame`, `No Frame`; `All` là filter-only state, không phải mode thứ ba.
- Khi mở Book/artwork workspace, filter mặc định là `All`, không mặc định lọc No Frame để tránh che mất page Frame.
- Bulk selector giữ `No change`, `Frame`, `No Frame`; bỏ `Auto`.
- Badge tổng hợp Book: tất cả Frame → `Frame`; tất cả No Frame → `No Frame`; có cả hai → `Mixed`; không có page → giữ trạng thái empty/needs-review hiện tại.

### Interaction states

| State | UI behavior |
|---|---|
| Book/page mới | Hiển thị `No Frame` ngay từ snapshot/default; không cần user save để có default |
| Legacy state v1 vừa load | Hiển thị `No Frame`; warning tính cả page v1 missing override/explicit Auto, nêu số page bị ảnh hưởng và action chuyển tới Interior artwork; không giữ badge Auto |
| User chọn Frame | Stage draft như hiện tại; Save persist `Enabled` override |
| User chọn No Frame | Stage draft như hiện tại; Save xóa override tương ứng (canonical sparse v2) |
| Frame asset hợp lệ | Cho process theo workflow hiện có |
| Frame asset thiếu/stale | Vẫn cho Save lựa chọn Frame, nhưng Brand validation hiện tại làm Book chưa Ready; alert nêu Brand/frame cần sửa, pipeline fail closed nếu bị bypass/race |
| Save/process lỗi | Inline `role=alert`, giữ draft/focus để user sửa hoặc retry |
| Filter không có kết quả | Nêu filter đang áp dụng và có action `Clear filters` |
| Processing đang lock | Disable controls và hiển thị lý do, không chỉ đổi visual state |

Luồng edit được giữ rõ ràng:

```text
select cards -> Apply to selected (stage draft) -> Unsaved changes -> Save
       |                  |                            |
       |                  +--> filter đổi nhưng selection/count vẫn rõ
       +--> select-all hỗ trợ checked/unchecked/indeterminate
                                                  Save fail -> giữ draft + focus
                                                  Save success -> clear draft + refresh snapshot
```

- Giữ close-with-unsaved confirmation hiện có; không thêm Reset/Undo action mới trong MVP.
- Khi selection bị ẩn bởi filter, toolbar vẫn hiển thị tổng số selected và số selected đang visible.
- Inline migration warning chỉ xuất hiện trên Book state v1 có normal Interior page bị hard-cutover (kể cả missing override); đây là cảnh báo theo context, không phải banner toàn app.

### Accessibility và responsive

- Tái sử dụng button/chip/select hiện có; filter button có `type="button"`, `role="group"`, `aria-pressed`, label rõ và status announcement.
- Accessible name của artwork selection gồm filename + Active/Inactive + Frame/No Frame; alert/status dùng `aria-live` phù hợp.
- Giữ keyboard focus/search/scroll restore qua dynamic redraw theo pattern hiện có.
- Cho phép CSS chỉnh hẹp trong workspace: toolbar/filter wrap dưới 980px, grid hai cột ở 681–980px và một cột ở ≤680px nếu test cho thấy overflow; chỉ giữ một scroll container chủ đích.
- Không dùng màu đơn lẻ để phân biệt `Frame`, `No Frame`, `Mixed`.
- Chuẩn hóa user-facing label thành Title Case `No Frame`.

## Processing contract

- Queue fallback khi `priorState` null phải là `Disabled`, không phải `Auto`.
- Normal Interior `Disabled` giữ nguyên forced No Frame/CropArt path.
- Normal Interior `Enabled` giữ detected preparation hiện tại nhưng overlay là bắt buộc.
- Giữ global Brand readiness hiện tại: mọi processing session vẫn cần Brand hợp lệ, kể cả khi toàn bộ page là No Frame; đây là constraint ngoài frame-mode và chưa tách theo operation trong MVP.
- Trước khi xử lý từng Book có active Frame page, atomically copy Brand frame vào file bất biến thuộc run hiện tại, ví dụ `<book>/.workspace/cache/_frame-input/<run-id>/frame.png`; inspect/hash chính file staged này một lần.
- Descriptor tối thiểu là `ValidatedFrameAsset(StagedFile, Sha256, Size)`. Mọi request Frame trong Book phải overlay đúng `StagedFile` và stamp đúng digest đó; không được quay lại đọc `brands/<Brand>/frame.png`, loại TOCTOU giữa hash/cache/overlay.
- Pipeline validate descriptor trước mọi cache-success return. Cleanup thư mục run theo best effort khi Book kết thúc; Cache Cleanup phải dọn staging run mồ côi mà không đụng output publish.
- Thêm structured failure code (ví dụ `INTERIOR_FRAME_REQUIRED`, `INTERIOR_FRAME_INVALID`, `WORKSPACE_STATE_CORRUPT`) cùng safe detail Book/page/Brand/frame reference. Queue phải giữ code/detail thay vì chỉ lưu outer exception message.
- Đưa state load của queue vào structured error boundary. Snapshot đánh dấu Book corrupt là `state unavailable`, disable selection/actions; stale request có chứa Book đó bị preflight reject rõ ràng. Không đổi semantics batch thành “process phần còn lại” trong MVP; Book healthy vẫn load và process được trong request riêng.
- Khi một page fail, không publish PDF/output Book. Partial page cache đã hoàn tất được phép giữ và retry chỉ reuse khi stamp/digest còn hợp lệ.
- Không xóa `AutoFrameRecommended`, border detector hoặc classification metadata trong phase này; chúng vẫn có thể là chi tiết thuật toán của preparation path.

## Cache và output compatibility

- Tận dụng comparison `ClassificationPolicy` hiện có: cache Auto (`detected`) khi request mới là No Frame (`forced-no-frame`) phải invalidate từ classification/preparation/frame/downstream.
- Bump cache schema vì stamp Frame phải chuyển từ identity yếu path/length/timestamp sang content digest của frame đã validate/stage.
- Cache `enabled` chỉ được reuse nếu mode, processing policy và frame content digest match; validation phải chạy trước fast return.
- Seed regression fixture có readable unframed final output + cache stamp cũ `Enabled` nhưng frame missing: request phải fail, không được trả cache hit.
- Test thêm frame corrupt, sai geometry và file bị thay nội dung nhưng giữ nguyên length/timestamp; digest phải invalidate. Không hash/decode lại độc lập cho từng page trong cùng Book run.
- PDF đã publish không bị sửa/xóa tự động. Output thay đổi chỉ khi user process/publish lại sau cutover.
- Release note phải nêu rõ: Book cũ dùng Auto sẽ trở thành No Frame ở lần load/reprocess kế tiếp.

## Failure Modes Registry

| Failure | Guard/error handling | User impact | Test bắt buộc | Critical gap sau plan |
|---|---|---|---|---|
| State v1 thiếu toàn bộ override | Load provenance + asset join coi page normal là legacy Auto | Book load thành No Frame, có warning đúng count | Persistence/snapshot regression | Không |
| JSON chứa `"auto"` sau khi enum đổi | Compatibility reader map sang Disabled | Book load bình thường, có warning | Persistence regression | Không |
| JSON numeric legacy `0/1/2` | Mapping explicit theo ma trận | Không crash/mis-map | Persistence theory | Không |
| Một fallback `Auto` còn sót | Binary enum + exhaustive search/test | Có thể tạo behavior thứ ba ẩn | Contract/search regression | Không |
| Frame bị xóa sau validation | Immutable per-Book staged descriptor + guard trước cache return | Process fail rõ, không xuất sai | Pipeline race/missing-file test | Không |
| Cache Auto/cached unframed Enabled cũ được reuse | Schema + policy/digest mismatch phải invalidate; validation precedes fast return | Output có thể sai | Seeded cache regression | Không |
| Snapshot/bridge serialize enum ordinal | Dedicated string wire DTO/converter | UI không mis-map `0` thành Auto | Final JSON wire test | Không |
| Một state file corrupt | Book-isolated snapshot error + disable actions; stale request reject | Book khác vẫn dùng/process riêng được | Multi-book snapshot + separate healthy process test | Không |
| Save/process redraw mất draft/focus | Reuse draft/save lifecycle hiện có | User phải nhập lại | Desktop UI regression | Không |
| Published PDF cũ khác lần reprocess | Release warning; không auto republish | Visual edition drift có chủ ý | Manual release acceptance | Không |

## Error & Rescue Registry

| Boundary | Error | Rescue contract |
|---|---|---|
| State load | state v1 Auto/missing | Normalize No Frame, warning không chặn qua load-result provenance |
| State load | corrupt unknown mode/token/key collision | Không ghi đè file; snapshot chỉ degrade Book đó thành `state unavailable/corrupt`, disable selection/mutation; stale mixed request reject toàn session theo preflight hiện có |
| Bridge command | `auto` hoặc unknown từ client mới | Reject bằng error code hiện có; không persist |
| Save | persistence failure | Giữ draft/focus và hiển thị inline feedback hiện có |
| Process | Frame reference null/missing/unreadable/wrong geometry | Structured task failure có Book/page/Brand và hướng sửa; không fallback No Frame |
| Cache | stamp/schema cũ hoặc digest mismatch | Recompute dependency chain; validation không được đặt sau cache fast return |
| Downgrade | mở state v2 bằng app cũ | Unsupported; updater không tự rollback binary, release note yêu cầu backup và không reprocess bằng bản cũ |

## Test coverage map

```text
CODE PATHS                                           USER FLOWS
[State compatibility reader]                        [Open Book mới]
  |- missing override -> Disabled [GAP]                `- thấy No Frame [GAP -> integration]
  |- v1 missing + string/numeric legacy [GAP]         [Open legacy Auto Book]
  |- malformed token/key collision [GAP]                |- load thành No Frame [GAP -> integration]
  `- corrupt -> Book-isolated error [GAP]               `- warning/count không chặn [GAP]

[Domain binary state]                               [Edit một/nhiều artwork]
  |- default Disabled [GAP]                            |- Frame/No Frame draft [existing, update]
  |- Enabled stored [existing, update]                 |- bulk No change/Frame/No Frame [GAP]
  `- Disabled removes override [GAP]                   `- save/redraw giữ state/focus [regression]

[Bridge + snapshot]                                 [Filter artwork]
  |- final wire JSON uses strings only [GAP]            |- All default [GAP]
  |- accept enabled|disabled [existing, update]        |- Frame / No Frame / empty [GAP]
  |- non-Interior has null/no frameMode [GAP]           `- keyboard/aria state [manual]
  `- reject auto/unknown [GAP]

[Pipeline]
  |- Disabled -> forced CropArt/no overlay [existing, retain]
  |- Enabled + valid frame -> overlay [existing, strengthen]
  |- Enabled + null/missing/unreadable -> fail [GAP, CRITICAL]
  |- validation runs before cache hit [GAP, CRITICAL]
  |- frame content digest invalidates cache [GAP]
  |- Intro/Production -> internal Disabled [existing, retain]
  `- legacy cache stamp -> recompute [GAP, CRITICAL]

[Failure propagation]                               [Corrupt one of many Books]
  |- structured code + safe details [GAP]              |- healthy Books still render/use [GAP]
  |- queue state load inside failure boundary [GAP]     `- corrupt Book is blocked/actionable [GAP]
  `- no publish after partial batch failure [GAP]
```

Mọi `[GAP]` là test phải bổ sung/cập nhật trong implementation; hai regression critical chặn merge.

## Kế hoạch triển khai theo phase

### Phase 1 — Atomic binary cutover (domain, persistence, C# consumers và wire/client)

- Trong cùng một compile/release unit, đổi `FrameMode` thành hai member (`Disabled = 0`), rồi cập nhật mọi C# reference ở state, snapshot, bridge, queue, pipeline, factory/test helper; không commit enum removal khi còn consumer `Auto`.
- Cũng trong unit này, cutover wire/client tối thiểu: dedicated string DTO, JS parser/default và command validation chỉ nhận hai mode. UI polish có thể theo Phase 2 nhưng không được có build trung gian gửi/nhận ordinal cũ.
- Thêm `FrameModeContractVersion = 2`, load-result có provenance và canonical writer sparse Enabled-only.
- Thêm compatibility deserialize v1 cho string/numeric legacy theo ma trận; canonicalize dictionary ở cả load/save.
- Snapshot join provenance v1 với normal Interior discovery để cảnh báo cả missing override; không persist notice.
- Bọc corrupt state bằng Book/path context và degrade riêng Book đó; state lỗi không bị rewrite.
- Loại nhánh Auto khỏi pipeline/cache mode, đặt fallback No Frame và thêm fail-closed guard cơ bản cho `Enabled`; digest/staging strengthening hoàn tất ở Phase 3.
- Test domain default/set/remove, unversioned state không có dictionary, token matrix, key collision, final wire JSON và build toàn solution.

### Phase 2 — UI/UX hai trạng thái và migration recovery

- Hoàn thiện drafts/filter/bulk/badge/event handler chỉ còn Frame/No Frame; contract tối thiểu đã cutover atomically ở Phase 1.
- Không gán mode giả cho Cover/Representative; `frameMode` null/absent ngoài normal Interior và đã có regression từ Phase 1.
- Default filter explicit `All`; bổ sung `Mixed` aggregation.
- Hiển thị warning provenance v1; Book corrupt có summary/actionable alert nhưng selection/mutation/process bị disable.
- Inline readiness/save errors phải actionable và giữ draft/focus.
- Kiểm tra empty state, keyboard, screen-reader labels và responsive breakpoint.

### Phase 3 — Pipeline fail-closed và cache invalidation

- Giữ hai policy processing hiện có: forced No Frame/CropArt và detected preparation + required overlay.
- Loại nhánh Auto khỏi `ShouldApplyFrame` và canonical cache mode.
- Giữ global Brand readiness hiện tại; atomically stage/validate/hash frame một lần mỗi Book run và truyền `ValidatedFrameAsset` cho request Frame.
- Thêm invariant/guard tại session/request và race guard trước cache fast return; thêm structured failure code/detail xuyên queue/UI.
- Bump cache schema, đưa frame content digest vào stamp và giữ policy mismatch để invalidate Auto legacy.
- Xác nhận partial page cache có thể giữ nhưng Book output/PDF không publish khi batch fail.
- Regression test valid Frame; null/missing/corrupt/wrong-size/same-metadata-replaced Frame; seeded stale cache; No Frame detector bypass; Intro/Production.

### Phase 4 — Documentation và release validation

- Cập nhật README/user guide/architecture/changelog: chỉ hai trạng thái, default No Frame và hard cutover Auto.
- Nêu rõ PDF đã publish không tự đổi; lần reprocess có thể đổi output.
- Nêu state contract v2, downgrade unsupported và updater không tự rollback về binary không hiểu contract mới.
- Chạy full solution tests, frontend contract/static tests và manual smoke trên Book mới + fixture legacy.
- Không auto-republish hay rewrite hàng loạt workspace.

## Files dự kiến tác động

- Core: `FrameMode.cs`, `BookProcessingState.cs`, state-store abstraction/load-result, request/queue/snapshot/interior settings/background-task services.
- Infrastructure: `JsonBookWorkspaceStateStore.cs`, `DiskBackedInteriorPagePipeline.cs`, frame staging/validation/fingerprint path hiện có.
- Desktop: `MainWindow.xaml.cs` wire serialization, `WebViewBridgeRouter.cs`, `Frontend/js/app.js` và test contract liên quan.
- Tests: Core, Infrastructure, Desktop suites cho frame mode/state/pipeline/bridge/snapshot.
- Docs: `README.md`, `docs/user-guide.md`, `docs/architecture.md`, `docs/interior-artwork-preparation-v1.md`, `docs/interior-shared-pipeline-integration.md`, `CHANGELOG.md` nếu release workflow yêu cầu.

## What already exists

- Per-page mode dictionary, case-insensitive source keys và save service hiện có sẽ được tái sử dụng; persistence chuyển sang explicit v2 để provenance/downgrade rõ ràng.
- `Disabled` đã có đúng semantics No Frame/CropArt cần giữ.
- Detector/classifier, `ClassificationPolicy`, cache stamp và downstream invalidation đã tồn tại; ưu tiên reuse, không xây pipeline mới hay bump schema không cần thiết.
- Brand validation, geometry check, frame resolver/fingerprint và background task feedback đã tồn tại; tái sử dụng thay vì tạo khái niệm certification mới, đồng thời tăng identity lên content digest cho cache correctness.
- Frontend đã có per-page edit, bulk edit, filter chips, draft/save và empty state; chỉ thu gọn option/default.

## NOT in scope

- Thiết kế thêm kiểu frame thứ ba hoặc giữ hidden Auto.
- Xóa detector/classifier hay đổi thuật toán prepare/CropArt.
- Refactor lớn state store, processing pipeline hoặc frontend architecture.
- Tách Brand readiness theo operation để No Frame được process khi Brand thiếu `frame.png`.
- Proactive rewrite toàn bộ workspace state.
- Auto-reprocess/auto-republish PDF cũ.
- Migration wizard/modal hoặc per-Book grandfathering.
- Thay đổi Intro/Production workflow ngoài việc giữ invariant forced No Frame.

## Parallelization

Ưu tiên tuần tự vì Core contract là dependency của tất cả lane:

```text
Phase 1: atomic contract cutover across domain/persistence/pipeline/wire/client
        |
        +--> Phase 2: snapshot/bridge/UI
        |
        `--> Phase 3: pipeline/cache
                    |
                    v
             Phase 4: docs + full validation
```

Sau Phase 1, Phase 2 và Phase 3 có thể chạy song song ở worktree riêng; chúng gặp nhau ở shared test helpers nên merge tuần tự và chạy full suite sau khi hợp nhất.

## Implementation handoff (DX review)

### Phase gates và thứ tự commit đề xuất

1. `feat(state): add versioned frame-mode compatibility`
   - Giữ code compile với enum hiện tại; thêm reader/provenance/fixtures trước, chưa bật hard cutover độc lập trong release.
2. `feat(interior): cut over atomically to Frame and No Frame`
   - Trong một commit compile được: xóa Auto và cập nhật toàn bộ C# consumers, pipeline basic guard, wire serializer/parser, JS defaults/commands. Final WebView JSON phải được test, không chỉ response object.
3. `fix(processing): stage and verify required interior frames`
   - Cache schema/digest, immutable staged input và structured failures phải land cùng nhau để không có trạng thái nửa vời.
4. `docs: document the interior frame mode cutover`
   - Chỉ land sau full regression/manual migration smoke để docs phản ánh behavior đã chứng minh.

Không release commit 1 riêng như behavior hoàn chỉnh; commit 2 là atomic cutover gate. Không bao giờ có commit xóa enum `Auto` nhưng còn C#/JS consumer tham chiếu nó.

### Error contract có thể kiểm thử

| Code | Boundary | Message/user action bắt buộc | Diagnostic giữ trong log |
|---|---|---|---|
| `WORKSPACE_STATE_CORRUPT` | state load | Nêu Book không khả dụng, state file không bị sửa và cần restore/fix file | Book ID, exact path, JSON token/key và inner exception |
| `INTERIOR_FRAME_REQUIRED` | session/request | Nêu Book/page đang chọn Frame, Brand nào cần Validate/sửa `frame.png`, rồi retry | Book ID, page/source key, Brand ID, frame reference |
| `INTERIOR_FRAME_INVALID` | frame validation/race guard | Nêu missing/unreadable/wrong geometry; không nói chung chung “page failed” | failure subtype, expected/actual geometry, digest nếu đã có |
| `INVALID_FRAME_MODE` | bridge command | Chỉ nhận `enabled` hoặc `disabled`; yêu cầu refresh client nếu gửi `auto`/unknown | command ID, safe raw token; không log cả payload/path không cần thiết |

UI hiển thị message sửa được; task/log giữ code và safe detail. Outer `InteriorPageProcessingException` không được làm mất code/cause từ inner failure.

Carrier tối thiểu: `ProcessingFailure(Code, Message, Context?)`, với `ProcessingFailureContext(BookId?, PageId?, BrandName?, AssetRelativePath?)`. `ProcessQueueEntry` phải mang `FailureCode` + safe `FailureContext` thay vì chỉ `Detail`; `BackgroundTaskSnapshot` tiếp tục dùng `ErrorCode/ErrorMessage` ở task level. Không gửi absolute path ra UI nếu relative path đủ để sửa lỗi.

### Fixtures và validation commands

Thêm raw fixtures dưới `tests/PrintableBook.Infrastructure.Tests/TestData/WorkspaceState/`:

- `v1-no-frame-overrides.json`: state unversioned không có dictionary — tất cả normal Interior page là legacy Auto provenance.
- `v1-explicit-mixed.json`: `auto`/`enabled`/`disabled` và numeric `0/1/2`, gồm casing hợp lệ.
- `v1-conflicting-case-keys.json`: hai source key chỉ khác case nhưng mode conflict — phải corrupt.
- `v1-malformed-frame-values.json`: fractional/overflow/null/bool/object/array/unknown — theory hoặc fixture-driven failures.
- `v2-canonical.json`: sparse Enabled-only, round-trip ổn định và không phát `disabled`, numeric hoặc `auto`.
- Cache fixture: v4-style `Enabled` stamp + readable unframed final page trong khi frame missing — phải fail trước cache hit.

Fast feedback theo lane:

```powershell
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --filter "FullyQualifiedName~BookProcessingStateTests|FullyQualifiedName~ApplicationSnapshotServiceTests|FullyQualifiedName~InteriorPagePipelineRequestTests|FullyQualifiedName~BookProcessingQueueProcessorTests"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --filter "FullyQualifiedName~JsonBookWorkspaceStateStoreTests|FullyQualifiedName~DiskBackedInteriorPagePipelineTests"
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --filter "FullyQualifiedName~BridgeMessageContractTests|FullyQualifiedName~ProcessingSessionTaskManagerIntegrationTests"
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
```

Merge gate dùng full commands trong `README.md#build--test`; LocalCorpus tiếp tục opt-in và không chặn CI. Manual smoke bắt buộc gồm Book mới, state v1 không override, mixed Frame/No Frame, một Book corrupt cạnh một Book healthy (healthy vẫn process được khi chọn riêng; stale request chứa corrupt Book bị reject), Frame bị xóa sau validation và reprocess output drift.

### DX scorecard áp dụng cho người triển khai

| Dimension | Trước review | Sau plan | Ghi chú |
|---|---:|---:|---|
| Getting started / tìm test | 7/10 | 9/10 | Có command theo lane và full-suite gate |
| Contract/API clarity | 5/10 | 9/10 | Versioned state, string wire DTO, binary domain invariant |
| Error/debugging | 4/10 | 9/10 | Stable code + actionable UI + retained diagnostics |
| Documentation | 7/10 | 9/10 | Chỉ rõ năm tài liệu cần đồng bộ và cutover note |
| Upgrade/migration | 4/10 | 9/10 | Provenance cho missing key, raw fixtures, downgrade policy |
| Tooling/testability | 7/10 | 9/10 | Targeted filters, cache seed và Book-isolated corruption |
| Ecosystem/community | N/A | N/A | Desktop app nội bộ; không mở API/ecosystem mới |
| Feedback loop | 6/10 | 8/10 | Manual smoke + release validation; post-ship UX review là follow-up |

DX mode: `POLISH`; persona chính là maintainer triển khai/review feature trong repo Windows/.NET hiện tại. Không thêm playground, SDK, telemetry hay community surface vì không phục vụ scope này.

## Implementation tasks

- [x] **T1 (P1) — State contract:** thêm v2/load provenance/canonical writer và raw migration fixtures; verify bằng Core + Infrastructure targeted tests.
- [x] **T2 (P1) — Fault isolation:** degrade/disable riêng corrupt Book và đưa queue state load vào structured failure boundary; verify snapshot, stale-request rejection và backend state guard.
- [x] **T3 (P1) — Wire/UI:** phát string mode ở final WebView JSON, bỏ ordinal/Auto/sentinel ngoài Interior; verify C# serialization + Node bridge tests.
- [x] **T4 (P1) — Processing/cache:** stage/hash frame, validate trước cache hit, bump stamp và giữ structured failure xuyên queue; verify missing/corrupt/wrong-size/same-metadata fixtures.
- [ ] **T5 (P2) — UX/docs/release:** UI two-state, migration warning, docs, downgrade note và full automated suite đã hoàn tất; manual artifact smoke giữ lại cho release gate.

## Acceptance criteria

- Không còn `Auto` trong normal Interior enum, snapshot, bridge payload, UI, filter, bulk control hoặc cache stamp mới.
- Book/page mới và missing override luôn resolve/display/process là No Frame.
- Legacy `auto`, numeric `0` và explicit disabled load được, canonicalize về No Frame; warning áp dụng cho Auto/missing legacy, writer không phát `auto`/`disabled`/numeric.
- State v1 không có override vẫn cảnh báo đúng các normal Interior page; state v2 canonical sparse Enabled-only và có policy downgrade rõ.
- Một Book có state corrupt bị block/actionable nhưng không làm hỏng library refresh; Book healthy vẫn process riêng được, stale request chứa corrupt Book bị reject rõ.
- `Frame` với frame hợp lệ luôn overlay; frame thiếu/stale luôn fail rõ và không sinh output thành công.
- `No Frame` giữ forced CropArt và không dùng detector để quyết định overlay.
- Intro/Production vẫn forced No Frame và không bị lộ thành state user-selectable.
- Cache Auto hoặc unframed Enabled cũ không được reuse sai; frame thay nội dung cùng length/timestamp vẫn invalidate; PDF đã publish không tự thay đổi.
- Wire JSON cuối dùng `"enabled"`/`"disabled"`, không có numeric mode/`auto`; asset không phải normal Interior không dùng mode sentinel.
- `INTERIOR_FRAME_*` giữ nguyên code + safe context từ pipeline qua queue/task tới final bridge JSON; outer exception không làm mất cause.
- Artwork mở mặc định All; UI chỉ còn Frame/No Frame và hỗ trợ Mixed aggregate.
- Bulk chỉ còn No change/Frame/No Frame; invalid Frame block Process với hướng xử lý cụ thể.
- Keyboard/screen reader nhận biết filter, mode/status và save/process errors; layout dùng được ở breakpoint hiện có.
- Tất cả test mới/regression qua; không làm thay đổi workflow ngoài phạm vi đã chốt.

## Decision Audit Trail

| # | Phase | Decision | Classification | Principle | Rationale | Rejected |
|---|---|---|---|---|---|---|
| 1 | CEO | Hard cutover legacy Auto/missing sang No Frame | User decision | Explicit contract | User ưu tiên đúng hai trạng thái và legacy phù hợp No Frame | Hidden Auto; detector-based freeze |
| 2 | CEO | `Frame` fail closed nếu frame thiếu | User decision | Correctness | Nhãn Frame phải bảo đảm output có frame | Silent No Frame fallback |
| 3 | CEO | Giữ detector/classifier cho preparation path | Auto-decided | Minimal diff | Loại Auto không đòi refactor thuật toán ảnh | Xóa detector trong MVP |
| 4 | Design | Giữ workspace/layout hiện có | Auto-decided | Scope discipline | UI hiện tại đã đủ interaction | Màn hình/wizard mới |
| 5 | Design | Filter mặc định explicit All | Auto-decided | Predictability | Default No Frame không nên che page Frame | Default filter No Frame |
| 6 | Design | Mixed chỉ là aggregate badge | Auto-decided | Honest status | Không gọi cả Book là Frame chỉ vì một page | Badge ưu tiên Frame |
| 7 | Design | Save Frame intent nhưng block Process nếu frame invalid | Auto-decided | Recoverability | Không mất lựa chọn khi Brand được sửa sau | Chặn Save; silent fallback |
| 8 | Persistence | Sparse state chỉ lưu Enabled override | Auto-decided | Simplicity | Missing key trở thành No Frame tự nhiên | Lưu Disabled cho mọi page |
| 9 | Migration | Compatibility tập trung tại read boundary | Auto-decided | Explicit boundaries | Domain/runtime không mang trạng thái thứ ba | Mapping rải rác |
| 10 | Design | Cảnh báo legacy theo từng Book, không modal | Auto-decided | User safety | Hard cutover có thể đổi output khi reprocess và user đã yêu cầu có cảnh báo | Chỉ release note; modal chặn |
| 11 | Design | Giữ status filter mặc định Active | Auto-decided | Scope discipline | Chỉ đổi frame mode; đổi Active filter sẽ thay workflow ngoài scope | Đặt cả hai filter về All |
| 12 | Cache | Giữ policy mismatch như một lớp invalidation | Auto-decided | Defense in depth | `detected` và `forced-no-frame` vẫn phân biệt Auto cũ với No Frame mới | Chỉ dựa schema version |
| 13 | Eng | Version state contract và mang migration provenance qua load-result | Auto-decided | Data correctness | Legacy missing key chính là Auto; không thể chỉ dò explicit `auto` | Cảnh báo chỉ explicit key; side-channel mutable |
| 14 | Eng | Wire mode là string DTO explicit | Auto-decided | Contract safety | WebView hiện serialize enum thành ordinal nên đổi ordinal có thể mis-map | Global enum converter; giữ numeric |
| 15 | Eng | Bump cache schema và dùng frame content digest | Auto-decided | Output correctness | Cache fast return và path/length/time không bảo đảm Frame thực sự hợp lệ | Chỉ dựa policy/path metadata |
| 16 | Eng | Corrupt state degrade theo Book | Auto-decided | Fault isolation | Một file lỗi không được làm mất toàn bộ library | Fail toàn snapshot; silently normalize |
| 17 | Eng | Giữ global Brand readiness trong MVP | Auto-decided | Scope discipline | Brand hiện luôn yêu cầu frame; tách readiness là thay đổi workflow riêng | Cho No Frame bypass Brand frame validation |
| 18 | Eng | Canonical v2 sparse Enabled-only; downgrade unsupported | Auto-decided | Minimal diff | State store không biết asset discovery và missing đã là No Frame theo contract mới | Materialize Disabled cho mọi page; cam kết downgrade an toàn |
| 19 | Outside voice | Enum/persistence/wire/client cutover là atomic compile unit | Auto-decided | Build safety | Xóa Auto trước khi cập nhật mọi consumer làm phase trung gian không compile hoặc mis-map | Release phase theo layer độc lập |
| 20 | Outside voice | Frame overlay chỉ đọc immutable staged file đã hash | Auto-decided | TOCTOU safety | Digest cache vô nghĩa nếu overlay quay lại đọc mutable Brand file | Hash Brand path rồi overlay trực tiếp |
| 21 | Outside voice | Corrupt Book bị disable; stale mixed request reject toàn session | Auto-decided | Scope discipline | Giữ preflight batch semantics hiện tại nhưng không làm mất library/healthy Book | Thêm partial-batch processing mới |

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---:|---:|---|---|
| CEO Review | `/plan-ceo-review` via `/autoplan` | Scope & strategy | 1 | CLEAR | Hard cutover, fail-closed và legacy policy đã chốt |
| Codex Review | outside voice via `/autoplan` | Independent second opinion | 2 | CLEAR AFTER UPDATES | Migration provenance, wire ordinal, cache fast-return, atomic cutover và TOCTOU đã được đưa vào plan |
| Eng Review | `/plan-eng-review` via `/autoplan` | Architecture & tests | 1 | CLEAR AFTER UPDATES | 5 high + 3 medium gaps đã xử lý trong contract/phases/tests |
| Design Review | `/plan-design-review` via `/autoplan` | UI/UX gaps | 1 | CLEAR AFTER UPDATES | Bulk interaction, warning, filter, accessibility và responsive contract đã rõ |
| DX Review | `/plan-devex-review` via `/autoplan` | Implementation experience | 1 | CLEAR | 7/10 → 9/10; targeted commands, fixtures, errors và phase gates đã bổ sung |

**CROSS-MODEL:** Engineering và outside voice cùng xác nhận cần versioned provenance, explicit string wire contract, cache validation trước fast return và end-to-end structured failures.

**VERDICT:** CEO + DESIGN + ENG + DX CLEARED — kế hoạch sẵn sàng triển khai; chưa có code sản phẩm trong phase này.

NO UNRESOLVED DECISIONS
