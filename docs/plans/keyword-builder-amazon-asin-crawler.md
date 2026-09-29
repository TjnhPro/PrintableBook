<!-- /autoplan restore point: C:/Users/admin/.gstack/projects/PrintableBook/feat-book-keyword-builder-autoplan-restore-20260929-071311.md -->
# Keyword Builder — Amazon ASIN Crawler

## Trạng thái

- Loại tài liệu: implementation plan đã qua `/autoplan` review.
- Phạm vi hiện tại: chỉ plan và review; chưa triển khai code.
- Product stage: BUILD, local-first Windows MVP.
- Chiến lược: additive, không refactor Keyword Builder hay production workflow hiện có.
- Readiness: **conditional go**. Phase 0 là cổng go/no-go bắt buộc cho CloakBrowser, CDP loopback, license và release shape.

## 1. Mục tiêu và outcome

Trong Book Detail → Keyword Builder, user có thể:

```text
Book Keywords hiện tại
→ seed một lần vào Search Keywords có thể chỉnh sửa độc lập
→ mở/kết nối CloakBrowser profile local
→ browser warm-up Amazon Cart
→ JavaScript fetch lần lượt từng Amazon search page
→ trả HTML về C#
→ Html Agility Pack lấy ASIN + title
→ chọn tối đa 1 ASIN unique cho mỗi keyword theo thứ tự input
→ review/copy kết quả
→ chủ động thay draft Ads ASIN
→ Build & Save theo workflow hiện tại
```

Thành công của MVP là giảm thao tác search/copy thủ công nhưng vẫn giữ user ở vị trí quyết định cuối. Crawl không tự Save Book, không tự chạy cùng `Build & Save`, và không thay đổi thuật toán tạo `keyword_1`–`keyword_7` hoặc Ads Keyword.

## 2. Quyết định MVP đã khóa

| Chủ đề | Quyết định |
|---|---|
| Marketplace | Cố định `https://www.amazon.com` |
| Search depth | Chỉ trang kết quả đầu tiên |
| Sponsored | Loại khi text của toàn result item chứa `Sponsored`; chỉ organic result được phép chọn |
| Title filter | Regex `\bcoloring\s+books?\b`, ignore-case/culture-invariant; không match `colouring book` hoặc `coloring booklet` |
| Nguồn input | Seed một lần từ Book Keywords draft hiện tại; sau đó là session draft độc lập |
| Thứ tự | Giữ thứ tự keyword từ trên xuống |
| Assignment | Tối đa 1 ASIN chưa dùng cho mỗi keyword |
| Empty | Bỏ khỏi final string nhưng giữ row và lý do trong UI |
| Ads ASIN | `Use in Ads ASIN` thay toàn bộ draft; không merge |
| Persistence | Search draft, task và crawl result chỉ sống trong app session; không ghi Book state |
| Challenge | Dừng task, giữ partial result; không pause/resume, không bypass |
| Browser | Headed persistent profile; một session app-wide, một crawl tại một thời điểm |
| Warm-up | Cart + chờ cancellable 10 giây chỉ khi tạo/reconnect session, không lặp trên session còn `Ready` |
| Parser | Html Agility Pack là DOM parser chính; regex chỉ validate ASIN/marker nhỏ |
| Apply partial | Cho phép Copy/Use nếu partial result có ít nhất một ASIN và được gắn nhãn `Partial` |

## 3. Phạm vi

### Trong MVP

- Textarea Search Keywords cố định 5 dòng, một phrase mỗi dòng.
- Mở hoặc reconnect app-owned CloakBrowser profile bằng loopback CDP port.
- Execute JavaScript `fetch` trong Amazon page context để dùng cookie/session của profile.
- Trả typed response envelope và HTML về C#; parse bằng Html Agility Pack.
- Progress, status từng keyword, cancel, retry thủ công, copy và apply vào Ads ASIN draft.
- Partial result khi lỗi/cancel/challenge.
- Lifecycle theo từng Book, reattach khi đóng/mở lại drawer trong cùng app session.
- Bounded input, fetch, response size, retry và shutdown.
- Diagnostics an toàn và tài liệu first-run/release.

### Không nằm trong MVP

- Multi-page search, product detail, rank, review, price, image, bullet point.
- Auto bypass/solve CAPTCHA, proxy rotation hoặc stealth tuning UI.
- Crawl nhiều Book, nhiều browser/crawler chạy song song.
- Resume-only-failed, persisted crawl history, CSV export, manual candidate picker.
- Marketplace selector, sponsored toggle, `colouring book` locale support.
- Auto crawl khi `Build & Save`, auto merge hoặc auto persist Ads ASIN.
- General-purpose browser automation framework.
- Refactor Keyword Builder, WebView bridge hoặc background-task framework ngoài phần additive cần thiết.

## 4. Những gì codebase đã có

- WPF + WebView2 local UI với JSON bridge v1.
- Keyword Builder hai pane trong `Frontend/js/app.js`, draft theo `bookId`, Ads ASIN draft và `Build & Save` là điểm persist.
- Textarea Generic Keywords giữ chiều cao 5 dòng; Book Keywords giãn theo chiều cao pane và bố cục chuyển một cột tại breakpoint `1100px`.
- `BackgroundTaskManager` có lane, duplicate policy, progress, cancellation, typed `Result` và typed `View` nội bộ.
- Pattern typed session service qua `ProcessSessionService`.
- `task.get` hiện **chỉ** trả `BackgroundTaskBridgeSnapshot`; không trả typed View/Result.
- Cancelled worker không có typed Result; partial result chỉ có thể giữ qua `SetView` trước khi unwind.
- Interactive shutdown hiện chỉ chờ Process session; `BackgroundTaskManager.Dispose()` chỉ signal cancellation, không chờ crawler/browser đóng.
- Release là framework-dependent, single-file `win-x64`; package root chỉ cho phép executable và `Frontend/`.
- App root writable và updater chỉ thay controlled payload, nên runtime-created data có thể sống cạnh app mà không được đóng gói.

