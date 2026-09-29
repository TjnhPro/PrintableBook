<!-- /autoplan restore point: C:/Users/admin/.gstack/projects/coloringbook/feat-keyword-builder-in-asin-research-autoplan-restore-20260929-165651.md -->
# Keyword Builder + ASIN Research workflow consolidation

## Trạng thái

- Loại thay đổi: UI workflow + bridge/application contract refactor.
- Branch mục tiêu: `feat/keyword-builder-in-asin-research`.
- Base: `main`.
- Trạng thái hiện tại: `/autoplan` approved; implementation đang thực hiện trên branch hiện tại.

## 1. Mục tiêu

Gộp ASIN Research vào Keyword Builder để user hoàn tất một workflow liên tục thay vì nhập lại dữ liệu và chuyển qua card riêng:

1. Nhập tối đa 5 hàng hiển thị của **Book Keywords**.
2. Nhấn **Shuffle** để tạo preview `Keyword 1`–`Keyword 7`, `Ads Keyword` và `Ads ASIN` mà chưa ghi xuống Book.
3. Dùng `Ads Keyword` đang có làm nguồn cho Amazon crawl; bỏ textarea **Search Keywords** trùng lặp.
4. Hiển thị **Crawl Results** ngay trong panel trái **Build inputs & ASIN research**, cao 5 hàng ở desktop.
5. ASIN crawl thành công tiếp tục tự draft vào `Ads ASIN`.
6. Nhấn **Save** để lưu đúng preview hiện tại; không random lại trong lúc save.

Outcome thành công: chỉ còn một source of truth cho từ khóa tìm kiếm, một preview rõ ràng trước khi persist, và hai panel Inputs/Research và Generated output cân đối.

## 2. Quyết định sản phẩm đã xác nhận

- Giữ hai panel ngang: **Build inputs & ASIN research** bên trái, **Generated output** bên phải.
- **Book Keywords** hiển thị cố định 5 rows.
- **Crawl Results** nằm trong panel trái và có viewport 5 compact rows ở desktop, có scroll.
- Bỏ input **Search Keywords**; crawler lấy phrases từ `Ads Keyword` hiện hành.
- Tách `Build & Save` thành hai thao tác độc lập: **Shuffle** và **Save**.
- ASIN hợp lệ sau crawl tự merge/dedupe vào Ads ASIN draft; user không copy tay, dữ liệu thủ công không bị ghi đè.
- Empty Ads Keyword/Ads ASIN vẫn được bỏ qua theo contract hiện có.

## 3. Hiện trạng code và khoảng cách

- `renderBookKeywordBuilder` và `renderAsinResearch` đang render thành hai card xếp dọc trong ASIN Research tab.
- `asinResearchDraftFor` giữ một `sourceText` riêng được seed từ Book Keywords, nên hiện tồn tại hai nguồn nhập keyword.
- `book.keywords.save` gọi `BookKeywordBuilder.Build(...)` rồi persist ngay; thao tác random và save chưa thể tách bằng UI-only change.
- `autoStageAsinCrawlResults` đã đưa final ASIN hợp lệ vào draft nhưng dựa trên stale check của Search Keywords cũ.
- `patchAsinResearch` và `refreshBookKeywordBuilderCard` đang patch hai subtree khác nhau; khi gộp UI phải giữ focus, scroll và không redraw toàn drawer.
- Layout/test hiện khẳng định ASIN Research card nằm dưới Keyword Builder, Ads ASIN nằm trong Generated output, và Build/Copy chung action row.

## 4. Contract UX đề xuất

### 4.1 Generated preview và save

- **Shuffle** tạo seed mới và preview mới từ snapshot của Generic Keywords + Book Keywords + Ads ASIN draft.
- Preview có trạng thái `Unsaved preview` và được giữ riêng theo `bookId` trong frontend.
- **Save** chỉ enabled khi preview tồn tại, hợp lệ và Book/Generic source không stale.
- Save rebuild server-side bằng cùng deterministic seed rồi persist đúng `Keyword 1`–`Keyword 7`, Ads Keyword và Ads ASIN user đang nhìn; không tạo seed/random lần hai và không tin generated fields từ WebView.
- Thay đổi Book Keywords đánh dấu preview `stale`. Thay đổi Ads ASIN hoặc auto-stage từ crawl gọi **Update preview** bằng seed hiện tại, giữ nguyên Keyword 1–7/Ads Keyword và chỉ cập nhật normalized Ads ASIN.
- Sau save thành công, preview trở thành saved output và badge chuyển `Saved`.
- Copy to Clipboard copy output đang hiển thị khi preview current; nếu preview chưa save, feedback nói rõ `Unsaved preview copied`. Copy bị disable khi preview stale.

### 4.2 Nguồn crawl

- Nguồn crawl được user khóa: Ads Keyword của preview hiện hành; nếu chưa có preview thì dùng saved Ads Keyword không stale. Reviewer khuyến nghị nguồn research ổn định khác, nhưng plan giữ yêu cầu sản phẩm và hiển thị rõ snapshot nguồn đang dùng.
- Chuỗi Ads Keyword được normalize bằng parser comma/newline hiện có, distinct case-insensitive, tối đa 30 phrases và 200 grapheme/phrase.
- Nếu không có Ads Keyword, disable Crawl ASINs và hiển thị `Shuffle keywords first`.
- Khi crawl bắt đầu, lưu snapshot normalized phrases dùng cho request.
- Nếu Ads Keyword thay đổi trước terminal result, result được hiển thị nhưng không auto-stage; feedback yêu cầu crawl lại.
- Khi crawl bắt đầu, đồng thời chụp snapshot Ads ASIN draft để chống ghi đè concurrent edit.
- Nếu result còn current, Ads ASIN chưa bị user sửa trong lúc crawl và có ASIN hợp lệ, merge case-insensitive vào Ads ASIN draft, giữ thứ tự hiện có, dedupe và gọi Update preview bằng seed hiện tại. Không Shuffle lại keyword.
- Nếu Ads ASIN bị sửa trong lúc crawl, vẫn hiển thị result nhưng không auto-stage; cung cấp feedback rõ để user chạy lại hoặc tự áp dụng.

### 4.3 Information architecture

```text
Keyword Builder
├─ Build inputs & ASIN research
│  ├─ Book Keywords (5 rows)
│  ├─ Amazon research source summary + crawl actions/status
│  └─ Crawl Results (5 compact rows desktop, scroll)
└─ Generated output
   ├─ Preview state
   ├─ Keyword 1..7
   ├─ Ads Keyword
   ├─ Ads ASIN draft (editable; auto-merged by crawl)
   └─ Shuffle | Save | Copy to Clipboard
```

- Bỏ standalone `ASIN Research` card và mô tả trùng lặp.
- Browser status, progress, Cancel và feedback nằm cạnh Crawl Results; kết quả không nằm trong live region bị cập nhật liên tục.
- Hai panel có surface cao bằng nhau ở desktop, nhưng Book Keywords/Crawl Results không bị stretch quá viewport quy định; dưới `1100px` stack thành một cột và dùng chiều cao nội dung tự nhiên.
- Giữ tab **ASIN Research** vì đây là entry point user đã chỉ định; heading trong panel giải thích cả keyword build và Amazon research.
- Dùng white surfaces, visible labels, live region riêng cho preview/crawl summary, progressbar có `aria-valuetext`, disabled reason qua `aria-describedby`, và không đánh cắp focus sau success.

## 5. Engineering direction

### 5.1 Domain/application

- Tách build thuần khỏi persistence ở `IBookCatalogMetadataService`:
  - build preview bằng explicit seed từ Generic + Book + Ads ASIN;
  - update preview bằng cùng seed khi chỉ Ads ASIN thay đổi;
  - save bằng cách rebuild server-side cùng seed rồi persist, không shuffle lại.
- Preview receipt mang seed, canonical input/generic fingerprints, algorithm version, build id và built timestamp để kiểm tra stale/compatibility; generated output không được dùng làm authority.
- Dùng independent deterministic random streams cho keyword slots, Ads Keyword và Ads ASIN để update một field không làm đổi các output khác.
- Save reject receipt thiếu/sai Book, sai fingerprint/algorithm, field vượt limit hoặc optional input không normalize được.
- Không thêm database hay background preview registry; receipt là stateless capability có integrity validation/binding với Book hiện tại.

### 5.2 Bridge

- Thêm `book.keywords.shuffle` → `book.keywords.shuffled`; không gọi state store và không refresh library.
- Thêm `book.keywords.preview.update-ads-asin` cho thay đổi Ads ASIN với seed hiện tại.
- Đổi `book.keywords.save` để chỉ nhận `{ bookId, receipt }`; receipt opaque là authority duy nhất, server verify rồi rebuild bằng seed và persist exact preview.
- Giữ mutation gate + processing-active guard cho Save; Shuffle là read/compute-only nhưng vẫn cần snapshot Book + settings.
- Structured errors phân biệt invalid input, stale preview và unsupported algorithm version.

### 5.3 Frontend state/patching

- Thêm preview map theo `bookId`; tách `shuffle pending` khỏi `save pending`.
- Xóa `asinResearchDraft.sourceText`; crawl source dựa trên normalized Ads Keyword snapshot của preview/saved output.
- Render crawl subsection bên trong panel trái nhưng giữ heading/region riêng, không gắn semantic label Crawl Results là input.
- Hợp nhất patch path đủ nhỏ để cập nhật progress/results mà không làm mất focus Book Keywords, Ads ASIN hay scroll của results.
- Khi crawl auto-stage Ads ASIN, merge nếu cả source và target snapshots còn current, sau đó update preview bằng seed hiện tại và patch đúng subsection.

## 6. Failure and rescue contract

| Trường hợp | Hành vi | Recovery |
|---|---|---|
| Shuffle validation fail | Giữ saved output/preview trước, focus Book Keywords | Sửa input, Shuffle lại |
| Save khi preview stale vì Book/Generic đổi | Không ghi file | Shuffle lại rồi Save |
| Save fail | Giữ preview và input | Retry Save |
| Crawl không có Ads Keyword | Disable Crawl | Shuffle trước |
| Ads Keyword đổi khi crawl chạy | Giữ rows, không auto-stage | Crawl lại |
| Ads ASIN bị user sửa khi crawl chạy | Giữ rows, không auto-stage | Giữ manual draft; crawl/apply lại |
| Crawl partial/cancel có ASIN hợp lệ | Merge nếu source/target còn current; update cùng seed | Review preview rồi Save |
| Browser challenge | Giữ rows/progress và preview | Resolve prompt, Crawl lại |
| Library refresh sau Save fail | State đã lưu, badge `Refresh needed` | Retry refresh hiện có |

