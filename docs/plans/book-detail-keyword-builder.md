<!-- /autoplan restore point: C:/Users/admin/.gstack/projects/unknown/feat-cover-panel-previews-autoplan-restore-20260928-162105.md -->
# Book Detail Keyword Builder — Kế hoạch MVP

## Trạng thái

- Chỉ lập kế hoạch và review; phase này không triển khai code.
- Các quyết định `1A–5A` đã được duyệt và là contract cố định của MVP.
- Tính năng phải additive, dùng state/snapshot/bridge hiện có và không thay đổi workflow Book Information, Brand, Cover, Interior hay Production.

## Mục tiêu và user job

Thêm một card **Keyword Builder** trong Book Detail để biến danh sách keyword nghiên cứu thành dữ liệu sẵn dùng cho production:

1. User paste danh sách phrase, mỗi dòng một phrase.
2. User có thể nhập thêm `adsAsin` dạng text nhiều dòng.
3. User bấm **Build & Save**.
4. App tạo và lưu `keyword_1…keyword_7` cùng `adsKeyword`.
5. User kiểm tra kết quả và copy dữ liệu theo đúng thứ tự field mà không phải tự format lại.

Outcome cần đạt:

> Turn pasted research into seven destination-ready keyword values and an Ads phrase list that can be transferred without reformatting.

## Quyết định sản phẩm đã khóa

| # | Quyết định | Contract MVP |
|---|---|---|
| 1A | Thời điểm shuffle | Pack unique words vào slot trước; chỉ sau khi toàn bộ packing hợp lệ mới shuffle words trong từng slot đã có dữ liệu, đúng một lần cho mỗi lần build thành công. |
| 2A | Unique word | So sánh case-insensitively. `Book` và `book` trùng nhau; `book` và `books` khác nhau. Giữ spelling/casing của lần xuất hiện đầu tiên. |
| 3A | Capacity failure | Chặn build nếu một word dài hơn 50 ký tự hoặc ordered unique-word stream cần slot thứ 8 theo sequential next-fit. Không truncate, split hoặc drop word. |
| 4A | Field có thể edit | User chỉ edit source phrases và `adsAsin`. `keyword_1…keyword_7` và `adsKeyword` là read-only. |
| 5A | Duplicate trong Ads Keyword | `adsKeyword` giữ toàn bộ phrase đã normalize theo đúng thứ tự input, kể cả phrase trùng nhau. Unique word chỉ áp dụng cho `keyword_1…keyword_7`. |

Contract bổ sung:

- Giới hạn `keyword_*` là **tối đa 50 Unicode grapheme**: 50 hợp lệ, 51 không hợp lệ.
- “Word” là token không rỗng khi split phrase bằng Unicode whitespace. Punctuation thuộc về token; không stem, bỏ punctuation hoặc gộp singular/plural.
- Bỏ blank line; trim mỗi phrase; collapse whitespace bên trong thành một ASCII space.
- `adsKeyword` là các phrase đã normalize nối bằng `", "`.
- `adsAsin` là text dành cho **advertising product targets**, không phải ASIN riêng của Book Information. MVP chỉ trim hai đầu toàn bộ text, giữ nội dung/multiline bên trong, không dedupe và không giới hạn ký tự.
- Source phrases và output đều có thể empty. Build nguồn empty sẽ lưu source empty, xóa bảy keyword và `adsKeyword`, nhưng vẫn lưu `adsAsin` đã submit.
- Bấm **Build & Save** lần nữa là một build mới và có thể cho thứ tự shuffle mới. Load, refresh, redraw, resize hoặc reopen tuyệt đối không được shuffle.

## Ví dụ chuẩn

Input:

```text
coloring books for adults
adult coloring book
coloring book
```

`adsKeyword`:

```text
coloring books for adults, adult coloring book, coloring book
```

Ordered unique-word stream trước khi pack:

```text
coloring books for adults book
```

Stream trên nằm trong `keyword_1`; năm word được shuffle một lần rồi persist. `keyword_2…keyword_7` là `null`.

### Làm rõ capacity semantics

MVP dùng **sequential next-fit theo thứ tự unique-word stream**, không giải bài toán bin-packing toàn cục:

- nếu word kế tiếp không vừa slot hiện tại thì mở slot kế tiếp;
- không quay lại nhét word vào slot cũ và không reorder trước khi pack;
- vì vậy một tập word có thể fit nếu sắp xếp thủ công theo cách khác nhưng vẫn fail theo ordered next-fit;
- error phải nói rõ: “ordered keyword stream cần slot thứ 8 theo quy tắc packing hiện tại”, không khẳng định rằng mọi cách sắp xếp đều bất khả thi.