### Dream-state delta của MVP

Chỉ thêm một “research-to-draft loop” trong Keyword Builder. Không biến PrintableBook thành scraper platform, không đưa Amazon/browser concepts vào Book domain, và không tạo database mới.

## 5. Input và selection contract

### 5.1 Normalize và giới hạn

```text
split lines
→ trim
→ collapse whitespace
→ bỏ dòng rỗng
→ distinct OrdinalIgnoreCase, dòng đầu thắng
→ giữ nguyên thứ tự
```

- Tối đa `30` normalized keywords.
- Mỗi keyword tối đa `200` grapheme/character theo cùng policy text hiện có.
- Empty/all-whitespace: disable Crawl và backend reject `amazon_keywords_required`.
- Quá giới hạn: reject toàn request bằng field-level error; không silently truncate.
- Frontend validation để feedback sớm; backend là source of truth.

### 5.2 Fetch envelope

`CloakBrowser` không trả bare HTML; contract trung lập:

```text
BrowserFetchResponse
- status
- ok
- redirected
- finalUrl
- contentType
- html
```

- Search URI build ở C# bằng `UriBuilder`/query encoding; không chỉ replace space bằng `+`.
- JavaScript là fixed function do app sở hữu; keyword/URL truyền bằng JSON-serialized argument, không interpolate raw text thành source code.
- `fetch` dùng `credentials: "include"`, `redirect: "follow"`, `cache: "no-store"`; không dùng `no-cors`.
- `AbortController` timeout `20 giây` cho mỗi fetch; toàn crawl timeout tối đa `10 phút`.
- Chỉ HTTPS và exact host allowlist `www.amazon.com`/`amazon.com`; validate lại `finalUrl` sau redirect.
- Chỉ nhận HTML content type và tối đa `5 MiB` decoded response; vượt giới hạn trả safe error, không parse.
- Không giữ HTML sau khi parse và không đưa HTML vào task View/Result/log.

### 5.3 Parse DOM

- `AmazonCrawl` dùng Html Agility Pack và XPath; không dùng regex để parse toàn document.
- Root selector ưu tiên: `//div[@data-component-type='s-search-result' and normalize-space(@data-asin)!='']`.
- Title dùng đúng ordered fallback đã kiểm chứng: hai selector `span` đọc `InnerText`, sau đó hai selector đọc `aria-label` (`.a-size-base-plus.a-spacing-none.a-color-base.a-text-normal`, rồi `[data-cy='title-recipe'] h2`).
- HTML entity decode, collapse whitespace.
- ASIN uppercase và validate `^[A-Z0-9]{10}$`.
- Loại Sponsored trước normalize/dedupe khi `InnerText` của toàn result item chứa text `Sponsored`, tương đương function crawler đã kiểm chứng.
- Giữ đúng DOM order của các organic candidate còn lại.
- Phân biệt rõ:
  - `NoSearchResult`: có known empty marker.
  - `NoMatchingTitle`: có result nhưng không title nào match.
  - `AllCandidatesUsed`: có match nhưng ASIN đều đã chọn.
  - `UnexpectedMarkup`: không có result và cũng không có known empty marker.
  - `NeedsAttention`: CAPTCHA/robot/access denied/rate limited.

### 5.4 Chọn ASIN

```text
selected = OrdinalIgnoreCase set rỗng

foreach keyword theo input order:
    candidates = parser(html)
        → loại sponsored result
        → title match `\bcoloring\s+books?\b`
        → DOM order
    chọn candidate đầu tiên có ASIN chưa nằm trong selected
    nếu có: Selected
    nếu không: status/reason tương ứng

final = Selected ASINs join bằng dấu phẩy, không space, không phần tử rỗng
```

Ví dụ: `B0AAAA1111,B0BBBB2222,B0CCCC3333`.

## 6. Architecture

### 6.1 Dependency diagram

```text
Frontend / WebView bridge
        │ start/get/cancel/open
        ▼
AmazonAsinCrawlSessionService (Core application boundary)
        │
        ▼
AmazonAsinCrawlWorker (Core)
   ├─ IAmazonSearchPageClient ───────► Infrastructure/CloakBrowser/
   └─ IAmazonSearchHtmlParser ──────► Infrastructure/AmazonCrawl/

Infrastructure/CloakBrowser/ ↛ Infrastructure/AmazonCrawl/
Infrastructure/AmazonCrawl/  ↛ Infrastructure/CloakBrowser/
Infrastructure/CloakBrowser/ ↛ Book domain
Infrastructure/AmazonCrawl/  ↛ Book domain
```

### 6.2 `CloakBrowser/` boundary

Đặt tại `src/PrintableBook.Infrastructure/CloakBrowser/`. Module sở hữu browser/profile/CDP adapter; Desktop chỉ gọi lifecycle contract trong composition/shutdown. Module chỉ chịu trách nhiệm:

- open, connect/reuse và close browser;
- dedicated profile, process ownership, page/context và loopback endpoint;
- navigate URL do caller cung cấp;
- execute fixed JS fetch và trả `BrowserFetchResponse`;
- timeout/cancellation, crash/disconnect và browser state.

