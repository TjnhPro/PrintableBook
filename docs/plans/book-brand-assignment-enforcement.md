<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\plan-book-brand-author-mvp-assignment-enforcement-autoplan-restore-20260922-180115.md -->

# Kế hoạch bắt buộc Book Brand Assignment và loại bỏ Processing Brand override

## 1. Trạng thái và mục tiêu

- Giai đoạn hiện tại: lập kế hoạch và review, chưa triển khai code.
- Branch: `plan/book-brand-author-mvp`.
- Baseline đã review: commit `801e694`.
- Hướng thay đổi: additive, không refactor pipeline Interior/Production hiện tại.

Mục tiêu của phase này là biến `Book.AssignedBrand` đã persist thành nguồn sự thật duy nhất cho mọi thao tác Book-scoped có đọc asset của Brand. UI không còn cho phép chọn một `Processing Brand` toàn cục để ghi đè assignment của Book.

Book chưa assign hoặc assignment không còn hợp lệ phải bị chặn trước khi bất kỳ Brand asset nào được đọc. Hệ thống không tự assign, không tự đổi Brand và không âm thầm fallback sang Brand khác.

## 2. Các quyết định đã chốt sau autoplan review

| Chủ đề | Quyết định | Lý do |
| --- | --- | --- |
| Nguồn Brand cho Book action | Chỉ dùng `AssignedBrand` đã persist và được resolve từ fresh snapshot | Loại bỏ hai nguồn sự thật và ngăn xử lý nhầm Brand |
| Book chưa assign | Chặn toàn bộ action cần Brand với lỗi có hướng xử lý | Đây là product contract đã chốt |
| Assignment invalid | Chặn và hiển thị nguyên nhân cụ thể; không tự unassign/reassign | Giữ dữ liệu gốc và recovery rõ ràng |
| Batch nhiều Book | Chỉ chạy khi mọi Book valid và cùng một Brand | Pipeline hiện nhận một bộ Brand assets cho cả session; tự chia batch nằm ngoài MVP |
| Global selector | Xóa `Processing Brand` khỏi header và xóa vai trò production context của `selectedBrand`/`activeBrand()` | Selector không còn ý nghĩa sau khi assignment là bắt buộc |
| Brand page | Giữ `inspectedBrand` cho browse/edit/validate Brand | Đây là inspection context, không phải execution context |
| Bridge compatibility | Frontend mới không gửi `brandName`; backend tạm nhận field cũ như một assertion, chỉ cho qua khi trùng assignment rồi discard | Tương thích request cũ nhưng tuyệt đối không cho field cũ làm override |
| Session snapshot | Có thể giữ `ProcessSessionSnapshot.BrandName` như output-only resolved value | Tránh schema churn; giá trị này không được dùng làm input authority |
| Migration | Không migration dữ liệu; state cũ thiếu `AssignedBrand` load thành `Unassigned` | Additive và rollback đơn giản |
| Reassignment | Không tự xóa/copy lại output hoặc template đã tạo trước đó | Cleanup tự động nằm ngoài scope; action tiếp theo luôn dùng assignment mới |

### 2.1 Constraint kỹ thuật quan trọng

Pipeline hiện tại tạo một `ProcessingSessionWorkerRequest` với một Brand rồi dùng chung `frame.png`, `background.png` và automatic `IntroTemplate` cho cả batch. Vì vậy MVP không thể chạy một batch chứa nhiều assigned Brand nếu không refactor pipeline.

Quyết định: preflight reject toàn bộ batch bằng `mixed_assigned_brands_not_supported`. Không chạy một phần, không tự partition và không tự chọn Brand đầu tiên.

## 3. Phạm vi guard

### 3.1 Action bắt buộc assignment hợp lệ

| Action/surface | Brand asset được dùng | Yêu cầu |
| --- | --- | --- |
| Process Interior (`InteriorOnly`) | `frame.png`, optional `background.png`, automatic `IntroTemplate` | Mọi Book valid và cùng assigned Brand |
| Full Book processing API (`FullBook`) | Cùng worker và Brand assets như Interior | Cùng guard dù hiện chưa có entry point UI chính |
| Build Final Interior (`ProductionInterior`) | Brand frame/background/automatic Intro cộng Book-owned prefix pages | Một Book, assignment valid |
| Copy Brand Templates | `cover.psd`, `app_plus.psd`, `book_owner.psd` | Resolve Brand từ Book assignment |
| Automatic Intro preview/readiness | Danh sách và preview từ `IntroTemplate` | Chỉ đọc/render từ assigned Brand valid |
| Process/template request serialization | Hiện đang gửi `activeBrand()` | Không gửi Brand do UI chọn |