Điều này giữ đúng flow mà user đã duyệt: cộng dồn theo input order, chuyển slot khi `current + space + nextWord` vượt 50, rồi mới shuffle trong từng slot.

## Những gì codebase đã có và sẽ reuse

| Thành phần | Hiện trạng | Cách reuse |
|---|---|---|
| `BookProcessingState` | Lưu Book metadata và workflow trong `state/book-state.json`. | Thêm một nested state optional ở cuối model. |
| `JsonBookWorkspaceStateStore` | Load/normalize/atomic file replace. | Persist builder state; không tạo database/migration riêng. |
| `BookCatalogMetadataService` | Sở hữu mutation của Book Detail. | Thêm build-and-save operation; load latest state sau khi vào mutation gate. |
| `ApplicationSnapshotService` / `BookDesktopSummary` | Project state sang frontend. | Append optional builder summary ở cuối contract. |
| `WebViewBridgeRouter` | Parse request, mutation gate và response. | Thêm `book.keywords.save` và success acknowledgement trực tiếp. |
| `BookProductionMetadata` | Có normalize Unicode whitespace và grapheme-count policy. | Extract/reuse helper chung thay vì viết một định nghĩa “character” thứ hai. |
| `app.js` | Draft state, Book drawer, targeted card refresh. | Thêm draft/feedback/updater riêng; sửa routing command theo allow-list. |

## Information architecture và layout

Không thêm tab thứ sáu. Thứ tự trong Book Detail Overview:

```text
Book summary
└── Book Information
└── Brand Assignment
└── Keyword Builder
    ├── Input column
    │   ├── Source Keywords
    │   ├── Ads ASIN (product targets)
    │   └── Build & Save / Clear & Save
    └── Saved output column
        ├── Saved-status badge + build time
        ├── keyword_1 … keyword_7 + counters
        ├── Ads Keyword
        └── Copy all in field order
└── Brand PSD templates
```

Đặt Keyword Builder **sau Brand Assignment** để không phá dependency/hierarchy đang quen dùng trong Book Detail. Đây vẫn là một card độc lập; Save Book Information và Brand Assignment không submit hoặc clear draft của builder.

### Responsive contract

- Ở drawer rộng: card hai cột, input bên trái và saved output bên phải.
- Dưới khoảng `1100px` usable width hoặc khi Windows scaling làm thiếu không gian: stack thành một cột, action full-width.
- Source Keywords ưu tiên 8–10 dòng; Ads ASIN 3–4 dòng; Ads Keyword có textarea read-only riêng.
- Bảy keyword dùng compact single-line read-only controls, có `min-width: 0`, ellipsis/scroll nội bộ phù hợp và counter không đẩy vỡ layout.
- Drawer là scroll owner chính; textarea được phép scroll bên trong. Không tạo nested page scroll hoặc horizontal overflow.
- Preserve drawer scroll khi targeted refresh, passive snapshot và resize.
- Manual check ở kích thước window tối thiểu/maximized và Windows scaling 125%, 150%, 200%.

## Data contract

Thêm optional property ở cuối `BookProcessingState`:

```text
KeywordBuilder: BookKeywordBuilderState? = null
```

Logical JSON:

```json
{
  "keywordBuilder": {
    "sourceKeywords": [
      "coloring books for adults",
      "adult coloring book",
      "coloring book"
    ],
    "keyword_1": "...",
    "keyword_2": null,
    "keyword_3": null,
    "keyword_4": null,
    "keyword_5": null,
    "keyword_6": null,
    "keyword_7": null,
    "adsKeyword": "coloring books for adults, adult coloring book, coloring book",
    "adsAsin": "...",
    "buildId": "opaque-unique-id",
    "builtAtUtc": "2026-09-28T10:00:00Z",
    "algorithmVersion": 1
  }
}
```

Rules:

- Dùng explicit JSON names cho `keyword_1…keyword_7` (ví dụ `JsonPropertyName`) để camel-case serializer không biến thành `keyword1`.
- Empty generated slot normalize thành `null`; `sourceKeywords` giữ ordered normalized phrases của build đã lưu.
- `buildId` là revision opaque duy nhất cho một build thành công; dùng để chống stale snapshot ghi đè UI result mới.
- `builtAtUtc` phục vụ nhãn “Last saved”; `algorithmVersion` là policy marker, không phải history/versioning workflow.
- Generated output được persist verbatim và không regenerate/shuffle khi deserialize, normalize, snapshot hoặc unrelated save.
- Passive load chỉ được trim/null hóa giá trị empty theo contract tương thích; không áp current build limits lên dữ liệu cũ.
- Append property mới ở cuối positional constructor của `BookProcessingState` và `BookDesktopSummary`, có default `null`, để giảm breakage cho call site/test cũ.
- Không cần schema migration/database. Missing `keywordBuilder` nghĩa là “Not built”.
- Rollback binary cũ đọc được file vì ignore unknown property, nhưng lần whole-state save tiếp theo bằng binary cũ có thể làm mất `keywordBuilder`; đây là rollback limitation phải ghi trong release note.

## Thuật toán authoritative

Frontend chỉ gửi editable inputs. Backend chịu trách nhiệm normalize, validate, pack, shuffle và persist.

Request:

```json
{
  "bookId": "Book One",
  "keywords": [
    "coloring books for adults",
    "adult coloring book"
  ],
  "adsAsin": "B0...\nB0..."
}
```

Pipeline:

1. Parse shape nghiêm ngặt: `bookId` non-empty, `keywords` bắt buộc là array string; array empty hợp lệ; `adsAsin` chỉ nhận string hoặc `null`.
2. Vào process-local catalog mutation gate; kiểm tra Book tồn tại và chặn khi Processing Session, Production Action hoặc Cache Cleanup đang active.
3. Normalize phrase: trim, collapse Unicode whitespace thành một ASCII space, bỏ empty.
4. Tạo `adsKeyword` từ toàn bộ normalized phrase theo input order, giữ duplicate phrase.
5. Split normalized phrase thành ordered word stream.
6. Dedupe bằng `StringComparer.OrdinalIgnoreCase`, giữ spelling/casing của lần xuất hiện đầu tiên.
7. Dry-run sequential next-fit tối đa bảy slot:
   - word vào empty slot nếu grapheme count `<= 50`;
   - slot có dữ liệu nhận word khi `count(current + " " + word) <= 50`;
   - nếu không vừa thì mở slot kế tiếp;
   - không split/truncate/drop/reorder word.
8. Nếu một word `> 50`, fail `keyword_word_too_long` trước mọi random call và state write.
9. Nếu phải mở slot thứ 8, fail `keyword_capacity_exceeded` trước mọi random call và state write.
10. Chỉ sau khi toàn bộ dry-run pass, gọi Fisher–Yates một lần trên từng slot có từ hai word trở lên; slot 0/1 word không cần random call.
11. Join mỗi slot bằng một ASCII space; tạo đủ bảy named properties; trim outer edge của `adsAsin`; tạo `buildId`, `builtAtUtc`, `algorithmVersion`.
12. Load latest Book state **sau khi đã vào gate**, replace riêng `KeywordBuilder`, và thực hiện đúng một atomic save.
13. Sau khi save thành công, không còn cancellable work trong transaction; trả exact persisted state ngay. Refresh library là best-effort riêng và không được đổi save success thành failure.

### Randomness test seam

- Tách `normalize/dedupe/pack` khỏi `shuffle`.
- Inject một abstraction rất nhỏ như `IKeywordWordShuffler` hoặc random-index source.
- Production dùng unbiased index generation; test dùng fake deterministic.
- Test số lần/call invariant, không assert output bắt buộc phải khác input vì identity permutation là hợp lệ.

## Concurrency và transaction boundary

Atomic file replace chỉ chống torn write; nó không tự chống lost update từ hai read-modify-write song song. MVP contract:

- `book.keywords.save` dùng cùng process-local mutation gate với catalog mutations.
- Sau khi giữ gate mới load Book state mới nhất, sau đó replace riêng `KeywordBuilder` và save một lần.
- Chặn save khi Processing, Production Action hoặc Cache Cleanup active vì các worker này có thể ghi cùng workspace state.
- Metadata save và Keyword Builder save đi qua cùng gate; test chứng minh hai property không ghi đè nhau.
- Cross-process concurrent editing cùng workspace không được hỗ trợ trong MVP và phải được ghi rõ.
- Không tạo `UpdateAsync` transaction framework trong phase này; đó là refactor lớn hơn và chỉ cân nhắc khi toàn bộ state writer cùng chuyển sang contract mới.

## Bridge contract

Command:

```text
book.keywords.save
```

Request fields:

| Field | Type | Required | Contract |
|---|---|---:|---|
| `bookId` | string | yes | Exact current Book id. |
| `keywords` | string[] | yes | Empty hợp lệ; member không phải string làm request invalid. |
| `adsAsin` | string/null | no | Freeform multiline product-target text; outer-trim only; unlimited. |

### Success acknowledgement

Không trả `background.task` làm bằng chứng duy nhất cho save. Trả trực tiếp:

```text
book.keywords.saved
{
  bookId,
  keywordBuilder: <exact persisted state>
}
```

Frontend phải:

1. patch ngay builder state của đúng Book bằng payload đã persist;
2. giữ một confirmed overlay theo `bookId + buildId` cho đến khi snapshot chứa cùng build;
3. chỉ clear draft nếu current normalized inputs vẫn giống submitted inputs của request đó;
4. chạy refresh library best-effort sau acknowledgement;
5. nếu refresh cũ đã chạy trước save trả snapshot stale, không được overwrite confirmed builder overlay;
6. nếu refresh fail, hiển thị **Saved · Refresh needed** và action **Retry refresh**; retry chỉ refresh, tuyệt đối không build/shuffle/save lần nữa.

Save đã commit nhưng refresh lỗi phải được báo là “Saved; library refresh failed”, không bao giờ là “Build failed”.

### Structured validation error

Top-level bridge error giữ code ổn định; payload dùng schema versioned:

```json
{
  "policyVersion": 1,
  "field": "keywords",
  "code": "keyword_word_too_long",
  "message": "The word ... contains 51 characters; maximum is 50.",
  "details": {
    "offendingWord": "...",
    "graphemeCount": 51,
    "maximumCharacters": 50
  }
}
```

Capacity details:

```json
{
  "policyVersion": 1,
  "field": "keywords",
  "code": "keyword_capacity_exceeded",
  "message": "The ordered keyword stream needs an eighth slot.",
  "details": {
    "firstUnplacedWord": "...",
    "uniqueWordCount": 123,
    "requiredSlotCount": 8,
    "maximumSlotCount": 7,
    "packingRule": "sequential_next_fit"
  }
}
```

Error registry:

| Code/state | Problem + cause + fix | Data behavior |
|---|---|---|
| `invalid_keyword_builder` | Request sai shape; yêu cầu phrase text, mỗi dòng một phrase. | Không save; giữ draft/output cũ. |
| `keyword_word_too_long` | Nêu exact word, grapheme count, max 50; yêu cầu shorten/split source token. | Không random/save. |
| `keyword_capacity_exceeded` | Nêu first unplaced word và sequential next-fit cần slot 8; yêu cầu remove/shorten/reorder source. | Không random/save. |
| `processing_active` | Processing đang ghi state; thử lại sau. | Không save. |
| `production_action_active` | Production action đang chạy; thử lại sau. | Không save. |
| `cache_cleanup_active` | Cache cleanup đang chạy; thử lại sau. | Không save. |
| `book_not_found` | Book không còn trong snapshot/discovery. | Giữ draft; yêu cầu refresh/reselect. |
| Store I/O fail | Disk full/read-only/path failure. | Không clear draft; output cũ giữ nguyên. |
| Save success, refresh fail | Save đã hoàn thành, snapshot chưa đồng bộ. | Hiển thị committed output + refresh-only retry. |
| Malformed error payload | Backend trả payload không đúng schema. | Fallback safe generic message; không render raw exception. |

Tạo validation result/exception riêng cho Keyword Builder; không ép details này vào `BookMetadataValidationError`.

## UI contract chi tiết

### Wireframe

```text
┌ Keyword Builder ──────────────────────────────────────────────────────┐
│ Turn phrase research into saved backend and Ads keyword fields.      │
│                                                                      │
│ Source Keywords                     Last saved generated keywords     │
│ [ phrase per line              ]     Keyword 1 [read-only]   42 / 50  │
│ [                              ]     ...                              │
│ [                              ]     Keyword 7 [read-only]    0 / 50  │
│                                                                      │
│ Ads ASIN (product targets)           Ads Keyword                      │
│ [ freeform multiline           ]     [read-only multiline          ]  │
│                                                                        │
│ [ Build & Save ]                      [ Copy all in field order ]      │
│ status / error / refresh-only retry                                  │
└──────────────────────────────────────────────────────────────────────┘
```

### Labels và helper text