Không được reference Amazon parser, Book, Keyword Builder, Ads ASIN hoặc bridge DTO.

Target connection model:

1. Đọc session metadata do chính app tạo; không scan port.
2. Nếu PID/endpoint/profile marker còn hợp lệ, connect lại CDP.
3. Nếu stale/unreachable, xóa metadata stale và launch CloakBrowser persistent profile với CDP bind `127.0.0.1`.
4. Dùng OS-selected/discovered port, ưu tiên `--remote-debugging-port=0` + `DevToolsActivePort`; không hardcode public port.
5. Track `Owned` vs `Attached`; app chỉ terminate process `Owned`.

Data paths theo yêu cầu portable app hiện tại:

```text
<AppRoot>/.cloakbrowser/profile-v1/
<AppRoot>/.cloakbrowser/cache/
<AppRoot>/.cloakbrowser/session.json
```

App root vốn đã phải writable. Nếu không writable, trả `browser_storage_not_writable`; không fallback âm thầm. Release/update không package, backup, replace hoặc xóa `.cloakbrowser/`. Không dùng normal Chrome/Edge profile của user.

### 6.3 `AmazonCrawl/` boundary

Đặt tại `src/PrintableBook.Infrastructure/AmazonCrawl/`. Chỉ chịu trách nhiệm:

- nhận HTML response + fixed title regex/policy;
- parse DOM bằng Html Agility Pack;
- extract/normalize ordered ASIN + title candidates;
- detect empty/challenge/unexpected markup;
- trả typed parser result.

Không reference CloakBrowser, CDP, WebView, Book, bridge hoặc background task.

### 6.4 Core contracts

- `AmazonAsinCrawlRequest`: normalized keyword snapshot, marketplace và request fingerprint; không chứa Book model.
- `AmazonAsinCrawlView`: task state projection, rows, selected count, final string, progress, stop reason, request fingerprint.
- `AmazonAsinKeywordResult`: input index, keyword, selected ASIN, row status, safe reason code.
- Row status: `Pending`, `Searching`, `Selected`, `NoSearchResult`, `NoMatchingTitle`, `AllCandidatesUsed`, `Failed`, `Cancelled`, `NotProcessed`.
- Overall view outcome: `Idle`, `Running`, `Completed`, `Partial`, `NeedsAttention`, `Failed`, `Cancelled`.
- `IAmazonSearchPageClient`: neutral ensure/navigate/fetch contract.
- `IAmazonSearchHtmlParser`: HTML-to-candidates/diagnostic contract.
- `IAmazonAsinCrawlSessionService`: typed Start/Get/Cancel boundary analogous to `ProcessSessionService`.

### 6.5 Background task policy

- Thêm `BackgroundTaskKind.AmazonAsinCrawl`.
- Thêm lane `Amazon`, maximum concurrency `1`.
- Không conflict với Library Refresh, Process Interior, Production Action hoặc Cache Cleanup vì không dùng state/file pipeline chung.
- Duplicate policy `ReturnExistingByKey`.
- Stable key: `bookId + SHA-256(normalized ordered keywords + marketplace)`; `bookId` chỉ là task ownership/subject ở session service, không truyền vào hai module.
- Exact same key reattach task; request khác khi lane bận trả `amazon_asin_crawl_active`, tuyệt đối không join nhầm Book/input.
- Initial View có đủ rows ở trạng thái `Pending`.
- Worker `SetView` sau warm-up, trước/sau mỗi keyword và ngay trước mọi terminal exit.
- Thêm task kind vào latest-terminal retention để đóng/mở drawer vẫn đọc được view cuối.

Không mở generic `BackgroundTaskBridgeSnapshot` để serialize arbitrary View. Dùng typed session service/bridge projection riêng.

### 6.6 Bridge commands

- `amazon.browser.open`: open/reconnect/warm-up; trả typed browser state.
- `amazon.browser.status`: đọc `Closed | Checking | Downloading | Opening | WarmingUp | Ready | NeedsAttention | Error`.
- `book.keywords.asin-crawl.start`: validate `bookId` + keywords, start/reattach exact task.
- `book.keywords.asin-crawl.get`: trả typed crawl view ở Running/Cancelling/Completed/Failed/Cancelled.
- `book.keywords.asin-crawl.cancel`: validate ownership rồi dùng manager cancellation.

Không dùng `task.get` để lấy crawl result vì contract hiện tại không có View/Result. Không đổi behavior `book.keywords.save`.

## 7. Browser và worker flow

1. Frontend freeze normalized request snapshot khi start.
2. Session service xác nhận task ownership theo `bookId` và request fingerprint.
3. Worker gọi `EnsureReadyAsync`:
   - reconnect app-owned CDP session nếu còn hợp lệ;
   - nếu cần, launch persistent profile;
   - navigate `https://www.amazon.com/gp/cart/view.html?ref_=nav_cart`;
   - validate origin/response/readiness;
   - chờ cancellable 10 giây.
4. Với từng keyword:
   - build safe search URI;
   - fixed JS fetch trong page context;
   - validate envelope/status/redirect/content type/size;
   - parse HTML ở C# bằng Html Agility Pack;
   - chọn first unused matching ASIN;
   - drop HTML reference;
   - publish View;
   - jitter `1–2 giây` có cancellation.
5. Hoàn tất với `Completed` hoặc `Partial`; final string chỉ lấy row `Selected`.
6. Frontend lấy typed view, copy/apply theo Book hiện tại.