### 3.2 Action không bị block chỉ vì Book chưa assign

Các action dưới đây dùng asset riêng của Book, không đọc Brand directory tại thời điểm chạy, nên không thêm assignment guard:

- Save Book Information.
- Assign/reassign/unassign Brand.
- Import/upload Production PNG.
- Process Interior Cover.
- Process Book Owner.
- Build Cover PDF từ `final-cover` trong Book workspace.
- Chọn cover, chỉnh frame mode, chọn custom Intro từ Book Interior.
- Browse/edit/validate Brand trong Brands & Templates.

Nút Process vẫn bị chặn dù custom Intro dùng asset của Book, vì cùng pipeline vẫn luôn dùng Brand frame và có thể dùng Brand background.

## 4. Hiện trạng code và nguyên nhân cần thay đổi

### 4.1 Backend

- `BookBrandExecutionPolicy.Evaluate(...)` hiện cho phép `Unassigned`.
- `IProcessSessionService.StartAsync(...)` nhận `brandName` từ caller.
- `ProcessingSessionWorkerRequest` lưu `BrandName`; worker lookup Brand và dựng pipeline từ giá trị này.
- Worker đã phát hiện nhiều assigned Brand, nhưng bỏ qua Book unassigned khi gom distinct Brand. Batch gồm Book assigned + Book unassigned vì vậy vẫn có thể lọt qua với Brand do caller chọn.
- `book.brand.templates.copy` hiện bắt buộc cả `bookId` và `brandName`, nên vẫn phụ thuộc global selection.
- Worker và template copy đã có `GetFreshAsync`; đây là điểm phù hợp để đặt authoritative guard.

### 4.2 Frontend

- `state.selectedBrand` và `activeBrand()` đang được dùng cho process, template copy, automatic Intro và readiness copy.
- Header còn hiển thị `Processing Brand`.
- Brands page đã có `state.inspectedBrand`; state này có thể tiếp tục độc lập.
- Sau assign/reassign, refresh cục bộ hiện chỉ cập nhật catalog card. Intro, Production, copy readiness và Process controls có thể stale.
- Không được giải quyết stale state bằng cách redraw toàn bộ Book panel, vì sẽ làm mất focus/scroll và tái phát lỗi panel redraw đã vá. Cần rerender có mục tiêu các section phụ thuộc Brand.

### 4.3 Test/docs đang encode behavior cũ

- Core test hiện assert Book unassigned được phép execute.
- Nhiều worker fixture thành công nhưng không có assignment.
- Frontend test assert payload copy có `{ bookId, brandName }` và test global Brand switching.
- Layout contract yêu cầu text `Processing Brand`.
- `docs/user-guide.md` còn mô tả legacy Book chưa assign được dùng workflow cũ.

## 5. Architecture đề xuất

### 5.1 Shared execution resolver nhỏ, không tạo abstraction lớn

Thêm một resolver ở Core, cạnh `BookBrandExecutionPolicy`, ví dụ:

```text
BookBrandExecutionResolver.ResolveBook(snapshot, bookId)
  -> DiscoveredBook
  -> BookDesktopSummary
  -> persisted AssignedBrand
  -> canonical DiscoveredBrand
  -> success hoặc typed failure

BookBrandExecutionResolver.ResolveBatch(snapshot, orderedBookIds)
  -> ordered Book contexts
  -> một canonical DiscoveredBrand duy nhất
  -> success hoặc typed failure
```

Resolver có trách nhiệm:

1. Giữ nguyên thứ tự `bookIds` từ request để lỗi luôn deterministic.
2. Tìm đủ `DiscoveredBook` và `BookDesktopSummary`.
3. Reject `Unassigned`.
4. Reject mọi status khác `Valid`: `BookAuthorMissing`, `BrandAuthorMissing`, `AuthorMismatch`, `MissingBrand`, `BrandMetadataUnavailable`.
5. Resolve đúng persisted Brand; không chọn default và không fallback.
6. So sánh batch bằng canonical `DiscoveredBrand.Name`, sử dụng comparer identity hiện hành của discovery; không dùng Author match để suy ra Brand.
7. Reject batch có hơn một canonical Brand.
8. Trả về Book, summary và Brand đã resolve để caller không lookup lại từ input khác.

Không đưa validation asset của Brand vào resolver. Assignment validity và Brand asset certification là hai concern khác nhau; caller tiếp tục chạy validation hiện có sau khi resolve assignment.

### 5.2 Validation order bắt buộc

Để lỗi rõ ràng và không phụ thuộc thứ tự dictionary/discovery, backend dùng thứ tự sau:

1. Validate request shape: mode, non-empty/distinct Book IDs, single Book cho `ProductionInterior`.
2. Fresh snapshot.
3. Resolve từng Book theo đúng request order; reject Book/snapshot summary bị thiếu.
4. Reject Book unassigned hoặc assignment invalid; báo Book đầu tiên bị lỗi.
5. Sau khi tất cả Book valid, reject mixed canonical Brands.
6. Nếu request cũ có `brandName`, chỉ kiểm tra nó trùng resolved Brand; mismatch thì reject và không enqueue/copy.
7. Kiểm tra Book readiness theo rule hiện có.
8. Kiểm tra Brand validation/fingerprint theo action hiện có.
9. Chỉ sau tất cả bước trên mới đọc/copy Brand asset hoặc dispatch pipeline.

Thứ tự này đảm bảo batch assigned + unassigned báo đúng Book cần assign trước, thay vì một lỗi mixed Brand gây khó hiểu.

### 5.3 Ba lớp bảo vệ

```text
Frontend snapshot readiness
        ↓
Bridge preflight dưới ProcessingMutationGate + fresh snapshot
        ↓
Worker authoritative check + fresh snapshot trước Brand asset I/O
```

- Frontend giúp phản hồi nhanh nhưng không phải authority.
- Bridge preflight ngăn tạo task chắc chắn sẽ fail và cho lỗi đồng bộ dễ hiểu.
- Worker bắt buộc check lại để chống stale UI/snapshot và request gọi trực tiếp.
- Sau khi enqueue, mutation assignment tiếp tục bị chặn bởi `processing_active`, nên assignment không đổi giữa preflight và worker qua UI hỗ trợ.
- Brand folder vẫn có thể bị sửa trực tiếp ngoài app sau validation; giữ cơ chế fingerprint hiện có và ghi rõ không sửa Brand assets khi đang process. Không mở rộng MVP sang filesystem locking.

## 6. Contract backend chi tiết

### 6.1 Process start

Contract mới từ frontend:

```json
{
  "command": "process.start",
  "payload": {
    "bookIds": ["Book 001"],
    "mode": "interior-only"
  }
}
```

Thay đổi dự kiến:

- `IProcessSessionService.StartAsync(bookIds, mode, cancellationToken)` không nhận Brand từ UI.
- `ProcessingSessionWorkerRequest` không chứa authority `BrandName`.
- Worker resolve Brand từ fresh snapshot và giữ Brand đó trong local execution context.
- `ProcessSessionSnapshot.BrandName` được giữ tạm để hiển thị/diagnostic, nhưng chỉ được set từ resolved assignment. Snapshot queued ban đầu có thể để `null`/`Resolving`; worker cập nhật sau khi resolve.
- `ProcessSessionSnapshot.BrandName` không được truyền ngược vào worker và không được dùng để lookup Brand.

Compatibility trong một release:

- Router có thể đọc optional legacy `payload.brandName`.
- Nếu field tồn tại và không trùng resolved assigned Brand: trả `book_brand_mismatch`, không enqueue.
- Nếu trùng: discard field rồi gọi service contract mới.
- Frontend và test mới tuyệt đối không gửi field này.
- Sau khi không còn client cũ, xóa parser compatibility trong một cleanup riêng; không giữ nó vĩnh viễn.

### 6.2 Copy Brand Templates

Contract mới:

```json
{
  "command": "book.brand.templates.copy",
  "payload": {
    "bookId": "Book 001"
  }
}
```