## 7. Kế hoạch triển khai theo phase

### Phase 0 — Freeze contracts, seams and fixtures

- Add fixed seams `IKeywordSeedSource`, `IKeywordReceiptKeyProvider`, `IBuildIdFactory` and `TimeProvider`; production uses secure implementations, tests use fixed values.
- Check in v3 raw workspace fixture plus v4 canonical/golden-vector fixture; document fixture format and review-only regeneration command.
- Add dormant v4 primitives only; keep algorithm v3 active and all existing flows green.
- Acceptance: deterministic vectors and compatibility fixtures pass without changing runtime behavior.

### Phase 1 — Atomically activate deterministic Core v4

- Land pure labelled-stream builder, `AdsAsinPolicy`, bounds and nullable persisted seed/fingerprint/digest together.
- Switch `CurrentAlgorithmVersion` to 4 only in this slice; enforce invariant `v4 ⇒ all metadata present`, `v1–v3 ⇒ metadata absent`.
- Acceptance: every persisted v4 output is reproducible across fresh instances/processes; legacy states still load.

### Phase 2 — Application receipt lifecycle and saved-state hydration

- Add typed preview DTOs, HMAC receipt protector, Shuffle/Update/Open-saved/Save application service and idempotent Save.
- `OpenSavedPreviewAsync` mints a fresh process receipt from current persisted v4 state after restart; legacy state returns the one-time Shuffle requirement.
- Serialize Generic settings mutation with exact Save.
- Acceptance: restart → open saved v4 → same-seed update → exact save works; tamper/stale/retry cases are typed.

### Phase 3 — Additive Bridge contracts

- Add preview commands, command constants/DTOs, safe error boundary, trusted crawl-source resolution and crawl-by-kind conflict handling.
- Retain the current raw save/crawl handlers temporarily so this commit remains usable; mark them compatibility-only in tests.
- Acceptance: old UI still works while new contract tests pass; no receipt/seed/signature/raw exception crosses diagnostics or errors.

### Phase 4 — Browser baseline and frontend state reducer

- Add isolated `tests/PrintableBook.Desktop.Playwright.Tests/` with a passing baseline before markup changes.
- Add DOM-free `keyword-workflow-state.js` reducer/selectors and focused unit tests.
- Switch frontend to per-Book revisions, receipt hydration, Shuffle/Update/Save and request ownership while preserving old visual layout.
- Acceptance: new frontend uses only receipt contracts; late/cross-Book responses are discarded; each commit builds and passes.

### Phase 5 — Crawl source and auto-merge integration

- Remove Search Keywords state/use; start crawl from verified preview receipt or legacy saved build.
- Add source/target CAS, stable merge/dedupe and same-seed Ads ASIN Update for current v4 previews.
- Cover Completed/Partial/Cancelled/NeedsAttention/Failed with valid partial rows and manual-edit races.
- Acceptance: only server-resolved displayed Ads Keyword is crawled; current results draft but never auto-save.

### Phase 6 — Consolidated markup, CSS and accessibility

- Move Crawl Results/actions/status into the requested left panel under its own labelled region; remove standalone card/input.
- Implement five-line/five-row desktop baselines, balanced surfaces, responsive/zoom-safe layout and state-dependent action hierarchy.
- Implement scoped live regions, progress semantics, disabled reasons, focus/caret/scroll preservation and drawer dirty warning.
- Acceptance: rendered tests pass at wide, 1100px, 760px, narrow and 200% zoom.

### Phase 7 — Contract cutover and cleanup

- After frontend and tests use receipts, change legacy raw save to `keyword_preview_required` and reject raw crawl keywords.
- Remove obsolete draft fields/selectors/handlers and update stubs/contract tests.
- Acceptance: no dead Search Keywords/Build & Save path remains; complete suite is green.

### Phase 8 — Verification, docs and package safety

- Add reproducible `scripts/test-keyword-workflow.ps1 -Fast|-Full`, non-mutating CSS verification and unsigned desktop package smoke.
- Update README, User Guide, architecture, CI, migration/rollback runbook and release exclusion assertions.
- Full smoke: Shuffle repeatedly; exact Save; restart/hydrate; crawl/merge/update/save; legacy one-time upgrade; cancel/partial/challenge; refresh recovery.
- Acceptance: clean Windows checkout passes the canonical full command without signing secret, local corpus or external browser credentials.

## 8. Acceptance criteria

- Không còn Search Keywords input hoặc standalone ASIN Research card.
- Crawl request dùng chính Ads Keyword đang hiển thị và không yêu cầu user nhập lại.
- Book Keywords có đúng 5 text rows; Crawl Results có viewport 5 compact rows ở desktop/100% zoom và scroll, co hợp lý khi wrap/mobile/200% zoom.
- Shuffle không persist; Save không random.
- Save luôn rebuild cùng seed và ghi đúng preview user đang thấy; bị chặn khi receipt/source stale.
- Crawl result hợp lệ merge/dedupe vào Ads ASIN khi source/target current, không auto-save, không cross-Book và không xóa manual targets.
- Hai panel cân bằng ở desktop và stack không overflow ở responsive breakpoints.
- Không regression Copy, refresh retry, cancellation, browser challenge, empty optional Ads fields và current keyword packing limits.
- Targeted + full test suite pass; docs phản ánh workflow mới.

## 9. Open review points

- Exact preview validation boundary: resolved by deterministic server seed + integrity-bound receipt; không nhận generated fields từ WebView và không cần preview registry.
- Copy semantics: auto-decide copy output đang hiển thị; feedback phân biệt saved/unsaved.
- Action row: state-dependent — chưa có preview thì `Shuffle` primary; preview current thì `Save` primary và `Shuffle again` secondary; Copy tertiary; stale thì `Regenerate preview` primary và Save/Copy disabled có reason.

## 10. `/autoplan` Phase 1 — CEO/Product review

Mode: **SELECTIVE EXPANSION**. Premises về layout, 5-row viewports và split Shuffle/Save đã được user xác nhận. CEO review ban đầu nêu ba challenge; Phase 2 đã giải quyết bằng user-locked constraints, same-seed update và non-destructive merge.

### 10.1 Premise challenge

| Premise | Evidence checked | Verdict |
|---|---|---|
| Hai card xếp dọc làm workflow rời rạc | `renderAsinResearchWorkspace` hiện ghép hai card và giữ hai patch paths | Valid; gộp visual hierarchy |
| Search Keywords chỉ là duplicate input | `asinResearchDraftFor` seed một lần rồi cho phép sửa độc lập; crawler plan cũ gọi đây là research draft | **USER CHALLENGE**; đây có thể là intentional control chứ không phải duplicate |
| Ads Keyword là crawl source tương đương | `BuildAdsKeyword` trộn tối đa 20 Generic + 10 Book phrases và shuffle order | **USER CHALLENGE**; output quảng cáo random không phải stable research intent |
| Auto-stage toàn bộ ASIN là an toàn | selector lấy first unused organic title match `coloring book`; existing draft bị replace | **USER CHALLENGE**; có nguy cơ mất curated targets |
| Shuffle và Save phải độc lập | Backend hiện build + write trong một call, nên cần contract mới để Save đúng preview | Valid; deterministic seed làm contract enforceable |
| Hai panel cần cân bằng | Existing CSS đã dùng two-column stretch và responsive stack | Valid visual constraint; không được hy sinh result review/scroll |

### 10.2 What already exists / leverage map

| Sub-problem | Existing implementation | Reuse decision |
|---|---|---|
| Normalize Book/Generic phrases | `BookTextPolicy.NormalizePhrases` | Reuse, không tạo JS-only policy |
| Randomize slots/Ads fields | `BookKeywordBuilder` + `IKeywordOutputShuffler` | Giữ packing policy; thay stateful shuffler bằng pure versioned deterministic implementation |
| Per-Book unsaved input | `bookKeywordBuilderDrafts` | Reuse và mở rộng bằng preview metadata |
| Crawl normalization/fingerprint | `AmazonCrawlPolicy.NormalizeKeywords/Fingerprint` | Reuse làm canonical stale check |
| Background crawl lifecycle | Existing crawl session/worker/bridge commands | Không đổi worker/selection trong core scope |
| Scoped UI patching | `refreshBookKeywordBuilderCard`, `patchAsinResearch` | Hợp nhất thành one-card patch, giữ focus/scroll |
| Save mutation/refresh rescue | `beginCatalogMutation`, refresh-needed retry | Reuse |

### 10.3 Dream-state delta

```text
CURRENT
Two cards + two keyword drafts + Build/Save coupled
        │
        ▼
THIS PLAN
One workspace + explicit preview/save + crawl evidence in context
        │
        ▼
12-MONTH IDEAL
One Listing & Ads package with durable research receipts,
candidate quality review, marketplace/version history and repeatable export
```

Plan này chỉ tiến tới workspace thống nhất và trustworthy preview. Durable research receipt, multi-marketplace và richer candidate ranking không thuộc branch này.

### 10.4 Alternatives evaluated

| Approach | Effort | Risk | Pros | Cons | Decision |
|---|---:|---:|---|---|---|
| UI-only consolidation, giữ Build & Save | Low | Low | Minimal diff | Không đáp ứng split Shuffle/Save | Reject vì trái requirement đã xác nhận |
| Client-owned exact preview payload | Medium | High | Ít backend state | Không chứng minh output do builder sinh | Reject, boundary yếu |
| Backend preview registry | High | Medium | Exact, tamper-resistant | Expiry/restart/concurrency mới | Reject, over-engineered |
| Deterministic seed + input fingerprint | Medium | Low | Stateless, Save rebuild exact output | Cần seeded shuffle contract | **Select** theo explicit-over-clever |
| Crawl từ Ads Keyword | Low | High | Không thêm input | Random/global-biased/circular | User Challenge |
| Stable research draft / ordered Book Keywords | Medium | Low | Crawl identity ổn định | Giữ thêm concept | User Challenge |

### 10.5 Temporal interrogation

```text
HOUR 1  Domain seed + fingerprint + exact-rebuild tests
HOUR 2  Application preview/save split
HOUR 3  Bridge payloads, errors and mutation gates
HOUR 4  Frontend per-Book preview state + actions
HOUR 5  Consolidated markup/CSS + crawl source integration
HOUR 6+ Auto-stage edge cases, contract tests, docs, full regression
```

Dependency order is real: UI action split must not land before the exact-save invariant is testable.

### 10.6 System architecture review