`Open Browser` là action phụ để login/recover/bring browser forward. `Crawl ASINs` vẫn gọi `EnsureReady`, nên user không bị buộc nhớ mở browser trước.

## 8. Error, continuation và rescue contract

| Trường hợp | Retry | Task behavior | UI/rescue |
|---|---:|---|---|
| Known empty/no match/all used | Không | Ghi row, tiếp tục | Hiển thị reason |
| Timeout/network/HTTP 408/500/502/504 cho một keyword | 1 lần | Vẫn lỗi: row `Failed`, tiếp tục | Partial summary; user có thể crawl lại |
| Unexpected markup | Không | Dừng `Failed`, remaining `NotProcessed` | “Amazon result layout changed; update the app/parser.” |
| HTTP 403/429/503 hoặc CAPTCHA/robot/access denied | Không | Dừng `NeedsAttention`, giữ partial | Open Browser, xử lý thủ công, start crawl mới |
| Browser closed/crashed/profile lock | Không | Dừng `Failed`, giữ partial | Open/reconnect; không auto loop |
| User cancel | Không | `Cancelling` → `Cancelled`; current `Cancelled`, remaining `NotProcessed` | Cho Copy/Use partial nếu có |
| App shutdown | Không | Stop accepting, cancel, bounded wait, close owned browser | Không prompt riêng nếu stop sạch |

### Error & Rescue Registry

| Code | Message phải có | Hành động user |
|---|---|---|
| `cloak_browser_download_failed` | Browser component download/install failed + nguyên nhân an toàn | Kiểm tra network/disk/antivirus và Retry |
| `cloak_browser_license_required` | CloakBrowser cần setup/license | Sign in/configure ngoài bridge rồi Retry |
| `cloak_browser_license_invalid` | License không hợp lệ | Kiểm tra key; không log key |
| `cloak_browser_profile_locked` | Profile đang được process khác giữ | Đóng browser/app instance khác rồi Retry |
| `cloak_browser_launch_failed` | Pinned version không launch được | Mở Diagnostics và Retry |
| `amazon_origin_not_ready` | Amazon session chưa ready | Open Browser |
| `amazon_fetch_timeout` | Keyword nào timeout | Retry crawl |
| `amazon_rate_limited` | Amazon rate-limited request | Chờ, xử lý trong browser, crawl lại |
| `amazon_challenge_detected` | Amazon cần user attention | Open Browser và xử lý thủ công |
| `amazon_markup_unsupported` | Parser không nhận diện result page | Dừng; update app/parser |
| `amazon_response_too_large` | Response vượt safe limit | Giữ partial; crawl lại sau |
| `amazon_asin_crawl_active` | Crawl khác đang chạy và Book nào sở hữu | Đợi/cancel task đúng Book |
| `amazon_crawl_result_stale` | Result không thuộc current Book/request | Mở đúng Book hoặc crawl lại |

Mọi message theo format: vấn đề + nguyên nhân có thể xác định + bước khắc phục. Không show exception stack, cookie, endpoint, profile path hoặc raw HTML.

## 9. Browser lifecycle và shutdown

- Browser session là app-wide singleton; crawl state/result là per-Book.
- Nếu user đóng browser bên ngoài, status chuyển `Closed/Error`; start sau tự reconnect/relaunch.
- Normal close, update restart và system shutdown phải đi qua một crawl/browser shutdown participant:
  1. không nhận work mới;
  2. cancel active crawl;
  3. wait tối đa `5 giây`;
  4. close page/context/CDP;
  5. gracefully close app-owned browser;
  6. last resort terminate **chỉ** owned process tree.
- Không kill attached/unowned process.
- `Environment.Exit` path và updater handoff phải không để orphan owned browser/profile lock.
- MVP không có nút Close Browser.

## 10. UI/UX contract

### 10.1 Information architecture

Giữ một Keyword Builder card. Thêm **ASIN Research** full-width sau grid hai pane hiện tại và trước feedback footer:

```text
Keyword Builder
├─ Header + saved/unsaved badge hiện tại
├─ Build inputs | Generated output hiện tại
├─ ASIN Research
│  ├─ Search Keywords (5 lines) + Browser status
│  ├─ Open Browser / Crawl ASINs / Cancel
│  ├─ Progress + ordered result rows
│  └─ ASIN Result / Copy ASINs / Use in Ads ASIN
└─ Keyword Builder feedback hiện tại
```

Không tạo card ngang hàng thứ ba hoặc modal UI mới. Reuse `keyword-builder-pane`, `keyword-list-input`, `control`, button, badge, spacing, focus và `--pb-*` tokens hiện có.

### 10.2 Draft và ownership

- State browser readiness là application-wide.
- Search draft/task/result map theo `bookId`.
- Draft lần đầu seed từ **current Book Keywords draft**, không từ generated output.
- Sau khi seed, chỉnh Search Keywords không đổi Book Keywords; Book Keywords đổi cũng không silently overwrite Search draft.
- Start freeze request snapshot và disable Search input đến terminal.
- Đóng drawer không cancel; polling tiếp tục. Mở lại cùng Book reattach bằng typed get.
- Book khác không hiển thị hoặc apply result của Book đang crawl.
- App restart mất research drafts/results đúng non-persistent contract.
- Start failure không xóa prior result. Edit Search sau completion giữ result nhưng gắn `Previous results — crawl again to refresh.`

### 10.3 Button/state matrix

