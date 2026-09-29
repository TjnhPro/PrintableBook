# Keyword Builder — Generic Keywords và Book Keywords

## Trạng thái

- Loại tài liệu: implementation plan
- Phạm vi hiện tại: plan và review, chưa triển khai code
- Hướng thay đổi: additive, giữ tương thích với Keyword Builder hiện có

## 1. Mục tiêu

Điều chỉnh Keyword Builder để hỗ trợ hai nguồn keyword:

1. **Generic Keywords**: danh sách keyword dùng chung cho mọi Book, chỉ cần cấu hình một lần.
2. **Book Keywords**: danh sách keyword riêng của từng Book.

Khi Build:

- Generic Keywords luôn được ưu tiên trước Book Keywords.
- Book phrase trùng Generic phrase bị loại trước khi xử lý.
- `keyword_1` đến `keyword_7` được build theo thứ tự từ trên xuống và dừng khi đủ bảy field.
- Ads Keyword có tối đa 30 phrase, ưu tiên tỷ lệ 20 Generic và 10 Book nhưng cho phép bên còn lại bù quota bị thiếu.
- Copy tất cả output thành một dòng có chín field, phân cách bằng tab.

## 2. Các quyết định đã chốt

| Chủ đề | Quyết định |
|---|---|
| Sort output | Sort được hiểu là random shuffle cho từng keyword slot, Ads Keyword và Ads ASIN, không phải alphabetical sort. |
| Duplicate phrase | So sánh toàn phrase sau normalize, không phân biệt hoa/thường. |
| Thứ tự ưu tiên | Generic trước, Book sau khi chọn input/quota; output được shuffle sau khi selection và packing hoàn tất. |
| Đủ bảy field | Dừng nhận token mới; loại phần còn dư thay vì báo lỗi hoặc tạo field thứ tám. |
| Dữ liệu bị loại | Hiển thị warning không blocking; Build & Save vẫn thành công. |
| Ads ASIN | Input một dòng, tách target bằng dấu phẩy (và chấp nhận newline từ state cũ), trim, shuffle rồi join lại bằng dấu phẩy khi Build & Save. |
| Generic/Book input | Textarea multiline có viewport cố định 5 dòng, không resize; nội dung dài hơn dùng scroll. |
| Clipboard | Copy chín giá trị không kèm label, phân cách bằng `\t`. |

## 3. Phạm vi MVP

### Trong phạm vi

- Lưu Generic Keywords trong Global Settings.
- Đổi tên Source Keywords trên UI thành Book Keywords.
- Lọc Book phrase trùng Generic phrase.
- Build lại bảy backend keyword field theo thứ tự ưu tiên mới.
- Giới hạn output ở đúng bảy field và bỏ phần dư.
- Build Ads Keyword theo quota linh hoạt tối đa 30 phrase.
- Đổi tên nút Copy và thay format clipboard.
- Điều chỉnh Ads ASIN thành input một dòng.
- Bổ sung validation, warning, compatibility và automated tests.

### Không nằm trong phạm vi

- Không tạo Generic Keyword database hoặc entity riêng.
- Không tạo nhiều template Generic Keywords.
- Không hỗ trợ Generic Keywords theo Brand.
- Không tự sinh keyword bằng AI.
- Không thêm bulk build cho nhiều Book.
- Không thay đổi Book Information, Brand Assignment hoặc production workflow.
- Không migrate hoặc rename hàng loạt state JSON hiện có nếu không cần thiết.

## 4. Data contract và persistence

### 4.1 Generic Keywords

Mở rộng `GlobalSettings` bằng một collection optional:

```text
genericKeywords: string[]
```

Quy tắc:

- Lưu trong `settings.json` bằng `IGlobalSettingsStore` hiện có.
- File settings cũ không có property này được hiểu là array rỗng.
- Normalize trước khi Save: trim, gom whitespace và loại dòng rỗng.
- Không giới hạn số phần tử chỉ vì textarea hiển thị năm dòng; textarea được scroll khi có nhiều hơn năm dòng.
- Save Configuration phải giữ nguyên toàn bộ settings khác.