```text
WebView Keyword Builder
  ├─ Book input draft ───────────────┐
  ├─ preview + opaque receipt        │
  ├─ crawl view/session              │
  └─ Ads ASIN draft                  │
              │ bridge JSON         │
              ▼                     │
WebViewBridgeRouter                  │
  ├─ book.keywords.shuffle ──────────┤ read/compute
  ├─ book.keywords.save ─────────────┤ validated write
  └─ asin-crawl.* ───────────────────┤ background session
              │                     │
              ▼                     │
BookCatalogMetadataService           │
  ├─ Load Generic settings           │
  ├─ Build(inputs, seed) ◀───────────┘
  ├─ Verify HMAC receipt + rebuild/compare on Save
  └─ StateStore.SaveAsync (atomic workspace state)
              │
              ▼
BookKeywordBuilderState (saved output + optional v4 seed/fingerprint)
```

No new package, database, service or external endpoint. 10x/100x load is bounded by one local Book interaction and current 30-query crawl cap; Amazon latency remains the first bottleneck, not preview generation.

### 10.7 Four-path data flow

```text
SHUFFLE INPUT → normalize → build(seed) → preview response → render
     ├─ nil/wrong type → invalid_keyword_builder request error
     ├─ empty → valid empty preview or explicit clear preview
     ├─ word >50 → structured validation error; old output preserved
     └─ snapshot unavailable → retry after refresh

SAVE opaque receipt → verify Book/signature → reload Generic/input snapshot → rebuild(seed)
     → compare fingerprint/output → atomic persist → refresh snapshot
     ├─ missing preview → Save disabled + backend reject
     ├─ stale inputs/settings → keyword_preview_stale; no write
     ├─ algorithm mismatch → keyword_preview_version_unsupported; reshuffle
     └─ write/refresh error → preview retained; retry Save/refresh

CRAWL phrases → AmazonCrawlPolicy normalize/fingerprint → worker rows
     → terminal result → stale check → Ads ASIN draft
     ├─ no phrases → Crawl disabled + backend validation
     ├─ challenge/timeout → partial rows + actionable feedback
     ├─ source changed → rows visible, no stage
     └─ valid current ASINs → stage only, never persist
```

### 10.8 Preview state machine

```text
SAVED/EMPTY ──Shuffle──▶ SHUFFLING ──success──▶ PREVIEW_CURRENT
    ▲                         │                    │   │
    │                         └─error──────────────┘   ├─input change──▶ PREVIEW_STALE
    │                                                  ├─Save──────────▶ SAVING
    │                                                  └─Shuffle───────▶ SHUFFLING
    │
    └──────── Save success ◀──────── SAVING
                                  │
                                  └─error──▶ PREVIEW_CURRENT

Invalid transitions rejected:
- Save without current preview
- Save while Shuffle/Save or conflicting processing mutation is active
- Persist a preview for another Book, another input fingerprint or algorithm version
```

### 10.9 Error & rescue registry

| Method/codepath | Failure | Rescue | User sees |
|---|---|---|---|
| `BookKeywordBuilder.Build(seed)` | invalid long word | typed validation exception | exact offending word/max |
| preview application method | settings/snapshot unavailable | typed catalog/snapshot error | refresh and retry |
| exact-save method | stale fingerprint/version/output | reject before state write | Shuffle again |
| state store save | IO/JSON/access failure | existing bridge error path, keep preview | Save failed; retry |
| shuffle bridge | malformed array/seed/bookId | request validation | inputs invalid |
| save bridge | missing/cross-Book preview metadata | request validation | preview invalid/stale |
| crawl start | empty/too many/oversized phrases | existing crawl validation | actionable field feedback |
| crawl worker | timeout/challenge/cancel/markup | existing partial terminal view | rows + recovery instruction |
| auto-stage | source changed/invalid ASIN/cross-Book | skip write and preserve rows | crawl again / invalid result |
| library refresh after save | refresh cannot start/fails | existing `Refresh needed` retry | saved; refresh needed |

No catch-all is added. Existing broad refresh-start catch remains a known compatibility behavior and must keep its explicit warning.

### 10.10 Security, quality, performance and operations

- Trust boundary: WebView never supplies authoritative generated slots, seed or fingerprints separately. Save supplies only Book ID + opaque HMAC receipt; application verifies, rebuilds and persists its own result.
- Add explicit Core + Bridge bounds for Book phrase count/size, total words, Ads ASIN tokens/bytes and receipt size; HTML rendering continues through `escapeHtml`.
- No secret, PII, filesystem path, package or network surface is introduced.
- Avoid a generic preview manager: the state is feature-local, per Book, and bounded.
- Preview build is O(number of normalized words); no cache is needed. UI maps are bounded by opened Books and should be cleared/reseeded on fresh snapshot.
- No production telemetry is added. Diagnostics use current structured bridge/task state; tests and user-visible status are the observability surface for this local app.
- Persisted-state change is additive (`ShuffleSeed`, `InputFingerprint` nullable). Old readers ignore additive JSON fields; v3 seedless state remains display/copy compatible.

### 10.11 Test coverage map

```text
DOMAIN
├─ seeded shuffle same seed → exact same 9 fields                 [UNIT]
├─ different seed → valid randomized ordering                    [UNIT]
├─ empty optional Ads fields                                     [UNIT]
├─ max grapheme + omitted words                                  [UNIT]
└─ normalize stored backward compatibility                       [UNIT]

APPLICATION / BRIDGE
├─ shuffle computes without state-store write/refresh             [UNIT/INTEGRATION]
├─ save rebuilds exact preview and writes once                    [UNIT/INTEGRATION]
├─ stale Generic/Book/Ads ASIN/version/cross-Book rejects         [UNIT]
├─ processing/mutation conflicts and malformed payload            [BRIDGE]
└─ save succeeds but refresh warns/retries                        [BRIDGE]

FRONTEND USER FLOW
├─ edit → Shuffle → preview → Save → saved                        [JS BRIDGE]
├─ rapid double Shuffle/Save disabled while pending               [JS BRIDGE]
├─ input change marks preview stale; failure preserves preview    [JS BRIDGE]
├─ crawl source snapshot/current-vs-stale terminal result          [JS BRIDGE]
├─ Book switch never leaks preview/session                        [JS BRIDGE]
├─ focus + caret + drawer/result scroll survive patches           [JS BRIDGE]
└─ 2-column/1100px/760px contracts + accessible labels            [LAYOUT]
```

Friday-2am confidence test: end-to-end bridge flow proves the value displayed after Shuffle is byte-for-byte the value persisted after Save. Hostile test mutates every client-returned output field and proves Save ignores it/rebuilds from seed. Chaos test fails refresh after a successful state write and proves the preview becomes Saved with a recoverable refresh warning.

### 10.12 Deployment, rollback and trajectory

```text
tests → package desktop → smoke existing workspace → release
                                      │ failure
                                      ▼
                         revert feature commit/release
                                      │
                                      ▼
                  old reader ignores additive v4 fields
```

- No feature flag: local app and additive/nullable state make revert practical after compatibility fixtures pass.
- Post-release smoke: old workspace load, Shuffle without write, exact Save, crawl cancel/partial, restart and reopen saved output.
- Reversibility: 4/5. Rollback preserves saved output because fields are additive, but loses same-seed update capability for builds created by the new version.

### 10.13 UX review at CEO depth

```text
Open ASIN Research tab
  → see Book inputs first
  → see crawl controls/results in same left work area
  → see generated package on right
  → Shuffle (preview feedback)
  → Save (durable feedback) / Copy
```

State coverage required: idle, empty, shuffling, unsaved preview, stale preview, saving, saved, refresh-needed, crawl running, partial, challenge, cancelled and failed. The deep UI decisions continue in Phase 2.

### 10.14 Dual voices consensus

Claude CLI was unavailable (`Not logged in`); an independent subagent was used as the fallback.

| Topic | Independent subagent | Codex | Consensus |
|---|---|---|---|
| Ads Keyword as crawl source | Critical: random/global-biased | Critical: wrong semantic source | **AGREE risk; user-locked source, mitigate with frozen labelled snapshot** |
| Shuffle→crawl→stale→Shuffle loop | Critical | Critical | **AGREE; same-seed Update preview removes loop** |
| Auto-replace ASIN | High: weak selector can erase curated target | Critical: restore review/apply | **AGREE; auto-merge/dedupe + target snapshot replaces destructive replace** |
| Exact preview boundary | deterministic seed or registry | deterministic seed | **AGREE — seed selected** |
| Persist research receipt | Six-month regret if omitted | High regret | Agree but outside current blast radius; defer |
| UI consolidation | Useful but should not drive domain semantics | Layout alone is not the product problem | Agree |

### 10.15 CEO implementation tasks

- [ ] **CEO-T1 (P1)** — Core/Application — Introduce deterministic seeded preview and exact rebuild-on-save contract.
- [ ] **CEO-T2 (P1)** — Frontend/Application — Freeze and label the user-locked Ads Keyword crawl snapshot.
- [ ] **CEO-T3 (P1)** — Frontend — Auto-merge/dedupe valid ASINs only when source and target snapshots remain current.
- [ ] **CEO-T4 (P2)** — Frontend — Consolidate visual hierarchy while retaining explicit states and scoped patching.
- [ ] **CEO-T5 (P2)** — Tests — Add exact-save, stale, cross-Book, failure and focus/scroll regression coverage.

Phase 1 completion: 11/11 review sections executed; 3 challenged risks resolved through explicit user constraints plus non-destructive/same-seed mitigations, 1 architecture decision auto-resolved, 0 security critical gaps.

## 11. Decision Audit Trail