| State | Search | Open Browser | Crawl | Cancel | Copy/Use |
|---|---|---|---|---|---|
| Browser closed | Enabled | Enabled | Enabled; auto EnsureReady | Hidden | Theo retained result |
| Checking/downloading/opening/warm-up | Enabled | Pending, disabled | Disabled | Hidden | Theo retained result |
| Ready + empty input | Enabled | Enabled | Disabled | Hidden | Theo retained result |
| Ready + valid input | Enabled | Enabled | Enabled | Hidden | Theo retained result |
| Running | Disabled | Enabled để surface browser | Disabled | Enabled | Disabled đến terminal |
| Cancelling | Disabled | Enabled | Disabled | `Cancelling…`, disabled | Disabled |
| Completed/Partial/Cancelled | Enabled | Enabled | Enabled | Hidden | Enabled khi final string khác rỗng |
| Needs attention | Enabled | Enabled | Disabled đến khi browser recheck Ready | Hidden | Enabled nếu partial khác rỗng |

First-run copy: `Downloading browser components (~200 MB)…` dùng indeterminate progress nếu SDK không có byte progress. Warm-up copy: `Preparing Amazon browser…` rồi `Waiting for Amazon session…`; không hiển thị giả `0 / N` khi chưa search.

### 10.4 Result presentation

- Ordered list vì input order quyết định priority.
- Mỗi row có text, không dựa riêng vào color:
  - `Selected — B0…`
  - `No search results`
  - `No title containing “coloring book”`
  - `Matching ASIN already used`
  - `Search failed — <safe reason>`
  - `Not searched — crawl stopped`
- Summary: `7 selected · 3 no match · 2 failed`.
- Result list cao khoảng 6–8 rows rồi internal scroll; không đẩy drawer quá dài.
- ASIN Result là `readonly`, không `disabled`, để focus/select/Ctrl+C.
- Zero match: `No matching ASINs found. Adjust Search Keywords and crawl again.`

### 10.5 Apply vào Ads ASIN

- Blank Ads ASIN: apply ngay.
- Giá trị đã giống result: không đổi, announce `These ASINs are already in the draft.`
- Giá trị khác: dùng `window.confirm` hiện có:
  - `Replace the current Ads ASIN draft with {N} crawled ASINs? This does not save the Book.`
- Cancel confirmation là no-op.
- Confirm chỉ đổi local Keyword Builder draft, bật unsaved indicator; không gửi `book.keywords.save`.
- Trước apply phải verify result `bookId` trùng Book đang mở và output đúng ASIN contract.
- `Build & Save` vẫn là điểm persist duy nhất.

### 10.6 Rendering, accessibility và responsive

- Poll chỉ patch ASIN Research subtree; không `outerHTML` toàn Keyword Builder card.
- Giữ drawer scroll, focus, caret/selection và cả ba draft inputs.
- `<section aria-labelledby="asin-research-title">`.
- Scoped `aria-live="polite"`, announce tối đa một lần mỗi keyword; challenge/unrecoverable error dùng `role="alert"`.
- Determinate search progress dùng `role="progressbar"` + min/max/now; warm-up indeterminate.
- Keyboard order: Search → Open → Crawl → Cancel → result actions.
- Trên `>1100px`, research dùng hai cột theo tỉ lệ grid hiện tại; `≤1100px` stack một cột; `≤760px` action wrap/full-width, không horizontal overflow.
- Long keyword/error wrap; ASIN selectable. Kiểm tra light/dark theme, không thêm font/gradient/card vocabulary mới.

## 11. Security và privacy contract

- CDP bind loopback only; không expose LAN/public, không scan arbitrary ports. Đây là direction user đã chọn; Phase 0 phải chứng minh wrapper hỗ trợ. Official direct persistent-context API chỉ là phương án đem ra review nếu gate này fail, không tự thay direction.
- Dedicated app profile; không attach normal user browser/profile.
- Session metadata không chứa cookie/credential.
- WebView không được truyền arbitrary URL/JavaScript; bridge chỉ nhận keyword array/bookId.
- Search URL, final redirect URL và content type đều validate ở trusted C# boundary.
- Không log keyword query URL, endpoint/port, raw HTML, cookie, profile contents hoặc license key.
- Diagnostics chỉ ghi event name, count, elapsed, status code category, pinned versions và safe reason.
- Review Amazon terms/policy và CloakBrowser binary/OEM terms trước release; feature không hứa bypass CAPTCHA/detection.
- Amazon automation phải nằm ngoài app WebView và không làm rộng bridge payload thành arbitrary URL/JavaScript. Việc harden source/top-level navigation chung của WebView được defer riêng vì là security scope hiện hữu, không phải dependency bắt buộc của crawler MVP.

## 12. Failure Modes Registry

| Failure mode | Detection | Containment | Test |
|---|---|---|---|
| CDP port conflict/stale metadata | Endpoint + owned marker validation | Không attach; relaunch safe | Integration |
| Second app instance/profile lock | Launch/connect exception | Safe error, không phá profile | Integration/manual |
| First-run Chromium download offline/blocked | Cloak install error | App vẫn mở; chỉ feature unavailable | Release/manual |
| Browser crash/external close | Disconnect/fetch failure | Stop task, retain view | Fake + manual |
| Amazon challenge/429 | Status + DOM markers | Stop, NeedsAttention | Fixtures + fake |
| Amazon markup drift | Không result/empty marker | Stop UnexpectedMarkup | Fixtures |
| Oversized/malformed HTML | Size cap/parser failure | Không parse/retain | Unit |
| Duplicate ASIN xuyên keyword | Selected set | Chọn candidate kế tiếp | Unit |
| Wrong Book applies result | Subject/fingerprint mismatch | Reject stale result | Bridge/frontend |
| Cancel mid-fetch/jitter | Cancellation + AbortController | View current/remaining đúng | Worker |
| Close/update during crawl | Shutdown participant | Bounded cancel/close, no orphan | Desktop integration |
| Poll redraw loses input/focus | Scoped DOM patch | Preserve state/caret/scroll | Frontend/manual |
| `.playwright/` thiếu/sai trong release | Published artifact smoke | Phase 0 no-go | Release/update smoke |