### 4.2 Book Keywords

Keyword Builder state hiện tại đã lưu `sourceKeywords`. Để tránh migration:

- Giữ JSON property `sourceKeywords` trong MVP.
- Trên UI và tài liệu, property này được gọi là **Book Keywords**.
- State cũ load lên sẽ hiển thị `sourceKeywords` trong input Book Keywords.
- Build mới vẫn lưu toàn bộ Book Keywords đã normalize, kể cả phần không vào được bảy generated fields.

### 4.3 Build audit

Mỗi build nên lưu thêm snapshot Generic Keywords thực sự được dùng:

```text
genericKeywords: string[]
sourceKeywords: string[]   // Book Keywords, giữ tên cũ để tương thích
algorithmVersion: 2
```

Snapshot giúp output đã lưu có thể được giải thích ngay cả khi user sửa Global Settings sau đó.

Nếu muốn giữ schema tối thiểu tuyệt đối, snapshot Generic có thể bỏ khỏi persistence; tuy nhiên plan mặc định lưu snapshot vì chi phí nhỏ và tránh mất khả năng truy vết build.

## 5. Normalize và duplicate contract

Áp dụng riêng cho từng phrase của cả hai nguồn:

1. Trim khoảng trắng đầu/cuối.
2. Gom nhiều whitespace liên tiếp thành một ASCII space.
3. Loại phrase rỗng.
4. Giữ nguyên text normalized đầu tiên để hiển thị và tạo Ads Keyword.
5. Dùng so sánh case-insensitive khi kiểm tra duplicate.

Thứ tự lọc:

```text
normalize Generic
→ unique duplicate phrase bên trong Generic, first occurrence wins
→ normalize Book
→ unique duplicate phrase bên trong Book, first occurrence wins
→ loại Book phrase đã xuất hiện trong Generic
```

Ví dụ:

```text
Generic:
coloring books
books for adults

Book:
Coloring   Books
cute animals

Effective Book Keywords:
cute animals
```

Duplicate phrase và duplicate word là hai bước khác nhau:

- Phrase dedupe quyết định phrase nào được tham gia build và Ads Keyword.
- Word dedupe áp dụng cho stream tạo `keyword_1` đến `keyword_7`.

## 6. Thuật toán `keyword_1` đến `keyword_7`

### 6.1 Tạo token stream

```text
effective phrases = normalized Generic + effective Book
→ split từng phrase bằng whitespace
→ ordered unique word, case-insensitive, first occurrence wins
```

Generic luôn đứng trước Book nên một word đã xuất hiện trong Generic sẽ không được lấy lại từ Book.

### 6.2 Pack field

- Có đúng bảy slot: `keyword_1` đến `keyword_7`.
- Mỗi slot tối đa 50 ký tự.
- Các word trong slot được phân cách bằng một ASCII space.
- Không cắt một word để lấp đầy slot.
- Nếu word tiếp theo không vừa slot hiện tại, chuyển nguyên word sang slot tiếp theo.
- Nếu một word riêng lẻ vượt 50 ký tự, giữ validation `keyword_word_too_long` và chặn Save.
- Khi slot thứ bảy không còn nhận được word tiếp theo, dừng stream và bỏ toàn bộ word còn lại.
- Không còn trả lỗi `keyword_capacity_exceeded` cho trường hợp vượt sức chứa bảy field.

### 6.3 Shuffle

- Pack hoàn tất trước, sau đó shuffle word trong từng slot đúng một lần.
- Không đổi word giữa các slot.
- Không shuffle lại khi refresh, mở lại Book Detail hoặc Copy.
- Persist chính xác output sau shuffle.

### 6.4 Overflow warning

Nếu có word bị bỏ vì đã đủ bảy slot:

```text
7 keyword fields are full. Some remaining keywords were not included.
```

Warning này:

- không chặn Save;
- xuất hiện cùng kết quả build;
- không biến Build thành trạng thái lỗi;
- nên kèm số word hoặc phrase bị bỏ nếu có thể tính chính xác mà không làm phức tạp MVP.