Flow:

1. Vào `ProcessingMutationGate`.
2. Reject nếu processing đang active theo behavior hiện có.
3. Lấy fresh snapshot.
4. Resolve Book và Brand từ assignment.
5. Optional legacy `brandName` chỉ là match assertion rồi discard.
6. Validate Book readiness và Brand certification theo behavior hiện có.
7. Copy templates từ resolved `DiscoveredBrand.Directory`.

Không copy từ Brand đang inspect trên Brands page và không dùng global state.

### 6.3 Stable errors

| Điều kiện | Code | Thông điệp/UX intent |
| --- | --- | --- |
| Book chưa assign | `book_brand_assignment_required` | Assign a Brand to this Book before continuing. |
| Persisted assignment không valid | `book_brand_assignment_invalid` | Giữ assigned name và dùng `AssignmentReason` cụ thể để hướng dẫn repair |
| Request cũ gửi Brand khác assignment | `book_brand_mismatch` | Request Brand does not match the Book's assigned Brand. |
| Batch có nhiều valid assigned Brand | `mixed_assigned_brands_not_supported` | Filter and process one Brand at a time. |
| Book/snapshot không còn tồn tại | Giữ `process_book_not_found` hoặc `book_not_found` theo command hiện có | Refresh Library rồi thử lại |
| Brand asset chưa được validate | Giữ `process_brand_not_validated` cho process | Validate assigned Brand trước khi process |
| Template copy gặp Brand chưa validate | Giữ `brand_not_validated` | Validate assigned Brand trước khi copy |

Không thêm code riêng cho từng invalid status trong MVP. `book_brand_assignment_invalid` đi kèm `AssignmentStatus`, assigned name và `AssignmentReason` đã có để UI hiển thị nguyên nhân cụ thể.

## 7. UI/UX contract

### 7.1 Header và state

- Xóa label/select `Processing Brand` khỏi header.
- Xóa `selectedBrand` như execution state và mọi Book-scoped use của `activeBrand()`.
- Không dùng Book filter đang chọn làm execution authority; filter chỉ thay đổi danh sách hiển thị.
- Giữ `inspectedBrand` trong Brands & Templates để browse, edit Author và validate Brand.

### 7.2 Book-scoped Brand context

Mọi section cần Brand hiển thị context từ Book hiện tại:

```text
Assigned Brand: <name>
```

Các trạng thái:

| State | UI behavior |
| --- | --- |
| Unassigned | Hiển thị “Assign a Brand to continue”, đặt action assign gần cảnh báo; disable Process/Build Final Interior/Copy Templates |
| Invalid assignment | Hiển thị persisted Brand và `AssignmentReason`; action dẫn tới reassign/unassign |
| Valid, Brand chưa validate | Hiển thị assigned Brand; disable action cần certified assets; dẫn tới Brands & Templates |
| Valid và validated | Enable action nếu các readiness khác cũng đạt |
| Missing Brand | Vẫn hiển thị tên Brand đã persist; cho reassign/unassign, không tự xóa |

Automatic Intro:

- Khi `HasIntro == false`, chỉ load và render Intro assets của assigned Brand valid.
- Nếu unassigned/invalid, không đọc preview từ một Brand khác; hiển thị blocked state.
- Khi `HasIntro == true`, custom Intro selection vẫn chỉnh được, nhưng Process vẫn cần valid assignment vì pipeline dùng frame/background của Brand.

### 7.3 Multi-select Process

- Select page vẫn có thể chọn Book bất kỳ; selection không tự thay đổi dữ liệu.
- Trước khi bấm Process, readiness tổng hợp tất cả selected Books.
- Nếu có unassigned/invalid Book: disable hoặc reject action với Book đầu tiên và số Book còn bị block.
- Nếu tất cả valid nhưng thuộc nhiều Brand: hiển thị nhóm theo Brand và hướng dẫn dùng Brand filter để process từng Brand.
- Nếu cùng một Brand: hiển thị `Assigned Brand: X` trong confirmation/queue context; không có dropdown override.
- Backend vẫn recheck kể cả khi UI đã disable.

### 7.4 Refresh sau assignment mà không redraw panel

Sau assign/reassign/unassign:

- Giữ drawer/panel hiện tại, scroll position, focus và tab đang mở.
- Cập nhật targeted sections phụ thuộc Brand: assignment card, Intro template preview/readiness, Production Final Interior readiness, template-copy readiness, Process controls và catalog badges/filter counts.
- Không gọi full Book panel redraw chỉ để cập nhật assignment.
- Nếu Book bị filter ra khỏi list sau assignment, xử lý catalog list riêng nhưng không dùng nó làm lý do reset drawer đang mở.

## 8. Kế hoạch triển khai theo phase

### Phase 1 — Core policy và resolver

Mục tiêu: tạo một nguồn logic duy nhất cho assignment execution.

Files dự kiến:

- `src/PrintableBook.Core/Application/Brands/BookBrandExecutionPolicy.cs`
- Thêm resolver/result types nhỏ trong cùng namespace.
- Core unit tests liên quan assignment/policy.

Công việc:

1. Đổi policy: `Unassigned` không còn Allowed.
2. Tạo typed resolution success/failure cho single Book và batch.
3. Preserve request order; không dựa vào order của discovery/dictionary.
4. Resolve canonical `DiscoveredBrand` từ persisted assignment.
5. Chuẩn hóa error code/message theo mục 6.3.

Exit criteria:

- Unit tests cover đủ bảy assignment statuses.
- Valid assignment trả đúng Book, summary và Brand.
- Không có path tự chọn Brand theo Author hoặc Brand đầu tiên.

### Phase 2 — Processing backend không nhận Brand override

Mục tiêu: mọi mode dùng `ProcessingSessionWorker` derive Brand từ assignment.

Files dự kiến:

- `IProcessSessionService.cs`
- `ProcessingSessionWorkerRequest.cs`
- `ProcessingSessionWorker.cs`
- `WebViewBridgeRouter.cs`
- DI registrations nếu resolver là injectable service.

Công việc:

1. Bỏ Brand authority khỏi service/worker request.
2. Thêm bridge fresh preflight dưới mutation gate.
3. Giữ optional legacy bridge assertion trong một release.
4. Worker lấy fresh snapshot và resolve lại trước Brand validation/I/O.
5. Dùng resolved Brand cho frame/background/automatic Intro.
6. Set session `BrandName` chỉ từ resolved context.
7. Reject toàn bộ mixed batch; không start partial work.
8. Giữ nguyên các validation mode, Book readiness, Intro, Production prefix và pipeline calls hiện tại.

Exit criteria:

- `InteriorOnly`, `FullBook`, `ProductionInterior` đều không thể chạy Book unassigned/invalid.
- Same-Brand multi-Book batch vẫn chạy như trước.
- Mixed-Brand batch fail trước pipeline dispatch.
- Không có Brand asset lookup nào dựa trên UI-selected Brand.

### Phase 3 — Template copy theo assignment

Mục tiêu: sửa triệt để nguyên nhân nút Copy Brand Templates phụ thuộc global selector.

Files dự kiến:

- `WebViewBridgeRouter.cs`
- Template copy service/tests hiện có.

Công việc:

1. Contract mới chỉ cần `bookId`.
2. Resolve assignment từ fresh snapshot dưới mutation gate.
3. Legacy `brandName` chỉ match-check; không dùng làm source directory.
4. Giữ Book readiness, Brand validation và copy semantics hiện có.
5. Không tự copy lại khi reassign; user bấm Copy khi cần.

Exit criteria:

- Assign Brand cho Book mới xong có thể copy ngay từ assigned Brand.
- Unassigned/invalid/missing/unvalidated đều fail trước file copy.
- Inspected Brand trên Brands page không ảnh hưởng copy source.

### Phase 4 — Gỡ global selector và chuyển toàn bộ UI readiness

Mục tiêu: UI phản ánh đúng backend contract và không tạo stale/hidden override.

Files dự kiến:

- `src/PrintableBook.Desktop/Frontend/index.html`
- `src/PrintableBook.Desktop/Frontend/js/app.js`
- CSS chỉ khi cần dọn layout sau khi bỏ selector.

Công việc:

1. Xóa header Processing Brand markup và listeners.
2. Xóa execution use của `selectedBrand`/`activeBrand()`.
3. Derive assigned Brand context từ current `BookDesktopSummary`.
4. Cập nhật Intro preview, Process, Build Final Interior và Copy Templates readiness.
5. Gửi payload mới không có `brandName`.
6. Thêm states unassigned/invalid/unvalidated/mixed rõ ràng.
7. Giữ `inspectedBrand` và toàn bộ Brand browse/edit/validate behavior.
8. Thêm targeted rerender sau assignment, không full redraw panel.

Exit criteria:

- Không còn text/control `Processing Brand` trong Book header.
- Không còn Book action nào đọc Brand từ global/filter/inspection state.
- Brand page vẫn browse và validate bình thường.
- Assign/reassign cập nhật ngay mọi dependent control mà không nhảy layout/panel.

### Phase 5 — Regression tests, docs và cleanup

Mục tiêu: khóa contract mới và chứng minh workflow cũ không bị phá ngoài phạm vi đã chốt.

Công việc:

1. Update fixture thành công để có valid assigned Brand.
2. Thêm negative matrix và stale-snapshot tests.
3. Update bridge/layout/frontend contracts.
4. Chạy full test suite liên quan Core, Desktop và Node bridge.
5. Update `docs/user-guide.md` và tài liệu background processing nếu contract request được mô tả.
6. `rg` toàn repo để không còn hướng dẫn “chọn Processing Brand” hoặc legacy-unassigned behavior.

Exit criteria:

- Full relevant suite xanh.
- Docs chỉ mô tả assignment-driven flow.
- Không migration và không thay đổi format state hiện có.

## 9. Test matrix bắt buộc

### 9.1 Core resolver/policy

- Unassigned -> `book_brand_assignment_required`.
- Valid -> resolve đúng exact persisted Brand.
- `BookAuthorMissing` -> invalid.
- `BrandAuthorMissing` -> invalid.
- `AuthorMismatch` -> invalid.
- `MissingBrand` -> invalid, giữ assigned name trong diagnostic.
- `BrandMetadataUnavailable` -> invalid.
- Missing Book hoặc missing summary -> deterministic not-found failure.
- Resolver không suy Brand từ Author dù có một Brand cùng Author.

### 9.2 Processing worker/service

- `InteriorOnly`: unassigned reject; valid success.
- `FullBook`: unassigned reject; valid success.
- `ProductionInterior`: unassigned reject; valid success; vẫn enforce single Book.
- Hai Book cùng valid Brand -> success.
- Assigned + unassigned -> fail Book unassigned trước, không dispatch.
- Hai valid Brand khác nhau -> `mixed_assigned_brands_not_supported`.
- Assignment đổi/mất Brand sau UI snapshot nhưng trước worker -> fresh check fail.
- Valid assignment nhưng Brand certification stale/invalid -> `process_brand_not_validated`.
- Session snapshot chỉ hiển thị Brand do resolver trả về.
- Failure queue đánh dấu đúng Book theo request order.

### 9.3 Bridge compatibility

- Request mới `process.start` không có `brandName` -> accepted khi assignment valid.
- Legacy matching `brandName` -> accepted nhưng không được dùng làm authority.
- Legacy mismatching `brandName` -> `book_brand_mismatch`, không enqueue.
- Copy payload `{ bookId }` -> copy từ assigned Brand.
- Copy legacy matching/mismatching tương tự process.
- Direct bridge calls không bypass assignment guard.
- Assignment mutation trong processing active vẫn trả `processing_active`.

### 9.4 Frontend/contract

- Header không còn Processing Brand selector.
- `process.start` payload chỉ có `bookIds` và `mode`.
- Template copy payload chỉ có `bookId`.
- Automatic Intro lấy đúng assigned Brand.
- Unassigned/invalid không render preview từ Brand khác.
- Unassigned/invalid/unvalidated disable đúng action và có recovery message.
- Same-Brand multi-select enabled; mixed selection blocked với grouped feedback.
- Brand filter chỉ lọc danh sách, không thay execution context.
- `inspectedBrand` vẫn điều khiển Brands page validation.
- Reassign cập nhật Intro/Production/copy/Process mà không full redraw drawer.

### 9.5 Compatibility/regression