Critical gaps trước khi Phase 0 pass: exact CDP reconnect API, binary license/distribution, first-run download UX, single-file behavior và owned shutdown.

## 13. Test strategy

```text
Normalize/select algorithm ─────────► Core unit tests
HTML → candidates/diagnostic ───────► Infrastructure fixture tests
JS fetch/CDP/profile lifecycle ─────► Desktop adapter + local integration tests
Worker/View/cancel/duplicate ───────► Core/Desktop background tests
Bridge ownership/contracts ─────────► BridgeMessageContractTests
Markup/state/responsive contracts ──► BookWorkspaceLayout + frontend source tests
Real Amazon + published artifact ───► manual/opt-in smoke only
```

### Core tests

- Normalize/order/dedupe, 30/200 bounds, empty/invalid request.
- Title match, ASIN validation, first-unused selection, duplicate across keywords.
- Status mapping và final comma string.
- Exact duplicate key vs distinct request conflict.
- Mixed success completes Partial; cancel/failure retains typed View.
- Inject delay/time/jitter abstraction để unit tests không chờ thật.

### `AmazonCrawl` fixture tests

- Organic được giữ, Sponsored bị loại bằng result-item text; kiểm thử đủ bốn title fallbacks và entity-encoded titles.
- Missing ASIN/title, invalid ASIN, duplicate nodes.
- Known empty, no match, all used, challenge, 429/access denied, unexpected markup.
- Oversized/malformed document.

### `CloakBrowser` tests

- JSON argument escaping: quote, slash, ampersand, Unicode.
- Status/redirect/content type/size/timeout/cancellation.
- Profile lock, stale port, port collision, crash/reconnect, owned vs attached close.
- Cancel during launch, warm-up, fetch và jitter.
- Local same-origin test server chứng minh cookie đi cùng fetch mà không gọi Amazon.
- Không mock Playwright/Cloak types ngoài adapter; fake neutral interface.

### Background/bridge tests

- Start/Get/Cancel typed projection ở mọi task state.
- Initial rows, progress và partial view sau Cancelled/Failed.
- Task retention; close/reopen same Book reattach.
- Book A không thấy/apply task Book B.
- Shutdown normal/update/system và bounded wait.
- Command/payload validation và stable safe error codes.

### Frontend/UI tests

- Section label, `rows="5"`, fixed no-resize input.
- Button states, scoped live region/progress semantics, read-only result.
- Apply blank/equal/different-confirm/cancel; không phát save command.
- Scoped polling không mất focus/caret/scroll/drafts.
- Stale result label, per-Book map, active task ở Book khác.
- CSS contracts tại 1100/760 và bounded result overflow.
- Manual keyboard/screen-reader/light-dark smoke vì repo chưa có real DOM browser harness.

### Release smoke

- Restore/build/all existing test commands.
- Publish single-file `win-x64`; app startup không phụ thuộc browser runtime/network.
- Release ZIP chứa controlled `.playwright/` runtime cần thiết nhưng không chứa downloaded CloakBrowser Chromium binary/profile/cookie.
- First Open Browser download/install/retry; second run reuse pinned cache/profile.
- Upgrade/rollback app giữ profile và pinned browser policy.
- Real Amazon smoke là manual/opt-in qua `scripts/test-amazon-crawl-smoke.ps1`, không CI.

## 14. Kế hoạch triển khai theo phase

### Phase 0 — Compatibility spike và go/no-go

Mục tiêu: chứng minh toàn bộ risky path trước product code.

- Chọn exact CloakBrowser NuGet candidate và exact `BrowserVersion`; candidate tại thời điểm review là `0.5.11`, nhưng chỉ pin sau spike.
- Chọn và pin exact Html Agility Pack version theo convention inline `PackageReference` hiện tại.
- Chứng minh `.NET 10`/Windows x64, headed persistent profile, `Args`, Playwright/CDP và async JS evaluation.
- Chứng minh port model: loopback-only, OS-selected/discovered, reconnect đúng owned session, no port scan.
- Chứng minh Cart warm-up, 10-second delay, cookies, same-origin fetch và typed HTML envelope.
- Chứng minh timeout/cancel, response cap, browser crash, stale endpoint, external close, profile lock và second instance.
- Chứng minh actual framework-dependent single-file publish. Với `CloakBrowser 0.5.11`, review spike cho thấy `.playwright/` khoảng 87 MB và `playwright.ps1`; Phase 0 phải xác định runtime file nào cần giữ, loại script không cần nếu chứng minh được, và cập nhật release/updater allowlist, install, backup/rollback tests cho controlled `.playwright/`.
- Xác nhận first-run ~200 MB download/cache, license/login, binary verification, distribution/OEM terms và rollback.
- Chốt profile/runtime cache paths và third-party notices.

Exit gate: tất cả mục trên pass và có spike note. Nếu port/CDP model, release hoặc license không đạt, **dừng plan và review lại với user**; không tự đổi sang WebView2/HttpClient.