| # | Phase | Decision | Classification | Principle | Rationale | Rejected |
|---:|---|---|---|---|---|---|
| 1 | CEO | Keep requested UI consolidation and 5-row viewports | Mechanical | P6 bias to action | Explicit user direction; existing CSS supports it | Preserve two standalone cards |
| 2 | CEO/Eng | Use deterministic seed + input fingerprint; Save rebuilds server-side | Auto | P5 explicit, P3 pragmatic | Exact preview without trusting WebView or adding registry lifecycle | Client output payload; backend registry |
| 3 | CEO | Copy the output currently displayed and label unsaved feedback | Auto | P1 completeness | Matches preview mental model | Saved-only copy |
| 4 | CEO | Defer durable research receipts/retry-failed | Auto-defer | P2 blast radius | Valuable but adds persisted crawler schema beyond requested UI workflow | Expand branch now |
| 5 | CEO/Design | Crawl from current Ads Keyword snapshot | User-locked + mitigated | User sovereignty, P5 explicit | User explicitly removed Search Keywords; freeze and visibly identify the source for each job | Reintroduce duplicate input |
| 6 | CEO/Design | Auto-merge/dedupe ASINs when source/target snapshots are current | Auto refinement | P1 completeness, P4 manual UX | Delivers no-copy workflow without erasing curated targets or racing user edits | Destructive replace; mandatory manual apply |
| 7 | Design | Update Ads ASIN preview with the existing seed | Auto | P1 completeness | Removes Shuffle→crawl→Shuffle loop while preserving reviewed keyword order | Force a new random shuffle |
| 8 | Design | Keep Ads ASIN editable in Generated output | User-locked | User sovereignty | Earlier explicit placement request controls over reviewer preference | Move field back to left panel |
| 9 | Design | State-dependent action priority | Auto | P4 manual UX, P5 explicit | Makes Generate primary initially and Save primary only for a current preview | Three equal-weight actions |
| 10 | Design | Keep ASIN Research tab label; clarify card heading | User-locked | User sovereignty, P2 blast radius | Requested entry point is ASIN Research; avoid unrelated navigation rename | Rename tab to Listing & Ads |
| 11 | Eng | Pure v4 SHA-256 counter-mode shuffle with labelled streams | Auto | P1 completeness, P5 explicit | Reproducible across calls/processes and isolates Ads ASIN changes | Seeded shared `Random`; call-order-dependent RNG |
| 12 | Eng | Opaque HMAC receipt; Save accepts only Book ID + receipt | Auto | P1 completeness, security boundary | Proves preview origin without registry or client-generated authority | Raw generated output; unsigned seed payload |
| 13 | Eng | Persist nullable seed/fingerprint/digest on v4 saved state | Auto | P1 completeness | Enables same-seed update after restart while loading legacy states | Memory-only seed; eager migration |
| 14 | Eng | Legacy v3 requires one Shuffle before saving ASIN draft changes | Auto | P2 blast radius, P3 pragmatic | Old random order cannot be reconstructed safely | Rewrite legacy output silently |
| 15 | Eng | Serialize settings.save and keyword Save | Auto | P1 correctness | Removes Generic-keyword TOCTOU race | Best-effort recheck without a shared boundary |
| 16 | Eng | Resolve crawl source server-side from receipt/saved build | Auto | P1 completeness, P5 explicit | Enforces user-locked Ads Keyword source at trust boundary | Accept raw keyword arrays from WebView |
| 17 | Eng | Per-Book monotonic revisions and request ownership | Auto | P1 correctness | Prevents cross-Book and out-of-order async mutation | Use selected Book at response time |
| 18 | Eng | One app-wide crawl conflicts by task kind | Auto | P2 blast radius | Avoids queued jobs running after their UI snapshots are obsolete | Model/persist multi-job queue |
| 19 | DX | Add new Bridge contracts before removing legacy payloads | Auto | P1 completeness | Every commit compiles and retains a working end-to-end path | Bridge-first breaking cutover |
| 20 | DX | Use BuildId as the single preview/saved identity | Auto | P5 explicit | Removes PreviewId/BuildId/savedBuildId ambiguity | Maintain aliases without mapping |
| 21 | DX | Put rendered tests in an isolated test package | Auto | P2 blast radius | Prevents Node/browser assets contaminating shipped Frontend | Add Playwright under production Frontend |
| 22 | DX | Add one Fast/Full verification entrypoint | Auto | P4 developer UX | Keeps local and CI commands synchronized | Maintain separate undocumented ladders |
| 23 | DX | Typed safe preview errors with secret redaction | Auto | P1 security/completeness | Makes recovery actionable without leaking receipt/crypto internals | Route through generic raw-message catch |
| 24 | DX | Whole-release rollback with explicit runbook | Auto | P5 explicit | Multi-commit schema/contract stack cannot be partially reverted safely | Claim one-commit rollback |

## 12. `/autoplan` Phase 2 — Design/UX review

Claude CLI was unavailable (`Not logged in`), so Design review used an independent delegated reviewer and a separate Codex review. Both reviewed the plan, current markup/CSS, responsive breakpoints and frontend tests. No production files were edited.

### 12.1 Hierarchy and information architecture

Final hierarchy:

```text
ASIN Research tab
└─ Keyword Builder & ASIN Research workspace
   ├─ Build inputs & ASIN research
   │  ├─ Book Keywords — textarea, 5 text rows
   │  ├─ Source summary — “Using N Ads Keywords from [saved/unsaved] output”
   │  ├─ Open Browser | Crawl ASINs / Cancel
   │  ├─ crawl status + progress
   │  └─ Crawl Results — labelled region, 5-row desktop viewport
   └─ Generated output
      ├─ Saved / Unsaved preview / Stale preview status
      ├─ Keyword 1–7 — read-only
      ├─ Ads Keyword — read-only crawl source
      ├─ Ads ASIN draft — editable, auto-merged by crawl
      └─ Shuffle again | Save | Copy preview
```

The left surface keeps the requested consolidated placement, but `Crawl Results` has its own heading/region so assistive technology does not identify evidence as an input. Ads ASIN remains in Generated output because its relocation there was an explicit prior requirement; its label and helper text make the editable draft role clear.

### 12.2 Primary journey and action hierarchy

1. Open ASIN Research; Book Keywords receives no forced focus.
2. Edit Book Keywords; old output becomes visibly stale and Save/Copy explain why they are disabled.
3. With no current preview, **Shuffle** is the primary action.
4. On preview success, keep focus on the activating button, announce once, and make **Save** primary; `Shuffle again` is secondary and `Copy preview` tertiary.
5. Crawl uses a frozen, visible Ads Keyword snapshot. During active crawl, source-changing Shuffle and Book Keyword editing are disabled; Cancel replaces Crawl.
6. Terminal valid results merge into Ads ASIN only if the source and Ads ASIN target snapshots are still current, then server-updated preview uses the same seed. Keyword 1–7 and Ads Keyword do not move.
7. Save persists that exact current preview. Save success updates status without moving focus.

This removes the circular `Shuffle → Crawl → stale → Shuffle` path while preserving the requested automatic draft behavior.

### 12.3 Composed interaction-state matrix

| Preview state | Crawl idle | Crawl active | Terminal crawl |
|---|---|---|---|
| No preview/saved Ads Keyword | Shuffle enabled; Crawl disabled with reason | impossible | n/a |
| Current unsaved preview | Save/Copy/Crawl enabled; Shuffle secondary | freeze Book source + Shuffle; Save/Copy remain available for frozen preview; Cancel enabled | merge + same-seed update if both snapshots current |
| Saved output current | Crawl/Copy enabled; Shuffle available | freeze source-changing controls; Cancel enabled | merge creates unsaved same-seed preview; Save becomes primary |
| Stale preview | Regenerate primary; Save/Copy/Crawl disabled with reason | existing job may finish but cannot auto-stage | keep rows; announce stale result and require regenerate/crawl |
| Shuffling/updating | related actions disabled; scoped busy status | no new crawl | n/a |
| Saving | Save disabled; keep preview visible | do not start a new crawl; active frozen crawl may continue | merge deferred/skipped if target changed |
| Refresh needed | state is durably saved; retry refresh action visible | Crawl allowed from saved snapshot | normal snapshot rules |

`NeedsAttention`, `Partial`, `Cancelled` and `Failed` retain rows already returned. Auto-merge is allowed only for normalized valid ASINs and only under the same source/target snapshot rules as Completed.

### 12.4 Spatial and responsive contract

- Desktop: two equal-width columns; panel surfaces stretch to the same row height, internal controls do not stretch.
- Book Keywords: HTML `rows="5"` plus CSS line-height/padding that preserves five visible text lines.
- Crawl Results: fixed compact-row token and `max-block-size` equal to five desktop rows plus separators; internal scrolling after five rows.
- Result keywords/status may wrap. At 200% zoom or narrow widths, accessibility takes precedence over an exact five-row count.
- Under `1100px`: one-column stack, content-driven panel heights and actions remain next to the content they affect.
- Under `760px`: controls/actions become full-width as needed; result rows use a two-line mobile token; no horizontal overflow.
- Remove the current `height: 100%`/stretch overrides that defeat the five-row constraints.

### 12.5 Visual and content specification

- Reuse existing white panel surface, border, radius, typography and color tokens; no new visual system.
- Three output states must not rely on color alone: `Saved`, `Unsaved preview`, `Stale preview` text badges.
- Source summary names its origin and count, e.g. `Using 30 Ads Keywords from unsaved preview`.
- Empty result copy: `No crawl results yet.` Disabled reason: `Shuffle keywords first to create Ads Keywords.`
- Remove the obsolete standalone ASIN Research description/card and Search Keywords counter/input.
- No warning about omitted remaining words; output capacity is represented by the seven field counters only.

### 12.6 Accessibility and focus contract

- Separate atomic live regions for preview status and crawl summary; result list itself is not live.
- Announce crawl milestones sparingly and one terminal summary exactly once.
- Progressbar exposes `aria-valuemin`, `aria-valuemax`, `aria-valuenow` and `aria-valuetext="N of M searches complete"`.
- Disabled actions have persistent visible explanations connected with `aria-describedby`; title text is insufficient.
- Validation failure focuses the first invalid field. Success, cancellation, merge and save do not steal focus.
- Preserve active element, textarea selection/caret, Ads ASIN caret and Crawl Results scroll during scoped polling patches.
- Use a labelled `ol`/`li` or equivalent stable list structure; maintain predictable DOM/tab order matching visual order.
- Drawer-close dirty detection includes Book input edits, current unsaved preview, Ads ASIN changes and staged crawl changes.

### 12.7 Visual QA and testability

Contract/string tests remain useful for command wiring but are insufficient for layout claims. Add rendered checks for:

- wide desktop two-column balance;
- 1100px transition and 760px/narrow stacking;
- 200% zoom, long keywords and wrapped statuses;
- five-line textarea and five compact result-row viewport at desktop/100%;
- no horizontal clipping and reachable action row;
- keyboard-only flow, focus/caret preservation, scroll preservation;
- live-region dedupe, disabled-reason association and progress semantics;
- Saved/Unsaved/Stale distinction and state-dependent button priority.

### 12.8 Dual voices consensus and resolution