- **Source Keywords** — “One phrase per line. Duplicate phrases remain in Ads Keyword; duplicate words are removed from keyword_1–keyword_7.”
- **Ads ASIN (product targets)** — “Optional advertising target text. This is separate from the Book Information ASIN.”
- Output group luôn có heading **Last saved generated keywords** để user không nhầm saved output với unsaved draft.
- Read-only controls dùng `readonly`, không `disabled`, để keyboard focus/select/copy hoạt động.

### State matrix

| State | Badge/action | Output/message |
|---|---|---|
| Chưa từng build | `Not built` | “No saved generated keywords.” |
| Saved và draft match | `Saved` | Hiện saved output + last saved time. |
| Draft khác saved source/adsAsin | `Unsaved inputs` | “Saved output does not include these edits.” |
| Request pending | `Building & saving…` | Giữ output cũ; lock editable controls/action, nhưng read-only output vẫn navigable. |
| Validation fail | `Needs attention` | Inline error ở Source Keywords + summary; output cũ giữ nguyên. |
| Save acknowledged, refresh pending | `Saved · Refreshing…` | Hiện exact committed output từ acknowledgement. |
| Refresh fail sau save | `Saved · Refresh needed` | Hiện committed output; có **Retry refresh**, không rebuild. |
| Saved empty source | `Saved` | “Saved output is empty”; không quay về `Not built`. |

### Action rules

- **Build & Save** vẫn enabled khi draft clean để user chủ động reshuffle, trừ lúc mutation/activity lock.
- Nếu normalized source empty và đã có saved generated output, đổi label thành **Clear & Save** và hiện inline warning “This will clear all saved generated keyword fields.” Không dùng modal.
- Nếu source chỉ chứa whitespace, xử lý giống empty source.
- Thêm một action MVP **Copy all in field order**. Clipboard text phải có labels ổn định theo thứ tự:

```text
keyword_1: ...
keyword_2: ...
...
keyword_7: ...
adsKeyword: ...
adsAsin: ...
```

- Copy dùng saved output đang hiển thị, không dùng unsaved draft và không mutate state.
- Clipboard failure phải hiện feedback inline và vẫn cho manual selection.
- Không thêm per-field copy buttons, randomize-only, auto-build, auto-save, chip editor hoặc confirmation modal.

### Draft lifecycle

Draft keyed theo `bookId` và gồm source textarea + `adsAsin`:

- survive tab switch, targeted redraw, passive snapshot, window resize và unrelated catalog mutation;
- chuyển sang Book khác không làm mất draft của Book trước trong cùng session;
- drawer close có shared unsaved-change guard gồm Interior, Book Information và Keyword Builder;
- chỉ discard draft của selected Book khi user xác nhận discard;
- Save Book Information/Brand Assignment không clear builder draft;
- request success chỉ clear draft nếu draft hiện tại vẫn match request đã submit; edit mới hơn không được mất.

### Command/feedback routing

Thay negative check kiểu “mọi command không phải metadata đều là assignment” bằng allow-list rõ ràng:

```text
Book Information : book.metadata.save
Keyword Builder  : book.keywords.save
Brand Assignment : book.brand.assign, book.brand.unassign
Brand page       : brand.author.save
```

Tạo updater riêng `refreshBookKeywordBuilderCard`. Pending/success/error của Keyword Builder không được xuất hiện trong Brand Assignment hoặc làm redraw toàn Book panel.

### Accessibility và focus

- Mỗi control có explicit `<label>`; counters nối bằng `aria-describedby`.
- Card có một live-region owner duy nhất để tránh screen reader đọc lặp.
- Validation fail focus Source Keywords; giữ caret/selection khi update không do submit.
- Success chỉ restore focus về Build/Clear button nếu focus trước đó nằm trong card bị replace; không steal focus từ nơi khác.
- Lock state có visible reason, không chỉ dựa vào disabled styling.
- Read-only output vẫn keyboard-navigable trong pending state.
- Book drawer hiện chưa có full modal semantics/focus trap/inert background; không mở rộng thành refactor trong feature này. Đây là follow-up accessibility debt, và plan không claim đã giải quyết toàn bộ dialog accessibility.

## Architecture/data flow

```text
Keyword Builder draft
        ↓ strict bridge-shape validation
process-local gate + active-writer checks
        ↓
normalize → dedupe → sequential next-fit dry run
        ↓ all validation passed
shuffle through injected source
        ↓
load latest Book state → replace KeywordBuilder → one atomic save
        ↓
book.keywords.saved { exact persisted state }
        ↓
patch only Keyword Builder + confirmed build overlay
        ↓
best-effort library refresh (never changes save outcome)
```