Commit khi triển khai: `spike: validate CloakBrowser runtime and CDP contract` (chỉ giữ artifact/test có giá trị; không merge throwaway code nếu gate fail).

### Phase 1 — Contracts và `AmazonCrawl`

- Thêm Core request/view/status/interfaces và constants bounds.
- Tạo `Infrastructure/AmazonCrawl/`, pin Html Agility Pack.
- Implement parser, diagnostics, filter/selection contracts.
- Thêm sanitized fixtures và unit tests trước browser thật.
- Thêm architecture tests bảo vệ module independence.

Exit: parser/algorithm deterministic, không reference Desktop/Book, fixtures xanh.

Commit: `feat: add Amazon search parsing contracts`.

### Phase 2 — `CloakBrowser` module

- Tạo `Infrastructure/CloakBrowser/` theo connection model đã pass Phase 0.
- Implement dedicated profile, session metadata, open/connect/reuse/close.
- Implement fixed JS capped fetch envelope, warm-up và browser status.
- Fake abstraction + local same-origin integration tests.
- Implement diagnostics redaction và ownership-safe shutdown primitives.

Exit: module không biết Amazon parser/Book; lifecycle tests xanh; no leaked process/profile lock.

Commit: `feat: add owned CloakBrowser session adapter`.

### Phase 3 — Worker, session service và bridge

- Thêm task kind/lane/policy/retention.
- Implement orchestration, retry matrix, jitter, SetView và cancellation rows.
- Implement typed session service Start/Get/Cancel.
- Thêm bridge commands/open/status và safe payload/error mapping.
- Add DI registrations without làm composition ambiguity.
- Integrate shutdown participant vào normal close, updater restart và system shutdown.

Exit: partial result readable ở mọi terminal state; Book/task ownership verified; bounded shutdown.

Commit: `feat: orchestrate Amazon ASIN crawl tasks`.

### Phase 4 — Frontend state và polling

- Thêm browser state global, research draft/task/result maps per Book.
- Seed-once behavior, request freeze/fingerprint và reattach.
- Add polling vòng đời độc lập với drawer; reject stale responses.
- Implement Copy/Use confirmation và draft-only apply.
- Patch scoped subtree, không redraw whole Keyword Builder.

Exit: close/reopen/switch Book không mix result; focus/draft/caret không mất.

Commit: `feat: connect ASIN research workflow to Keyword Builder`.

### Phase 5 — UI/UX và accessibility

- Render ASIN Research đúng hierarchy/state matrix.
- Progress, per-row statuses, summary, stale/partial/attention states.
- Responsive CSS 1100/760, bounded list, light/dark/a11y.
- Contract tests và manual keyboard/reflow checklist.

Exit: tất cả empty/loading/success/partial/error states rõ; không layout regression.

Commit: `feat: add Amazon ASIN research interface`.

### Phase 6 — Hardening, docs và release

- Dependency/license docs, first-run troubleshooting, profile/runtime cleanup guidance.
- Security review allowlist/CDP và redaction boundary.
- Full regression suite, published artifact smoke, real Amazon opt-in smoke.
- Update `README.md`, user guide, architecture, release packaging và third-party notices.
- Verify không đổi existing Keyword Builder/production workflow.

Exit: release artifact pass, license gate pass, no sensitive logs, offline app vẫn khởi động.

Commits: `test: harden Amazon ASIN crawler lifecycle` và `docs: document Amazon ASIN research workflow`.

### Phase 7 — Review lại từng nhóm sau triển khai

Review tuần tự, không gộp thành một smoke pass chung:

1. **CloakBrowser:** launch/connect/reuse/port/profile/fetch/close/release.
2. **AmazonCrawl:** fixtures/selectors/filter/dedupe/reason classification.
3. **Task/bridge:** lane/key/progress/view/cancel/retention/shutdown.
4. **Keyword Builder UI:** per-Book ownership, draft/apply, no redraw, responsive/a11y.
5. **Compatibility:** existing Build & Save, Process Interior, Production, update/release.

Mỗi nhóm ghi PASS/FAIL cùng evidence/test command. Chỉ tạo PR khi cả 5 nhóm PASS hoặc deviation được user chấp nhận rõ ràng.

## 15. Acceptance criteria

- Input normalize đúng order; >30 keyword hoặc keyword >200 ký tự bị reject rõ, không truncate.
- Mỗi keyword có đúng một row; final chỉ có selected unique ASIN, comma-separated.
- Sponsored excluded; organic candidate vẫn giữ DOM order và filter chỉ English `coloring book(s)`.
- Browser dùng app-owned persistent profile và loopback CDP; không attach arbitrary browser.
- Warm-up không lặp khi session còn Ready.
- HTML fetch trong browser context, trả về C#, parse bằng HAP; HTML không tồn tại trong View/log.
- Timeout/408/500/502/504 một keyword không làm mất prior result; 403/429/503/challenge dừng và remaining `NotProcessed`.
- Cancel bounded; partial rows/final string đọc được sau Cancelled/Failed.
- Exact duplicate reattach; distinct request không join nhầm.
- Closing/reopening same Book reattach; Book khác không hiển thị/apply result.
- Polling không redraw toàn card hoặc làm mất focus/caret/drafts/scroll.
- `Use in Ads ASIN` chỉ sửa local draft, confirm đúng lúc, không tự Save.
- Browser/app shutdown không để orphan owned process hoặc profile lock.
- App vẫn startup offline khi browser runtime chưa có.
- ZIP chứa đúng controlled `.playwright/`, không chứa downloaded Chromium/profile/cookie và release/license contract được xác nhận.
- Existing Keyword Builder, Book save, Process Interior và Production behavior không đổi.
- Full test suite, published artifact smoke và 5 group reviews đều PASS.