| Finding | Delegated reviewer | Codex reviewer | Resolution |
|---|---|---|---|
| Resolve crawl source semantics | Critical | Critical | User-locked Ads Keyword; freeze/label snapshot |
| Avoid destructive ASIN replacement | High | Critical | Auto-merge/dedupe + concurrent target snapshot |
| Crawl Results is not semantically an input | High | High | Distinct labelled subsection within requested left panel |
| Ads ASIN input/output ambiguity | High | High | Keep user-locked location; label editable draft and rebuild same-seed preview |
| Permanent Shuffle-primary hierarchy | High | High | State-dependent primary action |
| Preview and crawl states not composed | High | High | Added cross-state matrix |
| Five rows vs stretch/wrap | High | High | Desktop baseline; accessibility override at zoom/mobile |
| Copy stale output | High | Medium | Disable Copy with visible reason |
| Accessibility too generic | Medium | Medium | Added testable live/focus/progress contracts |
| Rename tab/workspace | Medium | Medium | Keep user-specified tab; clarify inner workspace heading |

### 12.9 Design implementation tasks

- [ ] **DES-T1 (P1)** — Frontend — Implement the composed preview × crawl state model and scoped control disabling.
- [ ] **DES-T2 (P1)** — Frontend — Render the revised two-panel hierarchy with distinct labelled research/result regions.
- [ ] **DES-T3 (P1)** — Frontend/Application — Same-seed Ads ASIN preview update after safe auto-merge.
- [ ] **DES-T4 (P1)** — Accessibility — Implement live-region, progress, disabled-reason and focus/caret contracts.
- [ ] **DES-T5 (P2)** — CSS — Enforce five-row desktop viewports without stretch conflicts; responsive/zoom-safe stack.
- [ ] **DES-T6 (P2)** — Tests — Add rendered responsive/accessibility checks and update DOM/bridge contracts.

Phase 2 completion: 9/9 design review areas executed; 10 consensus findings resolved, 0 unresolved user challenges. Passing the revised plan to Engineering review.

## 13. `/autoplan` Phase 3 — Engineering review

Claude CLI was unavailable (`Not logged in`), so Engineering review used an independent delegated reviewer and a separate read-only Codex review. Both traced Core, application services, bridge routing, crawl sessions, frontend async handling and current tests. Locked product decisions remained feasible; no production files were changed.

### 13.1 Engineering verdict

The feature is implementable without a database or preview registry, but only after replacing the vague seed/fingerprint language with an explicit deterministic algorithm, signed receipt protocol, additive saved-state metadata and concurrency rules. Reviewer P0 findings are treated as P1 implementation gates because they are fully resolvable inside this branch and must land before UI work.

### 13.2 Deterministic builder contract

- Bump `BookKeywordBuilder.CurrentAlgorithmVersion` from `3` to `4`.
- Replace the shared mutable `IKeywordOutputShuffler` execution model for v4 with a pure `Build(inputs, KeywordShuffleSeed, buildId)` path. No request may share mutable RNG state.
- Seed is 32 random bytes generated server-side and encoded base64url without padding.
- Canonical bytes use versioned, length-prefixed UTF-8 fields; Unicode normalization is NFC before hashing/shuffling. CRLF/LF and delimiter variants normalize through the shared policies first.
- Derive independent streams as `SHA256(seed || canonicalLabel || uint64_be(counter))` for labels `slot/0` … `slot/6`, `ads-keyword`, and `ads-asin`.
- Fisher–Yates indices use unsigned big-endian samples with rejection sampling; never use `System.Random`, runtime hash codes or modulo-biased sampling.
- Adding/changing Ads ASIN consumes only the `ads-asin` stream, so Keyword 1–7 and Ads Keyword remain byte-for-byte stable under the same root seed.
- Golden test vectors fix canonical input bytes, derived samples and all nine output fields across processes.

### 13.3 Preview identity and opaque receipt

Application DTOs are separate from persisted `BookKeywordBuilderState`:

```text
KeywordPreview
  preview: BookKeywordBuilderState-like display fields
  receipt: opaque base64url envelope
  clientRevision: echoed, non-authoritative

Signed payload v1
  receiptVersion = 1
  bookId
  buildId
  builtAtUnixMilliseconds
  algorithmVersion = 4
  seed256
  normalizedBookKeywords
  canonicalAdsAsinInput
  genericFingerprint
  inputFingerprint
  outputDigest
```

- Sign the canonical payload with HMAC-SHA256 using a process-lifetime 256-bit secret registered as a singleton; verify with fixed-time comparison before parsing into an authoritative request.
- Receipt has a maximum encoded size of 256 KiB. Reject malformed encoding, unknown version, invalid signature, wrong Book, unsupported algorithm or digest mismatch with stable error codes.
- WebView receives the opaque receipt but never supplies generated fields, seed or fingerprints as separate authoritative values.
- Every content-changing Shuffle/Update creates a new `BuildId` and timestamp. Ads ASIN Update retains the root seed.
- Save receives only `{ bookId, receipt }`, reloads Generic settings, verifies its fingerprint, rebuilds from signed inputs/seed, compares `outputDigest`, and atomically persists the rebuilt state.
- Save retry is idempotent: if current saved state already has the same `BuildId`/digest, return `disposition: "already_saved"` without a second workspace write. Refresh recovery remains independently retryable.
- Process restart invalidates unsaved receipts by design; frontend preview memory also disappears. Persisted v4 saved output can issue a fresh receipt from its stored seed/inputs.
- On opening the ASIN Research tab after restart, frontend calls `book.keywords.preview.open` for a v4 saved `BuildId`; application reloads that exact current state and mints a fresh receipt without changing `BuildId`, `BuiltAtUtc` or output. Legacy state renders without a receipt and shows the one-time Shuffle requirement.

### 13.4 Additive persisted schema and legacy policy

Add nullable fields to `BookKeywordBuilderState`:

```text
ShuffleSeed: string?
InputFingerprint: string?
OutputDigest: string?
```

- New v4 saves populate all three; existing v1–v3 JSON loads with null fields and remains display/copy/crawl-source compatible.
- A v4 saved state can be rebuilt and updated after restart using its stored seed and current server-owned fields.
- A legacy seedless saved state may still start a crawl from its stored Ads Keyword and show results. Valid ASINs are drafted, but the one-time transition requires **Shuffle** before Save because legacy randomized ordering cannot be reconstructed. UI explains `Shuffle once to update this legacy output`.
- No eager migration rewrites workspace JSON. The first explicit Shuffle + Save upgrades that Book to v4.
- Rollback tests prove an older reader ignores additive fields and still renders the nine saved output fields; rollback loses v4 same-seed editing but not data.

### 13.5 Core canonicalization and bounds

Introduce one `AdsAsinPolicy` used by Shuffle, Update and Save:

- split comma/newline, trim, remove empty values, preserve first occurrence and dedupe case-insensitively;
- preserve legacy/manual tokens for compatibility; enforce the existing strict ASIN format only on crawler-produced additions;
- append valid crawler ASINs in result order after existing manual tokens;
- fingerprint the canonical token list, not raw whitespace;
- bounds: maximum 1,000 Ads ASIN tokens, 200 graphemes/token and 64 KiB UTF-8 canonical input.

Builder input bounds enforced in both Bridge and Core:

- maximum 500 Book phrases;
- maximum 200 graphemes/phrase;
- maximum 64 KiB UTF-8 normalized Book input;
- maximum 10,000 normalized words across Generic + Book inputs;
- maximum 256 KiB bridge payload for each keyword preview/save/crawl-source command.

Errors are versioned and field-specific. Frontend validation is convenience only; Core remains authoritative.

### 13.6 Bridge/API contract

```text
book.keywords.shuffle
  req  { bookId, bookKeywords[], adsAsin, clientRevision }
  res  { bookId, preview, receipt, clientRevision }

book.keywords.preview.open
  req  { bookId, buildId, clientRevision }
  res  { bookId, preview, receipt, clientRevision }
       // hydrate current persisted v4 output after process restart; no write

book.keywords.preview.update-ads-asin
  req  { bookId, baseReceipt, adsAsin, clientRevision, expectedAdsAsinRevision }
  res  { bookId, preview, receipt, clientRevision, adsAsinRevision }
       // same seed; new BuildId/receipt

book.keywords.save
  req  { bookId, receipt }
  res  { bookId, keywordBuilder, disposition: saved|already_saved,
         refreshTask?, refreshWarning? }

book.keywords.asin-crawl.start
  req  { bookId,
         source: { previewReceipt } | { savedBuildId },
         expectedAdsAsinRevision }
  res  { taskId, bookId, sourceFingerprint,
         baseReceiptDigest?, expectedAdsAsinRevision, ...existingView }
```

- Shuffle/Open/Update are read/compute calls: resolve Book from the latest completed snapshot, load Generic settings, validate bounded inputs and issue receipt; no state-store write or library refresh.
- Save remains inside the workspace mutation/processing guard.
- `settings.save` joins the same mutation gate as keyword Save so Generic validation cannot pass and become obsolete before persistence.
- Crawl start no longer accepts arbitrary raw keyword arrays. It validates the preview receipt or resolves the exact saved `buildId`, extracts server-owned Ads Keyword, runs `AmazonCrawlPolicy.NormalizeKeywords`, then derives the source fingerprint.
- Register all new commands in the bridge allowlist and structured error mapping. Legacy direct save payload returns `keyword_preview_required` rather than silently rebuilding/randomizing.

### 13.7 Concurrency and ownership rules

Per Book frontend state carries:

```text
previewRevision       monotonic integer
adsAsinRevision       increments on every user/auto edit
latestShuffleRequest  request ID or null
latestUpdateRequest   request ID or null
receiptDigest         identity of currently rendered preview
```

Each pending request records `{ command, bookId, previewRevision, adsAsinRevision, receiptDigest }`. A response mutates state only if Book, request ID and relevant revisions/digest still match; otherwise it is safely discarded and its busy flag cleared.

- Latest Open/Shuffle wins; an older out-of-order response never replaces it.
- Save snapshots one receipt. A crawl terminal event during Save may create a later unsaved preview, but it cannot change the in-flight persisted receipt or falsely mark the later preview Saved.
- Auto-merge requires matching crawl `sourceFingerprint`, `baseReceiptDigest` when present, and `expectedAdsAsinRevision`. A manual Ads ASIN edit—even if normalized text later matches—increments revision and blocks auto-merge.
- Book switch/close never redirects a response through `state.selectedBookId`; ownership comes from pending request metadata and response Book ID.
- Drawer-close dirty detection includes all per-Book revisions/unsaved receipt state.
- Enforce one app-wide active or queued ASIN crawl by background-task kind. Same-source duplicate may return the existing task; a different Book/source returns `amazon_asin_crawl_active` instead of silently queuing stale work.

### 13.8 Four critical data paths