## Implementation phases

### Phase 1 — Shared text policy, domain algorithm và state contract

Work:

- Extract/reuse grapheme-count và Unicode whitespace normalization từ metadata policy.
- Add `BookKeywordBuilderState`, seven explicit named output properties và audit fields.
- Implement pure normalize/dedupe/sequential-next-fit dry run.
- Add injected shuffler; đảm bảo không random trước khi dry-run pass.
- Append optional property vào `BookProcessingState` và normalization tương thích dữ liệu cũ.
- Pin exact JSON names `keyword_1…keyword_7`.

Exit criteria:

- Old state không có builder load bình thường.
- Mỗi successful result có 0–7 slot, mỗi slot `<= 50` graphemes.
- Mọi unique word xuất hiện đúng một lần; duplicate phrase vẫn còn trong `adsKeyword`.
- Load/save lặp lại không rebuild/reshuffle.
- Fake shuffler chứng minh mỗi populated multi-word slot được shuffle đúng một lần, và validation fail có zero shuffle call.

Suggested commit:

```text
feat(keyword-builder): add domain and persistence contract
```

### Phase 2 — Save service, concurrency boundary và direct acknowledgement

Work:

- Extend catalog service với build-and-save operation trả exact persisted state.
- Load latest workspace state sau khi giữ gate; replace builder và atomic save một lần.
- Add active Processing/Production/Cache Cleanup rejection.
- Project builder vào `BookDesktopSummary`.
- Add `book.keywords.save` + `book.keywords.saved` và structured validation payload.
- Tách persistence success khỏi best-effort library refresh.

Exit criteria:

- Valid request trả exact committed result mà không phụ thuộc refresh.
- Active refresh bắt đầu trước save không thể clear/overwrite build mới.
- Refresh conflict/failure sau save chỉ tạo refresh warning.
- Metadata và keyword saves tuần tự giữ cả hai property.
- Cancellation sau persistence không biến thành response “build failed” và không chạy shuffle/save lần hai.

Suggested commit:

```text
feat(keyword-builder): add guarded save and bridge contract
```

### Phase 3 — Book Detail UI, isolated state và transfer action

Work:

- Add card sau Brand Assignment với wide/stacked layout.
- Add per-book draft, normalized dirty comparison và close guard integration.
- Render state matrix, counters, inline errors, pending/refresh-needed paths.
- Add exact success patch + confirmed `buildId` overlay and refresh-only retry.
- Replace negative feedback routing with explicit allow-lists.
- Add copy-all action and clipboard fallback feedback.
- Preserve focus, selection, drawer scroll và unrelated drafts.

Exit criteria:

- Keyword feedback không bao giờ leak sang Brand Assignment.
- Book Information Save không đổi behavior.
- Failed build giữ draft và last saved output.
- Clean Build & Save vẫn chạy một reshuffle mới.
- Empty source có clear warning và lưu thành Saved-empty state.
- Layout không overflow ở wide/stacked/scaled viewport.

Suggested commit:

```text
feat(keyword-builder): add book detail builder workflow
```

### Phase 4 — Regression, documentation và compatibility proof

Work:

- Add Core, persistence, service, bridge và frontend tests theo matrix dưới.
- Update user guide: input semantics, packing rule, product-target ASIN, Copy all, refresh warning và rollback caveat.
- Run toàn bộ Core/Infrastructure/Desktop/Node frontend suites.
- Manual QA trên legacy Book, multiple Books, Windows scaling và failure recovery.

Exit criteria:

- Automated suites pass.
- Existing Book Information, Brand, Cover, Interior, Process và Production actions giữ behavior.
- User guide phân biệt rõ draft input, last saved output và Book ASIN so với Ads product targets.

Suggested commit:

```text
test(keyword-builder): cover workflow and compatibility
```

## Test matrix

### Core algorithm

- Blank removal; Unicode whitespace collapse; phrase order.
- Duplicate phrase retained in `adsKeyword`.
- Case-insensitive unique word, first-spelling preservation.
- Singular/plural and punctuation remain distinct.
- 50 graphemes valid; 51 invalid with exact typed details.
- Seven slots exactly full; next word requires slot 8.
- Adversarial ordered sequence that could fit after global reorder still follows sequential next-fit and reports the defined capacity error.
- Empty source and whitespace-only source.
- No random call before complete validation; fake shuffler called once per multi-word slot.
- Output word multiset invariant; do not assert shuffled order differs from input.

### Persistence/model