Nếu riêng Generic Keywords đã chiếm hết bảy slot, Generic còn dư và toàn bộ Book Keywords sau đó đều bị bỏ khỏi generated fields.

## 7. Thuật toán Ads Keyword

Ads Keyword sử dụng phrase, không sử dụng shuffled word output.

### 7.1 Quota

- Tối đa 30 phrase.
- Quota ưu tiên ban đầu: tối đa 20 Generic và tối đa 10 Book.
- Nếu Generic thiếu 20, Book được bù phần còn thiếu đến khi tổng đạt 30 hoặc hết Book.
- Nếu Book thiếu 10, Generic được bù phần còn thiếu đến khi tổng đạt 30 hoặc hết Generic.
- Chọn toàn bộ Generic quota trước Book quota, trong từng nguồn theo thứ tự từ trên xuống.
- Sau khi chọn đủ quota, shuffle toàn bộ phrase đúng một lần trước khi persist.
- Ads Keyword empty hoặc chỉ có một phrase không gọi random.

Ví dụ:

| Generic khả dụng | Book khả dụng | Ads Keyword được chọn |
|---:|---:|---|
| 25 | 20 | 20 Generic + 10 Book |
| 15 | 30 | 15 Generic + 15 Book |
| 30 | 5 | 25 Generic + 5 Book |
| 8 | 6 | 8 Generic + 6 Book |

### 7.2 Format

Các phrase được nối bằng:

```text
", "
```

Book phrase trùng Generic đã bị loại trước khi tính quota.

Ads Keyword được build độc lập với giới hạn bảy field. Vì vậy một phrase có word không được đưa vào `keyword_1…keyword_7` do hết chỗ vẫn có thể xuất hiện trong Ads Keyword nếu còn quota.

## 8. Ads ASIN contract

- Dùng `<input type="text">`, không dùng multiline textarea.
- Không đặt business `maxlength` trong MVP.
- Outer trim, split bằng dấu phẩy/newline, bỏ phần tử rỗng và shuffle đúng một lần khi có từ hai target.
- Join output đã shuffle bằng dấu phẩy; không dedupe target.
- Empty hoặc một target không gọi random.
- Copy đúng giá trị đã Save.
- Book Information ASIN và Keyword Builder Ads ASIN tiếp tục là hai field khác nhau.

## 9. UI contract

### 9.1 Configuration

Thêm section:

```text
Keyword Builder

Generic Keywords
[ multiline input — viewport cố định 5 dòng ]

[ Save ]
```

Yêu cầu UI:

- `rows="5"` hoặc CSS height tương đương năm dòng.
- `resize: none`.
- `overflow-y: auto`.
- Helper text: mỗi dòng là một keyword phrase dùng chung cho mọi Book.
- Save dùng settings mechanism hiện có.
- Save success/error không làm mất draft khi request lỗi.

### 9.2 Book Detail

Điều chỉnh card Keyword Builder:

```text
Book Keywords
[ multiline input — viewport cố định 5 dòng ]

Ads ASIN
[ single-line input ]

[ Build & Save ]
```

Yêu cầu:

- Đổi label `Source Keywords` thành `Book Keywords`.
- Textarea Book Keywords không resize và có scroll.
- Hiển thị số Generic Keywords hiện đang được dùng, ví dụ `Using 12 saved Generic Keywords`.
- Generated fields và Ads Keyword tiếp tục read-only.
- Không redraw toàn Book Detail sau Save.
- Draft Book Keywords không bị mất khi Build lỗi.

### 9.3 Copy button

Đổi:

```text
Copy all in field order
```

thành:

```text
Copy to Clipboard
```

Button chỉ copy build đã Save, không copy draft chưa Build.

## 10. Clipboard contract

Copy đúng chín giá trị theo thứ tự:

1. `keyword_1`
2. `keyword_2`
3. `keyword_3`
4. `keyword_4`
5. `keyword_5`
6. `keyword_6`
7. `keyword_7`
8. `Ads Keyword`
9. `Ads ASIN`

Format:

```text
keyword_1\tkeyword_2\tkeyword_3\tkeyword_4\tkeyword_5\tkeyword_6\tkeyword_7\tAds Keyword\tAds ASIN
```

Quy tắc:

- Không kèm label.
- Không thêm header.
- Không thêm newline giữa các field.
- Field rỗng vẫn tạo vị trí tab tương ứng.
- Ads ASIN được copy đúng giá trị canonical đã shuffle và Save.
- Clipboard success/failure phải có feedback rõ ràng.

## 11. Bridge và application flow

Flow đề xuất:

```text
App snapshot
→ chứa GlobalSettings.genericKeywords
→ Book Detail render Generic count + Book Keyword draft
→ user nhấn Build & Save
→ frontend gửi bookId + Book Keywords + Ads ASIN
→ backend load Generic Keywords từ settings hiện hành
→ normalize/dedupe/build
→ save Book Keyword Builder state
→ response trả saved state + overflow warning
→ patch riêng card Keyword Builder
```

Backend phải load Generic Keywords từ source đáng tin cậy tại thời điểm Build, không nhận Generic Keywords tùy ý từ Book Detail payload. Điều này đảm bảo output dùng đúng template đã Save.

Bridge payload hiện tại có thể tiếp tục dùng property `keywords` để giảm migration, nhưng contract/documentation phải xác định đây là Book Keywords.

## 12. Error và warning contract

### Blocking errors

- Payload Book Keywords không phải string array.
- Ads ASIN không phải string hoặc null.
- Một word riêng lẻ vượt 50 ký tự.
- Không đọc hoặc lưu được settings/state.

### Non-blocking warnings

- Generated stream vượt sức chứa bảy field.
- Một số Generic hoặc Book word không xuất hiện trong generated fields do đã đầy.

Không dùng `keyword_capacity_exceeded` như blocking error nữa. Có thể giữ mapping legacy trong UI một thời gian để xử lý response từ binary cũ.

## 13. Kế hoạch triển khai theo phase

### Phase 1 — Domain và settings contract

- Mở rộng `GlobalSettings` với Generic Keywords optional/default empty.
- Cập nhật normalize và JSON settings compatibility.
- Mở rộng builder input để nhận Generic và Book Keywords riêng.
- Thêm result metadata cho overflow warning và Generic snapshot.
- Tăng algorithm version.

### Phase 2 — Builder algorithm

- Implement phrase normalize/dedupe giữa hai nguồn.
- Tạo ordered unique word stream theo Generic trước, Book sau.
- Đổi capacity behavior từ throw sang truncate có warning.
- Giữ validation word vượt 50 ký tự.
- Build Ads Keyword theo quota 20/10 có bù rồi shuffle phrase đã chọn.
- Shuffle từng packed slot và danh sách Ads ASIN có dữ liệu đúng một lần.

### Phase 3 — Persistence và bridge

- Save/load Generic Keywords bằng settings store hiện tại.
- Build dùng Generic Keywords đã Save tại thời điểm request.
- Persist Book input, Generic snapshot, outputs và audit fields.
- Trả overflow warning trong response.
- Bảo đảm save settings không ghi mất các processing settings hiện tại.

### Phase 4 — UI

- Thêm Generic Keywords vào Configuration.
- Đổi Source Keywords thành Book Keywords.
- Khóa hai textarea keyword ở viewport năm dòng và `resize: none`.
- Đổi Ads ASIN thành input một dòng.
- Hiển thị Generic count và overflow warning.
- Đổi tên Copy button.
- Đổi clipboard sang tab-separated values không label.

### Phase 5 — Compatibility và documentation

- Load được settings cũ thiếu Generic Keywords.
- Load được Book Keyword Builder state version 1.
- Giữ `sourceKeywords` làm JSON alias cho Book Keywords.
- Cập nhật user guide và troubleshooting.
- Ghi rõ build cũ không tự rebuild khi Generic Settings thay đổi.

### Phase 6 — Verification