```text
SHUFFLE
WebView bounded inputs → resolve Book/settings → normalize → seed256
→ pure v4 build → sign receipt → preview (no write)

UPDATE ADS ASIN
base receipt + expected revision → verify → canonicalize/merge target
→ v4 build with same seed → new BuildId + receipt (no write)

SAVE
bookId + receipt → fixed-time verify → shared mutation gate
→ reload settings/fingerprint → rebuild/digest compare
→ idempotency check → atomic workspace write → refresh/rescue

CRAWL
verified preview receipt or savedBuildId → server extracts Ads Keyword
→ normalize/fingerprint → single app-wide worker
→ terminal ownership/CAS checks → merge valid ASINs
→ UPDATE ADS ASIN → unsaved same-seed preview
```

### 13.9 Failure and recovery additions

| Code | Condition | Recovery |
|---|---|---|
| `keyword_preview_required` | direct legacy save payload/no receipt | Shuffle |
| `keyword_preview_invalid` | malformed/tampered receipt or digest | discard preview; Shuffle |
| `keyword_preview_expired` | receipt from prior process secret | Shuffle; saved output remains |
| `keyword_preview_stale` | Generic/input fingerprint changed | preserve inputs; Shuffle |
| `keyword_preview_version_unsupported` | receipt/builder version mismatch | Shuffle under current version |
| `keyword_preview_conflict` | late response/revision mismatch | keep newer state; no mutation |
| `keyword_input_too_large` | phrase/byte/word/token bound exceeded | focus associated input and reduce data |
| `amazon_asin_crawl_active` | another app-wide crawl active/queued | show owner/status; wait or cancel |
| `amazon_asin_target_changed` | Ads ASIN revision changed during crawl | keep results/manual draft; do not auto-merge |
| `keyword_legacy_shuffle_required` | seedless saved state needs target update | one-time Shuffle then Save |

### 13.10 Test and verification matrix

```text
Shuffle ──▶ canonicalize ──▶ deterministic build ──▶ sign receipt
   │             │                    │                  │
   └ bridge      └ policy unit        └ golden vector    └ tamper/Book/version

Open saved ──▶ load v3/v4 ──▶ hydrate or legacy gate ──▶ restart UI
   │                 │                  │                    │
   └ bridge          └ JSON fixtures    └ application        └ rendered/VM

Save ──▶ verify ──▶ shared gate/reload ──▶ rebuild/digest ──▶ write/refresh
  │         │               │                    │                │
  └ DTO     └ security      └ barrier race       └ exact output   └ idempotent/rescue

Crawl ──▶ trusted source ──▶ worker ──▶ source/target CAS ──▶ merge/update
   │             │             │              │                    │
   └ bridge      └ hostile raw  └ outcomes     └ async races        └ same-seed fields
```

**Domain golden/compatibility**

- v4 fixed vectors: ASCII, NFC-equivalent Unicode, CRLF/LF, duplicate case, empty optional fields and all labelled streams.
- same seed across fresh builder instances/processes gives exact nine fields; Ads ASIN-only change leaves the first eight fields unchanged.
- v3 seedless raw JSON fixture loads; v4 additive state round-trips; simulated old reader retains known fields.
- `AdsAsinPolicy` stable merge/dedupe, invalid crawler ASIN rejection, legacy manual-token preservation and all bounds.

**Application/bridge**

- Shuffle/Update never write or refresh; Save rebuilds and writes exact preview once.
- tampered/wrong-Book/expired/unsupported receipt; changed Generic settings; hostile client output fields are rejected/ignored.
- lost Save response retry returns `already_saved`; refresh-start failure reports saved + recoverable warning.
- barrier-controlled `settings.save` versus Save proves serialization.
- crawl source cannot be replaced by client raw keywords; preview/saved sources resolve to exact displayed Ads Keyword.
- one app-wide job, same-job rejoin, different-job reject and cancel/retry.

**Frontend ordering**

- Book switch with in-flight Shuffle/crawl; reversed Shuffle responses; Update after manual edit; terminal during Save; Save response after a newer preview.
- process-restart/receipt-expiry UI recovery and legacy one-time Shuffle message.
- scoped patching preserves focus, caret and result scroll.

**Rendered UI**

- Add a real Chromium harness against the static frontend (Playwright test dependency isolated from the shipped desktop bundle) for computed geometry, overflow, 1100/760 breakpoints, 200% zoom, keyboard focus and accessibility attributes. Source-string tests remain only wiring smoke tests.

### 13.11 Dual voices consensus

| Finding | Delegated reviewer | Codex reviewer | Resolution |
|---|---|---|---|
| Deterministic RNG underspecified/stateful | P0 | P1 | Pure SHA-256 counter streams + golden vectors; algorithm v4 |
| Saved same-seed update after restart | P0 | implicit compatibility concern | Persist nullable seed/fingerprints; explicit v3 transition |
| Receipt integrity undefined | P1 | P1 | Opaque HMAC-SHA256 receipt + fixed-time verify |
| Generic settings TOCTOU | P0 | P1 | Shared mutation gate |
| Crawl accepts arbitrary WebView keywords | P0 | P1 | Receipt/savedBuildId server resolution |
| Async cross-Book/out-of-order corruption | P1 | P1 | Per-Book revisions + request ownership |
| Ads ASIN policy missing | P1 | P1 | Shared Core stable merge/canonicalization policy |
| Input unbounded | P1 | P2 | Explicit Bridge/Core limits |
| App-wide crawl is actually queued by key | P1 | not raised | Reject different active/queued job by kind |
| Rendered claims lack harness | P2 | P2 | Add isolated Playwright computed-layout suite |

### 13.12 Engineering implementation order

1. Canonicalization constants, deterministic RNG v4, receipt format and golden/legacy fixtures.
2. Pure Core preview/update builder, `AdsAsinPolicy`, bounds and additive state fields.
3. Application preview/update/exact-save APIs, idempotency and shared settings/save mutation serialization.
4. Bridge commands, typed source receipt, allowlist/errors and crawl-by-kind conflict policy.
5. Frontend per-Book revision reducer, late-response guards and exact action/state handling.
6. Safe crawl auto-merge + same-seed update.
7. Consolidated markup/CSS/accessibility.
8. Targeted suites, rendered browser suite, full solution tests, packaging smoke and legacy workspace reopen.

### 13.13 Engineering tasks

- [ ] **ENG-T1 (P1)** — Core — Implement v4 canonical bytes, deterministic labelled streams and golden vectors.
- [ ] **ENG-T2 (P1)** — Core/Application — Implement Ads ASIN policy, bounded inputs and additive saved-state compatibility.
- [ ] **ENG-T3 (P1)** — Application — Implement signed receipt issuer/verifier and pure Shuffle/Update/Save methods.
- [ ] **ENG-T4 (P1)** — Bridge — Add typed commands, server-owned crawl source, mutation serialization and structured errors.
- [ ] **ENG-T5 (P1)** — Frontend — Implement per-Book revisions/request ownership and discard stale async responses.
- [ ] **ENG-T6 (P1)** — Crawl — Enforce app-wide job conflict and source/target CAS before auto-merge.
- [ ] **ENG-T7 (P2)** — Compatibility — Add v3/v4/restart/rollback/idempotency fixtures.
- [ ] **ENG-T8 (P2)** — QA — Add isolated real-browser responsive/accessibility tests.

Phase 3 completion: architecture/data flow/edge cases/security/performance/testing/rollout reviewed by two outside voices; 10 consensus or one-sided valid findings incorporated, 0 unresolved user challenges. Passing the locked contract to DX review.

## 14. `/autoplan` Phase 3.5 — Developer Experience review

Claude CLI was unavailable (`Not logged in`), so DX review used an independent delegated reviewer and a separate read-only Codex review. Both inspected the plan, monolithic router/frontend ownership, current test scripts, npm packaging, CI and release workflow. The current read-only baseline passed 109 bridge tests, 53 UI contract checks and production UI certification.

DX mode: **POLISH**. This is an internal implementation/API migration, so the developer is the next engineer executing and reviewing the branch rather than an external SDK consumer.

### 14.1 Developer journey map

| Stage | Current friction | Final plan experience | Verification |
|---|---|---|---|
| 1. Discover scope | Requirements distributed across conversation/current code | One reviewed plan with user-locked decisions and not-in-scope list | plan checklist |
| 2. Prepare toolchain | .NET/Node float; no browser-test setup | pinned SDK/Node files; one setup section | clean-checkout run |
| 3. Find ownership | 3,001-line `app.js`, 1,153-line router | explicit file/type ownership table and focused state module/handler | review paths |
| 4. Change Core | RNG/clock/IDs hard to control | injected deterministic seams + checked-in golden vectors | targeted Core tests |
| 5. Change contracts | raw/new payload cutover can break UI | additive Bridge commands, frontend switch, final legacy rejection | every commit green |
| 6. Debug behavior | strings and global selected Book obscure ownership | typed safe errors + per-request Book/revision metadata | race tests |
| 7. Validate UI | source-string tests cannot prove geometry | isolated real-browser suite with WebView shim | viewport/zoom matrix |
| 8. Run confidence loop | README/CI command sets differ | `test-keyword-workflow.ps1 -Fast|-Full` and CSS verification | same local/CI entrypoint |
| 9. Ship/rollback | signed package script blocks smoke; rollback prose only | unsigned package smoke + v3/v4 runbook and fixtures | package/runbook drill |

### 14.2 Developer empathy narrative

> I can start from one document and know which decisions are fixed. I can run one fast command before each commit and one full command before handoff. When a receipt or async response fails, the error names the field, cause and recovery without exposing secrets. I can open the small Core policy, application service, bridge handler or frontend reducer that owns the behavior instead of searching two monoliths. I can prove both legacy recovery and package cleanliness without a signing key, Amazon account, local corpus or production browser profile.

Initial TTHW (time to a trustworthy first change): **25–40 minutes**, with manual command discovery and no rendered harness. Target: **≤5 minutes** for warm `-Fast`, **≤15 minutes** for warm `-Full`; first browser installation may take longer and is reported separately rather than hiding download time.

### 14.3 File and type ownership