- Legacy JSON missing `keywordBuilder`.
- Exact JSON names `keyword_1…keyword_7`.
- Null empty slots; multiline/unlimited `adsAsin`.
- Append-only positional constructor compatibility.
- Passive normalization does not enforce latest build rules or reshuffle.
- Full round trip retains `buildId`, `builtAtUtc`, `algorithmVersion` and output verbatim.
- Rolled-back reader ignores unknown property; documented later legacy save may erase it.

### Service/concurrency

- Valid save replaces only builder property.
- Validation error preserves previous state byte-for-byte/logically.
- Simultaneous metadata/keyword requests serialize and preserve both.
- Keyword save blocked during Processing, Production Action và Cache Cleanup.
- Cross-process concurrency explicitly unsupported.
- Disk-full/read-only/atomic-save exception keeps draft/output behavior safe.

### Bridge

- Valid array, empty array, missing array, wrong type, non-string member.
- Word-too-long and capacity payload schema exact, including `policyVersion` and typed details.
- `book.keywords.saved` returns exact persisted state.
- Refresh already active before keyword save.
- Refresh start/terminal failure after successful save.
- Cancellation boundary after persistence.
- Malformed validation payload frontend fallback.

### Frontend

- Independent per-book draft and dirty state.
- Draft survives tab switch/redraw/passive snapshot/resize/unrelated mutation.
- Close guard includes builder and discards only selected Book draft.
- Feedback routing never leaks into Brand Assignment.
- Pending retains prior output; success patches only builder card.
- Confirmed build overlay rejects stale snapshot and clears on matching `buildId`.
- Refresh-only retry never posts `book.keywords.save`.
- Build & Save enabled while clean; one explicit click creates one request.
- Empty-source Clear & Save warning, saved-empty state, `adsAsin` retention.
- Copy all ordering, clipboard success/failure, manual selection fallback.
- Focus/caret/live-region/counter associations.
- Wide and stacked layout, no horizontal overflow; 125/150/200% scaling.

### Regression

- Save Book Information validation và button behavior.
- Brand assign/unassign feedback and filtering.
- Existing mutation/process locks.
- Book drawer navigation, selected tab, scroll restoration.
- Cover/Interior/Production workflows and legacy Book load.

## Acceptance criteria

1. Book Detail có card Keyword Builder riêng sau Brand Assignment với source phrases, Ads product-target text, bảy read-only keywords, read-only Ads Keyword và explicit save action.
2. Backend persist normalized source, `keyword_1…keyword_7`, `adsKeyword`, `adsAsin` và build audit fields trong Book workspace state hiện có.
3. Mỗi keyword slot không vượt 50 Unicode graphemes, chỉ chứa whole input words và một ASCII space giữa words.
4. Duplicate word case-only xuất hiện một lần trên toàn bộ bảy slots; singular/plural và punctuation token vẫn khác nhau.
5. Duplicate source phrase vẫn lặp trong `adsKeyword`, đúng normalized input order.
6. Packing là ordered sequential next-fit; word >50 hoặc slot thứ 8 chặn save bằng lỗi rõ ràng và giữ saved result trước đó.
7. Shuffle chỉ xảy ra sau dry-run pass và đúng một lần cho mỗi multi-word slot của explicit successful build.
8. Saved output không đổi qua redraw, refresh, reopen, restart hoặc unrelated save.
9. Bấm Build & Save khi clean vẫn tạo build mới; empty source có warning và có thể lưu Saved-empty.
10. Save acknowledgement độc lập refresh; refresh fail không báo save fail và không rebuild khi retry.
11. Builder feedback/draft không tác động Book Information hoặc Brand Assignment.
12. User có thể copy saved values theo field order; clipboard fail vẫn cho manual selection.
13. Books không có builder data load bình thường; tất cả workflow cũ giữ behavior.

## Không nằm trong MVP

- SEO scoring, suggestions, AI/ChatGPT integration, stop-word removal, stemming/lemmatization.
- Global bin-packing optimization hoặc tự reorder trước khi pack.
- Direct edit generated output.
- Per-field copy buttons, CSV export, KDP/Ads API integration hoặc direct publishing.
- Auto-build, auto-save, randomize-only, history/undo/restore of old builds.
- Bulk Keyword Builder cho nhiều Books.
- Author/keyword database, shared keyword entity hoặc schema migration lớn.
- General workspace transaction/`UpdateAsync` refactor cho mọi state writer.
- Cross-process concurrent editing.
- Refactor toàn bộ Book drawer modal accessibility; ghi nhận là follow-up debt riêng.