- State JSON cũ thiếu `AssignedBrand` load bình thường thành Unassigned.
- Book metadata, Author, assignment và Brand Author persistence không đổi format.
- Chọn cover, đổi frame mode, save metadata và publish Production assets không làm mất assignment.
- Process Interior behavior sau khi qua guard giữ output/pipeline semantics hiện có.
- Production actions không dùng Brand vẫn chạy được với Book unassigned.
- Brand processing/validation ngoài Book context không bị ảnh hưởng.

## 10. Concurrency và failure handling

- Dùng `ProcessingMutationGate` hiện có cho process start, assignment mutations và template copy; không tạo lock system mới.
- Bridge preflight và worker đều dùng fresh snapshot.
- Khi processing đã queued/running, assignment/reassignment/unassignment phải tiếp tục bị chặn.
- Worker không tạo output/copy/đọc Brand asset trước khi resolve và validate xong toàn batch.
- Nếu một Book fail guard, toàn batch fail; queue không chạy Book còn lại.
- Không rollback output cũ vì guard fail trước side effect mới.
- Standalone Production actions không đọc Brand không bị thêm conflict chỉ vì assignment; tránh mở rộng scope sang scheduler refactor.

## 11. Rollout và commit strategy đề xuất

Triển khai sau khi plan được duyệt bằng các commit nhỏ, mỗi commit build/test được:

1. `feat(core): require valid book brand execution context`
2. `feat(processing): resolve processing brand from book assignment`
3. `fix(templates): copy brand templates from assigned brand`
4. `refactor(ui): remove processing brand override`
5. `test(docs): cover assignment enforcement workflow`

Không trộn refactor unrelated. Không đổi pipeline algorithms. Không tạo database/migration.

## 12. Acceptance criteria end-to-end

```text
Book unassigned
→ UI hiển thị Assign a Brand to continue
→ Process Interior / Build Final Interior / Copy Brand Templates bị chặn
→ User save Author và assign Brand hợp lệ
→ Các Brand-dependent section cập nhật tại chỗ
→ User bấm action mà không chọn Processing Brand toàn cục
→ Backend lấy fresh snapshot và resolve AssignedBrand
→ Brand được validate
→ Action chạy bằng đúng assets của assigned Brand
```

Ngoài ra:

- Không có cách UI/bridge mới để override Book assignment.
- Request cũ gửi Brand khác bị reject, không được override.
- Batch trộn Brand bị reject toàn bộ với hướng dẫn filter/process từng Brand.
- Book legacy vẫn load và edit bình thường; chỉ action cần Brand bị block.
- Workflow và output hiện có giữ nguyên sau khi guard thành công.

## 13. Review disposition

### CEO/product review

- Chốt invariant mạnh: assignment là bắt buộc thay vì duy trì legacy fallback mơ hồ.
- Giữ scope MVP: không auto-assign, không multi-Brand batch, không migration.
- Phân biệt rõ “action cần Brand” với action dùng asset riêng của Book để tránh block quá mức.

### Design review

- Loại bỏ selector gây hiểu nhầm thay vì disable nhưng vẫn để lại.
- Định nghĩa đầy đủ các trạng thái unassigned, invalid, missing, unvalidated và mixed.
- Giữ Brand inspection riêng và yêu cầu targeted rerender để không tái phát panel redraw/layout regression.

### Engineering review

- Direction khả thi mà không refactor pipeline.
- Thêm một resolver nhỏ để tránh duplicate policy ở worker và template copy.
- Fresh preflight + authoritative worker check xử lý stale snapshot; single-Brand batch phù hợp kiến trúc hiện tại.
- Legacy field chỉ là compatibility assertion, không phải authority.

### Developer experience review

- Contract request/error được ghi cụ thể và có removal path cho compatibility parser.
- Test matrix tách Core, worker, bridge, frontend và regression.
- Commit sequence nhỏ giúp review/rollback từng phần và tránh thay đổi unrelated.

## 14. Câu hỏi còn mở

Không còn câu hỏi product bắt buộc phải chốt trước khi triển khai. Các lựa chọn kỹ thuật chính đã được quyết định trong plan này.

Một cleanup không chặn MVP: sau khi xác nhận không còn client cũ, xóa optional legacy `brandName` parser ở bridge trong release kế tiếp.