| Concern | Owner/file | Public names |
|---|---|---|
| v4 shuffle | `src/PrintableBook.Core/Domain/Books/KeywordShuffleV4.cs` | `KeywordShuffleSeed`, `KeywordShuffleV4` |
| ASIN canonicalization | `src/PrintableBook.Core/Domain/Books/AdsAsinPolicy.cs` | `AdsAsinPolicy` |
| saved output/schema | existing `BookKeywordBuilder.cs` / state record | `BookKeywordBuilderState`, `BuildId` |
| preview DTO/errors | `src/PrintableBook.Core/Application/Desktop/KeywordPreviewContracts.cs` | request/result records, `KeywordPreviewException` |
| receipt protection | `src/PrintableBook.Core/Application/Desktop/KeywordPreviewReceiptProtector.cs` | `IKeywordPreviewReceiptProtector` |
| lifecycle service | `src/PrintableBook.Core/Application/Desktop/IBookKeywordPreviewService.cs` + implementation | `OpenSaved`, `Shuffle`, `UpdateAdsAsin`, `Save` |
| production seams | `src/PrintableBook.Core/Application/Desktop/KeywordPreviewSources.cs` | seed/key/BuildId providers + `TimeProvider` |
| DI | existing `ServiceCollectionExtensions.cs` | singleton process key, service registrations |
| wire names/DTO parsing | `src/PrintableBook.Desktop/Bridge/KeywordBridgeCommands.cs` and `KeywordBuilderBridgeHandler.cs` | typed command constants/handlers |
| dispatch only | existing `WebViewBridgeRouter.cs` | delegate keyword commands; typed catch before fallback |
| pure frontend state | `src/PrintableBook.Desktop/Frontend/js/keyword-workflow-state.js` | one `window.PrintableBookKeywordWorkflow` namespace |
| DOM/transport | existing `Frontend/js/app.js` | render, events, scoped patching |
| Core fixtures | `tests/PrintableBook.Core.Tests/TestData/keyword-builder/` | v3 raw state + v4 vectors |
| reducer tests | `tests/PrintableBook.Desktop.Bridge.Tests/keyword-workflow-state.test.mjs` | DOM-free event/selector tests |
| rendered tests | `tests/PrintableBook.Desktop.Playwright.Tests/` | pinned package/lock/config/specs |
| runbook | `docs/runbooks/keyword-builder-v4.md` | migration/recovery/rollback |

Identity rule: use **BuildId** everywhere. A receipt `buildId`, persisted state `BuildId` and crawl `savedBuildId` refer to the same identity; an Ads ASIN content update gets a new BuildId while keeping the seed.

### 14.4 Test seams and fixtures

- `IKeywordSeedSource`, `IKeywordReceiptKeyProvider`, `IBuildIdFactory` and `TimeProvider` are mandatory constructor dependencies of the preview lifecycle service. Tests supply fixed bytes/IDs/time; production supplies cryptographically random seed/key and GUID BuildId.
- `tests/PrintableBook.Core.Tests/TestData/keyword-builder/v4-vectors.json` contains canonical input bytes, labelled-stream samples and nine expected fields. `v3-seedless-workspace.json` and `v4-seeded-workspace.json` cover reopening.
- Fixture generation is an explicit review-only command in the runbook. Automated tests read but never rewrite expected files.
- Activate algorithm v4 atomically with the pure builder + three persisted metadata fields. A dormant primitive commit may exist, but no state may claim v4 while using the current mutable shuffler.

### 14.5 Compile-safe cutover

1. Add dormant types/seams/fixtures with v3 still active.
2. Activate v4 atomically in Core and its current call sites.
3. Add lifecycle service and new Bridge commands **alongside** current handlers.
4. Add browser baseline and reducer, then migrate frontend requests/responses.
5. Prove no caller uses raw Save/crawl keywords.
6. Only then reject legacy payloads and remove old code.

No intermediate commit may knowingly break the existing end-to-end workflow. Treat dual contract support as branch-only migration scaffolding; it must not appear in a release.

### 14.6 Error and diagnostic contract

`KeywordPreviewException` owns a safe error record:

```text
{ policyVersion: 1,
  field: "bookKeywords" | "adsAsin" | "receipt" | "preview" | null,
  code: stable_snake_case,
  message: safe user-facing text,
  retryAction: "shuffle" | "retry" | "refresh" | "wait" | "none" }
```

- `KeywordBuilderBridgeHandler` catches typed preview validation/conflict exceptions before the router fallback and emits the versioned payload.
- Unexpected keyword-workflow exceptions return `keyword_preview_failed` with generic copy. Raw `Exception.Message`, receipt, seed, signature, HMAC key and canonical input bytes never cross the Bridge or enter user-visible diagnostics.
- Internal diagnostic context is limited to command, Book ID, BuildId when safe, error code and duration. Receipt values are always redacted.
- Frontend maps codes in one keyword-workflow error registry; every blocking error states problem + recovery, and every disabled action has the same visible reason.
- Tests assert catch order and absence of receipt/seed/signature/raw exception text in serialized responses and diagnostic events.

### 14.7 Reproducible test and packaging workflow

- Add `scripts/test-keyword-workflow.ps1`:
  - `-Fast`: targeted Core/Desktop filters, reducer tests, keyword/ASIN bridge name pattern, UI contracts and `git diff --check`.
  - `-Full`: restore/build, all six existing .NET suites with current opt-in exclusions, release-orchestrator test, all Node/UI tests, CSS verify, rendered browser suite and unsigned package smoke.
- Add `verify:css` to the existing frontend package: render Tailwind to a temporary file and byte-compare with committed `css/tailwind.css`; never rewrite during verify.
- Put Playwright exclusively under `tests/PrintableBook.Desktop.Playwright.Tests/` with an exact dependency version + lockfile. Serve real static frontend over loopback and inject the WebView bridge shim with `page.addInitScript`.
- Browser matrix: wide desktop, 1100px, 760px, 390px, plus 200% emulated zoom; assertions cover computed overflow/height, action reachability, keyboard/focus, ARIA and live-region output.
- CI installs that test browser/cache independently. It never points `PLAYWRIGHT_BROWSERS_PATH` at the production `.playwright` directory.
- Extract unsigned Desktop publish/filter/assert logic from `publish-release.ps1` into a shared helper used by both the signed release and `scripts/test-desktop-package.ps1`. Smoke requires no signing secret.
- Package assertions reject test package files, specs, traces, Node dependencies and test browser cache while retaining the production `.playwright` driver.
- Pin `.NET 10.0.401` through `global.json` with patch roll-forward and Node `24.18.0` through `.node-version`; CI reads the same files. Declare compatible engines in both npm packages.
- Resolve JS fixtures from `import.meta.url`, not `process.cwd()`, so tests work from any current directory.

Canonical commands:

```powershell
pwsh ./scripts/test-keyword-workflow.ps1 -Fast
pwsh ./scripts/test-keyword-workflow.ps1 -Full

npm --prefix src/PrintableBook.Desktop/Frontend ci
npm --prefix src/PrintableBook.Desktop/Frontend run verify:css
npm --prefix src/PrintableBook.Desktop/Frontend run test:ui

npm --prefix tests/PrintableBook.Desktop.Playwright.Tests ci
npm exec --prefix tests/PrintableBook.Desktop.Playwright.Tests -- playwright install chromium
npm test --prefix tests/PrintableBook.Desktop.Playwright.Tests

pwsh ./scripts/test-desktop-package.ps1 -Configuration Release -RuntimeIdentifier win-x64
git diff --check
```

### 14.8 Documentation and rollback handoff

Update:

- `README.md`: new workflow summary and canonical test entrypoint.
- `docs/user-guide.md`: ASIN Research sections, Shuffle/Save, crawl source, auto-merge, legacy message and troubleshooting.
- `docs/architecture.md`: v4 deterministic/receipt trust boundary, additive state and single crawl policy.
- `.github/workflows/build-and-test.yml` and `release-candidate.yml`: call the same Full path/provision browser without duplicating command drift.
- `docs/runbooks/keyword-builder-v4.md`: v3 fixture open, crawl, one-time Shuffle, expired receipt recovery, v4 reopen, backup, older-reader rollback and expected safe error codes.

Rollback is a whole-release revert, not a partial commit revert. Unsaved receipts are intentionally lost; the nine persisted fields remain readable; v4 same-seed editing is unavailable until the new release is restored. Never manually strip seed fields from workspace JSON.

### 14.9 DX dual voices consensus

| Dimension | Delegated reviewer | Codex reviewer | Consensus |
|---|---|---|---|
| Getting started <5 min | Missing single command | Command ladder incomplete | Confirmed: Fast/Full wrapper + pins |
| API/CLI naming guessable | DTO/file ownership unclear | BuildId/PreviewId ambiguous | Confirmed: ownership table + BuildId only |
| Error messages actionable | Recovery/error boundary incomplete | raw exception exposure risk | Confirmed: typed safe payload + redaction |
| Docs findable/complete | migration runbook missing | docs contradict new flow | Confirmed: explicit doc owners/runbook |
| Upgrade path safe | broken Bridge cutover | broken Bridge cutover + v4 activation | Confirmed: additive then atomic cutover |
| Dev environment friction-free | browser/CSS/package smoke unspecified | browser harness not executable | Confirmed: isolated package, CSS verify, unsigned smoke |

Consensus: **6/6 confirmed**, 0 disagreements.

### 14.10 DX scorecard

| Dimension | Initial | Planned | Why planned score is not 10 |
|---|---:|---:|---|
| Discoverability/ownership | 4/10 | 9/10 | Some integration remains in existing monoliths |
| Naming/API predictability | 5/10 | 9/10 | Wire versioning still requires reading contracts |
| Error/recovery quality | 5/10 | 9/10 | Local app has no durable preview history |
| Documentation | 5/10 | 9/10 | Runbook must stay synchronized manually |
| Test feedback speed | 6/10 | 9/10 | First browser download exceeds target |
| Test confidence | 5/10 | 9/10 | Real WebView2 host remains a manual smoke |
| Upgrade/rollback safety | 5/10 | 9/10 | Rollback loses same-seed editing capability |
| Environment/CI parity | 4/10 | 8/10 | Windows/browser provisioning remains heavier than unit tests |

DX overall: **4.9/10 → 8.9/10**. TTHW: **25–40 min → ≤5 min warm Fast / ≤15 min warm Full**.

### 14.11 DX implementation checklist

- [ ] **DX-T1 (P1)** — Add fixed seams, exact fixtures and atomic v4 activation rule.
- [ ] **DX-T2 (P1)** — Add file/type ownership and use BuildId consistently.
- [ ] **DX-T3 (P1)** — Use additive Bridge cutover; no broken intermediate commit/release.
- [ ] **DX-T4 (P1)** — Add typed safe error boundary and receipt/secret redaction tests.
- [ ] **DX-T5 (P1)** — Add isolated Playwright project, CI provisioning and package exclusion assertions.
- [ ] **DX-T6 (P1)** — Add Fast/Full wrapper, CSS verify and unsigned package smoke.
- [ ] **DX-T7 (P2)** — Pin toolchains and remove current-working-directory assumptions.
- [ ] **DX-T8 (P2)** — Update README/User Guide/architecture and add v4 migration/rollback runbook.