## Expected file touchpoints

| Area | Expected files |
|---|---|
| Shared text/domain | `src/PrintableBook.Core/Domain/Books/BookProductionMetadata.cs` hoặc helper chung mới; builder model/algorithm mới |
| State | `src/PrintableBook.Core/Domain/Processing/BookProcessingState.cs` (đường dẫn thực tế xác nhận khi implement) |
| Application | `IBookCatalogMetadataService.cs`; snapshot/summary contract |
| Persistence | `JsonBookWorkspaceStateStore.cs` |
| Bridge | `WebViewBridgeRouter.cs`; bridge tests |
| Frontend | `Frontend/js/app.js`; `Frontend/css/book-workspace.css` |
| Tests | Core builder, workspace state store, bridge/router, frontend DOM/bridge tests |
| Docs | `docs/user-guide.md`; plan này |

Exact file creation/extension sẽ ưu tiên implementation nhỏ nhất nhưng thuật toán pure phải test độc lập.

## Implementation tasks

- [ ] **T1 (P1) — Domain:** shared grapheme/whitespace policy; pure sequential-next-fit builder; injected shuffle seam.
- [ ] **T2 (P1) — Persistence:** optional append-only builder state, exact underscore JSON names, passive non-regenerating normalization.
- [ ] **T3 (P1) — Transaction:** gated latest-state read + one save; reject competing state-writing activities.
- [ ] **T4 (P1) — Bridge:** direct `book.keywords.saved` acknowledgement, versioned typed errors, refresh separated from commit.
- [ ] **T5 (P1) — Frontend state:** isolated per-book drafts, explicit command routing, confirmed-build overlay, refresh-only retry.
- [ ] **T6 (P2) — UI:** responsive card, full state matrix, clear warning, accessibility/focus and copy-all transfer.
- [ ] **T7 (P1) — Verification:** algorithm/persistence/concurrency/bridge/frontend/regression matrix.
- [ ] **T8 (P2) — Docs:** user semantics, ordered packing caveat, Ads ASIN distinction, rollback limitation.

## Deferred follow-up

- Full modal semantics/focus trap/inert background for the shared Book Detail drawer is valid accessibility debt, but changing it here would broaden the feature beyond Keyword Builder. Không sửa `TODOS.md` trong phase plan-only này.
- A workspace-scoped atomic `UpdateAsync` could replace activity blocking in a later state-management refactor covering every writer.

## Decision audit trail

| Decision | Resolution | Reason |
|---|---|---|
| Pack optimization vs ordered next-fit | Keep ordered sequential next-fit. | Matches approved input-order rule; error wording now explicitly describes algorithmic capacity. |
| Save coupled to snapshot refresh | Separate completely. | Prevent false failure, stale draft clearing and accidental reshuffle retry. |
| Ads ASIN meaning | Product-target freeform text, separate from Book ASIN. | Removes ambiguous duplicate label without adding validation. |
| Saved output staleness | Explicit status matrix + last-saved label. | User can distinguish unsaved source from persisted generated values. |
| Empty-source clear | Inline warning + `Clear & Save`. | Makes destructive clear visible without modal overhead. |
| Transfer step | One Copy-all action. | Completes production job with minimal UI surface. |
| Concurrency | Existing gate + active-writer blocking. | MVP-sized protection; general transaction refactor deferred. |
| Random testing | Inject minimal shuffle seam. | Verifies one-time shuffle without brittle exact-order tests. |
| Legacy rollback | No migration, but document possible data loss on later legacy save. | Avoids overpromising compatibility. |

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---:|---|---|
| CEO review | `/autoplan` | Product completeness and failure semantics | 2 voices | PASS after revision | Save/refresh separation, stale-output clarity, empty clear warning, Ads ASIN semantics, transfer action, shared text policy |
| Design review | `/autoplan` | New Book Detail UI | 2 voices | PASS after revision | Card hierarchy, responsive layout, state matrix, draft lifecycle, feedback routing, focus/accessibility |
| Engineering review | `/autoplan` | Persistence, bridge and concurrency risk | 2 voices | PASS after revision | Direct save acknowledgement, writer boundary, typed errors, shuffle seam, JSON naming, rollback caveat |
| DX review | `/autoplan` | Developer-facing product surface | 0 | SKIPPED | No public API/CLI/onboarding scope |

VERDICT: READY FOR IMPLEMENTATION REVIEW — plan-only scope complete; no code has been changed.

Unresolved decisions: 0