## 16. Verification commands dự kiến

```powershell
dotnet restore PrintableBook.sln
dotnet build PrintableBook.sln --configuration Release --no-restore
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "TestScope!=LocalCorpus&TestScope!=ExternalAmazon"
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --configuration Release --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
node src/PrintableBook.Desktop/Frontend/test-production-ui.mjs
```

External smoke riêng, explicit opt-in:

```powershell
./scripts/test-amazon-crawl-smoke.ps1
```

## 17. DevEx checklist

- Parser/worker/bridge tests chạy offline; real browser/Amazon không nằm trong CI mặc định.
- Exact wrapper/HAP/browser versions và tier documented một nơi.
- Fake browser, `TimeProvider`/delay và deterministic jitter giúp test nhanh.
- Error code catalog có problem + cause + recovery.
- First-run docs có dung lượng/network/disk/license/profile path và Retry.
- Không lưu license key trong settings, bridge hoặc diagnostics.
- Rollback app không xóa profile; browser upgrade có compatibility smoke riêng.
- Target developer TTHW cho unit path dưới 5 phút; external smoke là opt-in.

## 18. `/autoplan` review summary

| Review | Trước sửa | Sau quyết định plan |
|---|---:|---:|
| CEO/Product | 6/10 — outcome đúng, lifecycle mơ hồ | 9/10 — scope khóa, rescue/ownership rõ |
| Design | 5/10 — thiếu states/reattach/a11y | 9/10 — hierarchy + state matrix + responsive contract |
| Engineering | Conditional go — P0 gaps | Conditional go — gaps cô lập trong Phase 0/contracts |
| DevEx | 5.5/10 — first-run/release khó đoán | 9/10 target — offline tests + setup/recovery docs |

Cross-phase themes: typed partial result, per-Book ownership, Cloak compatibility gate, bounded shutdown, first-run/release UX và không redraw full card đều được nhiều review độc lập flag.

## 19. Decision Audit Trail

<!-- AUTONOMOUS DECISION LOG -->

| # | Phase | Decision | Classification | Principle | Rationale | Rejected |
|---:|---|---|---|---|---|---|
| 1 | CEO | Giữ feature là research-to-draft, không auto-save | Auto | Preserve user control | Giảm lỗi mà không đổi workflow | Auto persist |
| 2 | CEO | Khóa amazon.com/first page/sponsored excluded | User | Result quality | Không dùng paid placement làm ASIN research result | Marketplace/toggle |
| 3 | CEO/Eng | 30 keywords, 200 chars, 20s fetch, 10m global | Taste | Bound work | Ngăn task vô hạn, vẫn đủ Ads batch | Unbounded input |
| 4 | Design | ASIN Research full-width trong card hiện có | Auto | Hierarchy | Subordinate đúng Keyword Builder | Card/column thứ ba |
| 5 | Design | Seed once rồi independent per-Book draft | Auto | Predictability | Không overwrite input user | Live sync |
| 6 | Design | Scoped DOM patch | Auto | Preserve context | Tránh focus/layout regression | Whole-card redraw |
| 7 | Design | Replace Ads ASIN sau confirm | Auto | Explicit intent | Contract đơn giản, không merge mơ hồ | Auto merge |
| 8 | Eng | Typed crawl session service | Auto | Existing pattern | `task.get` không có View/Result | Generic View serialization |
| 9 | Eng | `ReturnExistingByKey` + conflict request khác | Auto | Correct ownership | Không join nhầm Book/input | ReturnExisting by kind |
| 10 | Eng | Partial result sống trong `SetView` | Auto | Fit current manager | Cancelled task không có Result | Rely on result only |
| 11 | Eng | Challenge stop, không pause | Auto | State-machine fit | Không có Paused/Resume contract | Hidden pause behavior |
| 12 | Eng | Hai folder độc lập trong Infrastructure; Desktop chỉ compose/shutdown | Auto | Dependency direction | Third-party adapters tách Book/UI | Coupled module |
| 13 | Eng | Loopback OS-selected CDP port, no scan | Auto | Least privilege | Port cố định dễ conflict/expose | Public/fixed port |
| 14 | Eng | Phase 0 hard no-go gate | Safeguard | Feasibility first | Wrapper chưa chứng minh exact port flow | Assume feasible |
| 15 | Eng | Stop và review nếu gate fail | Safeguard | No silent scope change | WebView2 fallback là direction mới | Auto fallback |
| 16 | DX/Eng | `.playwright/` controlled payload; Chromium lazy runtime data | Auto | Release correctness | Giữ driver cần thiết nhưng không bundle ~200 MB browser/profile | Bundle/omit blindly |
| 17 | DX | Fake-first CI, real Amazon opt-in | Auto | Deterministic tests | Không để external markup/network làm CI flaky | Live CI crawl |
| 18 | Cross | Review lại 5 nhóm sau implementation | Auto | Completeness | User yêu cầu tránh bỏ sót phase dài | Single final smoke |

## 20. Tài liệu tham chiếu

- [CloakBrowser repository](https://github.com/CloakHQ/CloakBrowser)
- [CloakBrowser .NET README](https://github.com/CloakHQ/CloakBrowser/blob/main/dotnet/README.md)
- Official docs xác nhận .NET persistent context, auto-download browser binary và first-run cache; exact CDP/port flow vẫn thuộc Phase 0 gate.