Phase 3.5 completion: DX overall 4.9/10 → 8.9/10; two outside voices raised 9 unique issues and agreed on all 6 consensus dimensions. All DX execution blockers are resolved; 0 new DX challenges/taste decisions, with the CEO crawl-source challenge carried to the final gate.

## 15. `/autoplan` Phase 4 — Consolidated final gate

### 15.1 CEO completeness appendix

**Failure Modes Registry**

| Failure mode | Severity | Prevention/detection | Recovery |
|---|---|---|---|
| Save output differs from preview | Critical | deterministic v4 + signed output digest + exact rebuild test | reject before write; Shuffle |
| Crawl overwrites manual ASIN edit | Critical | source fingerprint + target revision CAS + stable merge | retain manual draft/results; retry |
| Generic settings change during Save | Critical | shared mutation gate + barrier race test | `keyword_preview_stale`; Shuffle |
| Late response mutates another Book | Critical | pending request owns Book/revisions/digest | discard response; keep newer state |
| Tampered/cross-Book receipt | Critical | HMAC + fixed-time verify + Book binding | safe invalid-preview error; Shuffle |
| Legacy state cannot reproduce random order | High | nullable schema/version check | show saved output; one-time Shuffle before target Save |
| Crawl silently queues stale work | High | app-wide conflict by task kind | wait/cancel active owner, retry |
| Poll patch loses input/focus/scroll | High | scoped patch helpers + rendered regression test | preserve draft; rerender subsection only |
| Test/browser assets ship | High | isolated test package + unsigned package assertions | fail package gate |
| Save succeeds, refresh fails | Medium | persist response precedes refresh; existing rescue state | show Saved + Retry refresh |

**Explicitly not in scope**

- Change Amazon search parsing, first-match title relevance or candidate ranking.
- Add selectable candidates, manual Apply/Undo UI or persisted crawl research history/receipts.
- Add multi-marketplace crawl, retry-failed-row orchestration or multiple concurrent crawl jobs.
- Rename the ASIN Research tab or move Ads ASIN out of Generated output.
- Add product analytics/telemetry, a database, backend preview registry or network API.
- Redesign unrelated Book tabs, metadata, processing or release UX.

**CEO completion summary:** all premise, leverage, dream-state, alternative, temporal, architecture, data-flow, failure, security, test, rollback and UX passes are represented. Product completeness improves from a coupled Build/Save and duplicate-input workflow to an exact-preview, server-authoritative and recoverable workflow. Score: **9.0/10**; remaining gaps are intentionally deferred product capabilities, not missing contracts.

### 15.2 Design litmus scorecard

| Dimension | Initial | Planned | Dual-voice result |
|---|---:|---:|---|
| Information hierarchy | 6/10 | 9/10 | Confirmed |
| Primary journey/action priority | 5/10 | 9/10 | Confirmed |
| Interaction/state clarity | 4/10 | 9/10 | Confirmed |
| Responsive/spatial behavior | 6/10 | 9/10 | Confirmed |
| Accessibility/focus | 5/10 | 9/10 | Confirmed |
| Content/status clarity | 6/10 | 8/10 | Confirmed |
| Visual testability | 3/10 | 9/10 | Confirmed |

Design overall: **5.0/10 → 8.9/10**; consensus **7/7 dimensions**, 0 disagreements. The score stops below 10 because results remain compact evidence rather than a full candidate-review experience and exact five-row display yields to wrapping/zoom accessibility.

### 15.3 Engineering completion appendix

**Scope challenge:** a UI-only refactor was rejected because current Save randomizes while persisting, current crawl accepts arbitrary client keywords, and current async state can mutate the selected Book. The selected expansion is the smallest boundary that makes Shuffle/Save/crawl truthful: deterministic Core v4, stateless signed preview receipt, additive state fields, typed Bridge commands and per-Book revisions. A database, durable preview registry and crawler algorithm rewrite remain excluded.

**What already exists and remains reused:** Book phrase normalization/packing, workspace JSON state store, mutation gate, completed application snapshot, Amazon normalization/fingerprint/worker/session, library refresh rescue, per-Book drafts, scoped patch helpers, white panel tokens and current opt-in test exclusions.

**Engineering failure-gap assessment:** every critical path has a prevention, stable error and recovery; no silent persistence, cross-Book mutation or destructive ASIN replacement is accepted. Preview computation stays O(normalized input) within explicit bounds, and Amazon network latency remains dominant.

The independent test-plan artifact is stored at `C:/Users/admin/.gstack/projects/coloringbook/test-plan-keyword-builder-asin-workflow-20260929.md`. Section 13.8 maps all four codepaths to test layers and Section 13.10 defines the executable matrix.

Engineering overall: **9.2/10** after review. Consensus **6/6 dimensions** (architecture, trust boundary, migration, concurrency, bounds/performance, test/rollout), 0 disagreements. The remaining point gap reflects an intentionally manual real-WebView/package smoke layer.

### 15.4 Cross-phase themes

- **Exact preview authority** — CEO, Engineering and DX independently rejected trusting WebView-generated fields. Final contract uses deterministic rebuild + signed receipt.
- **Circular Shuffle/crawl workflow** — CEO and Design flagged it; Engineering made same-seed field-isolated Update executable.
- **Non-destructive ASIN staging** — CEO, Design and Engineering flagged curated-target loss/races. Final behavior is merge/dedupe + source/target CAS.
- **Async ownership** — Design, Engineering and DX all found combined state/late-response risk. Final frontend uses per-Book revisions and request identity.
- **Five-row layout vs accessibility** — both Design voices and DX testing review required a desktop baseline rather than an invariant at zoom/mobile.
- **Compatibility/cutover** — Engineering and DX independently required additive schema, saved receipt hydration, atomic v4 activation and a green dual-contract transition.
- **Test confidence** — Design, Engineering and DX agreed source-string assertions cannot prove responsive/focus behavior; final gate includes isolated rendered tests and package exclusion checks.

### 15.5 Final implementation commit stack

Every commit must compile and keep at least one working end-to-end path:

1. `test(core): add dormant keyword v4 seams and vectors` — fixtures/interfaces only; v3 remains active.
2. `feat(core): activate deterministic keyword builder v4` — pure streams, AdsAsinPolicy, bounds and additive state atomically.
3. `feat(app): add keyword preview receipt lifecycle` — Shuffle/Open saved/Update Ads ASIN/exact idempotent Save and settings gate.
4. `feat(bridge): add receipt keyword commands and trusted crawl sources` — typed safe errors; legacy handlers retained temporarily.
5. `test(ui): add isolated keyword workflow browser harness` — passing baseline, CI provisioning and release exclusions.
6. `feat(frontend): adopt revisioned keyword preview workflow` — reducer, hydration, Shuffle/Save/Copy and late-response guards.
7. `feat(crawl): merge ASIN results into same-seed preview` — server-owned Ads Keyword source, task conflict and CAS.
8. `feat(ui): consolidate ASIN research into keyword builder` — markup/CSS/accessibility + rendered assertions.
9. `chore: complete keyword v4 contract cutover` — reject/remove legacy paths, docs/runbook, Fast/Full/CSS/package gates and CI.

### 15.6 Aggregated implementation tasks

- [ ] **P1 Core contract** — deterministic v4/canonical bytes/field streams/golden vectors, `AdsAsinPolicy`, bounds and additive state invariants.
- [ ] **P1 Application boundary** — fixed seams, HMAC receipt, saved hydration, same-seed update, exact/idempotent Save and settings serialization.
- [ ] **P1 Bridge boundary** — typed DTO/constants/handler, trusted crawl source, safe structured errors, redaction and app-wide task conflict.
- [ ] **P1 Frontend state** — DOM-free reducer, per-Book revisions/request ownership, state-dependent actions, dirty close and legacy recovery.
- [ ] **P1 Crawl integration** — source/target CAS, stable auto-merge, all terminal outcomes and no auto-save.
- [ ] **P1 UI/accessibility** — consolidated two-panel layout, five-row desktop baselines, responsive/zoom, live/focus/progress contracts.
- [ ] **P1 Verification** — targeted race/tamper/restart/idempotency tests and real-browser geometry/accessibility matrix.
- [ ] **P2 Delivery** — Fast/Full wrapper, CSS drift check, unsigned package smoke, toolchain pins, CI parity and package exclusions.
- [ ] **P2 Docs** — README, User Guide, architecture and `docs/runbooks/keyword-builder-v4.md`.

### 15.7 Final review scores and status

| Phase | Score | Outside voices | Status |
|---|---:|---|---|
| CEO/Product | 9.0/10 | 6/6 consensus topics | Clean after mitigations |
| Design/UX | 8.9/10 | 7/7 dimensions confirmed | Clean |
| Engineering | 9.2/10 | 6/6 dimensions confirmed | Clean |
| Developer Experience | 8.9/10 | 6/6 dimensions confirmed | Clean |

Overall reviewed-plan readiness: **9.0/10**.

- Decisions: **24 total** — 21 auto/mechanical decisions, 3 user-locked constraints, 0 taste choices, 1 user challenge surfaced below.
- Deferred, not added to `TODOS.md`: persisted research history, richer candidate ranking/review, multi-marketplace, retry-failed rows and product analytics. They are outside this branch and do not block the requested workflow.
- `/autoplan` status: **REVIEW COMPLETE — READY FOR USER APPROVAL; implementation has not started.**

### 15.8 User approval challenge

**Challenge: use randomized Ads Keyword as the Amazon crawl source**

- User direction: remove Search Keywords and crawl directly from the displayed Ads Keyword.
- Both CEO and Design outside voices recommend a stable ordered research source instead because Ads Keyword mixes Generic + Book phrases and Shuffle changes its order/content selection, so research results may vary between previews.
- What the reviewers may be missing: the explicit product goal is one visible source of truth, and varying the research seed after an intentional Shuffle may be acceptable.
- Cost of changing direction: either reintroduce a separate research concept/input or crawl terms that no longer match the Ads Keyword user sees.
- Current plan keeps the user direction and reduces risk: server resolves the exact displayed Ads Keyword from a signed preview, freezes and labels that snapshot for the job, and blocks stale auto-merge.

Approval resolution (2026-09-29): **keep Ads Keyword**. Randomization only changes ordering/selection inside the same original maximum-30 phrase set and is accepted as non-impacting for this workflow. The frozen, labelled, server-resolved source snapshot remains required.