- Chạy Core tests cho normalize, dedupe, pack, truncate, shuffle và Ads quota.
- Chạy Infrastructure tests cho settings/state JSON round-trip.
- Chạy Desktop bridge tests cho payload/response/error/warning.
- Chạy frontend tests cho fixed textarea, input Ads ASIN, no-redraw và clipboard.
- Chạy full solution tests và UI contract checks hiện có.

## 14. Test matrix bắt buộc

### Domain

- Generic được xử lý trước Book.
- Phrase duplicate khác case/whitespace bị loại.
- Duplicate bên trong từng nguồn dùng first occurrence.
- Word duplicate trên toàn stream chỉ xuất hiện một lần.
- Word đúng 50 ký tự hợp lệ; word 51 ký tự bị chặn.
- Word không vừa slot hiện tại chuyển nguyên sang slot kế tiếp.
- Đầy slot thứ bảy thì bỏ phần dư và trả warning.
- Generic một mình đầy bảy slot thì Book không vào generated fields.
- Shuffle chỉ chạy sau pack và đúng một lần cho mỗi slot có thể shuffle.

### Ads Keyword

- `20 Generic + 10 Book` khi cả hai đủ quota.
- `15 Generic + 15 Book` khi Generic thiếu.
- `25 Generic + 5 Book` khi Book thiếu.
- Tổng dưới 30 khi cả hai đều thiếu.
- Book duplicate với Generic không chiếm quota.
- Output giữ Generic trước Book và giữ thứ tự trong từng nguồn.
- Ads output không phụ thuộc generated-field overflow.

### Persistence và compatibility

- Settings cũ thiếu `genericKeywords` load thành empty.
- Save/load Generic Keywords round-trip.
- Save Configuration giữ các settings hiện tại.
- State version 1 chỉ có `sourceKeywords` vẫn load.
- Build version 2 round-trip cả Generic snapshot và Book input.
- Metadata/Brand/Interior state không bị ghi đè khi save Keyword Builder.

### UI và clipboard

- Generic và Book textarea đều cố định năm dòng, không resize.
- Nội dung hơn năm dòng có thể scroll và không bị mất.
- Ads ASIN render là input một dòng.
- Copy button có label `Copy to Clipboard`.
- Clipboard có đúng chín field và tám tab.
- Empty middle field vẫn giữ đúng vị trí cột.
- Không có labels trong clipboard.
- Copy lấy saved build, không lấy draft.
- Build error giữ nguyên draft.
- Build success không redraw toàn Book Detail.

## 15. Acceptance criteria

1. User có thể Save Generic Keywords một lần trong Configuration và dùng lại cho mọi lần Build.
2. Mỗi Book vẫn có Book Keywords riêng và load được dữ liệu Keyword Builder cũ.
3. Book phrase trùng Generic phrase bị loại sau normalize, không phân biệt hoa/thường.
4. Generated fields ưu tiên Generic rồi Book theo thứ tự từ trên xuống.
5. Output không vượt bảy field hoặc 50 ký tự mỗi field và không cắt word.
6. Khi đầy bảy field, phần dư bị bỏ với warning không blocking.
7. Ads Keyword tối đa 30 phrase với quota 20/10 và cơ chế bù phần thiếu.
8. Generic và Book input là textarea cố định năm dòng, không kéo resize.
9. Ads ASIN là input một dòng và không bị biến đổi khi Copy.
10. `Copy to Clipboard` tạo đúng chín giá trị tab-separated, sẵn sàng paste vào spreadsheet.
11. Các workflow Book Information, Brand Assignment, Interior và Production không đổi behavior.

## 16. Flow cuối cùng

```text
User cấu hình Generic Keywords một lần
→ mở Book Detail
→ nhập Book Keywords và Ads ASIN
→ nhấn Build & Save
→ app load Generic Keywords đã Save
→ loại Book phrase trùng Generic
→ build keyword_1…keyword_7, Generic trước Book
→ dừng và cảnh báo nếu đủ bảy field
→ build Ads Keyword tối đa 30 phrase theo quota linh hoạt
→ persist build
→ user nhấn Copy to Clipboard
→ paste chín field vào chín cột
```
