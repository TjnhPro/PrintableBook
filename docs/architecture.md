# Kiến trúc Printable Book v0.1

## Mục tiêu và ranh giới

Printable Book là ứng dụng Windows local-first để kiểm tra, chuẩn bị ảnh Coloring Book và xuất PDF. v0.1 là ứng dụng portable: dữ liệu runtime được quản lý cạnh file thực thi, không dùng service từ xa và không có installer, auto-update hay data-root migration.

```text
<AppRoot>/
├─ PrintableBook.exe
├─ Frontend/
├─ brands/
├─ sources/
└─ settings.json
```

`AppRoot = AppDomain.CurrentDomain.BaseDirectory`. Vì `brands/`, `sources/`, `settings.json` và `.workspace/` của từng Book đều cần ghi được, ứng dụng phải được giải nén vào thư mục writable (ví dụ `C:\PrintableBook\`), không phải `Program Files`.

## Module và hướng phụ thuộc

```text
PrintableBook.Desktop
├─ WPF window / lifecycle
├─ WebView2 host
├─ JSON bridge v1
└─ BackgroundTaskManager
        │
        ▼
PrintableBook.Core
├─ discovery contracts
├─ application snapshots
├─ Book state/workspace model
├─ processing orchestration
├─ classification contracts
└─ ports
        │
        ▼
PrintableBook.Infrastructure
├─ physical filesystem
├─ Magick.NET image adapters
├─ disk-backed page pipeline/cache
├─ PDFsharp export/inspection
└─ workspace/output persistence
```

`PrintableBook.Core` giữ domain model, use case, orchestration và các port trung lập. `PrintableBook.Infrastructure` triển khai filesystem, Magick.NET, PDFsharp và persistence. `PrintableBook.Desktop` là composition root WPF/WebView2; frontend chỉ render snapshot và gửi command qua JSON bridge v1, không nắm business logic hay raster processing.

Các test kiến trúc bảo vệ hướng phụ thuộc: Core không tham chiếu Infrastructure/Desktop/WPF/WebView2/Windows APIs; Infrastructure không tham chiếu Desktop.

## Discovery, Book và workspace

Library discovery quét `sources/` thành Book, assets và validation snapshot. Mỗi Book có workspace riêng dưới `.workspace/`, gồm state, log, cache, processed preview và output tạm. Trạng thái Book lưu các lựa chọn ổn định theo key tương đối của ảnh Interior, không theo index hiển thị, nên refresh hay đổi thứ tự file không làm mất lựa chọn.

Các output đã publish thuộc `Output/` của Book. PDF Library đọc output đã publish; không đọc trực tiếp cache tạm.

## Luồng xử lý ảnh chuẩn hoá

Luồng production dùng một nguồn chuẩn duy nhất cho từng trang Interior:

```text
RAW Interior source
        ↓
artwork-source-normalization-v1
        ↓
.workspace/cache/<page>/normalized-source.png
default 2048×2048
        ↓
classification
├─ BorderLine V3
└─ BorderPixel V1 fallback
        ↓
Artwork Preparation V1
        ↓
Prepared 2270×2270
        ↓
optional frame
        ↓
Working 2550×2550
        ↓
Final 2588×2625
        ↓
shuffle / assembly
        ↓
Interior PDF
```

Raw source chỉ được dùng để xác định/fingerprint và chuẩn hoá. Sau khi `normalized-source.png` đã có, detector, classifier, preparation, frame và page production đều đọc artifact này; không có stage sau đó mở lại raw source. Normalization tạo PNG vuông opaque-white trong cùng toạ độ chuẩn (mặc định `2048×2048`).

`BorderLine V3` là detector hiện hành: pass 1 tìm viền nông (`200`), pass 2 sâu hơn (`320`) chỉ chạy khi pass 1 không có outer frame bốn cạnh coherent. Hai pass dùng cùng quality gates; `BorderPixel V1` chỉ là fallback khi BorderLine âm. Tuning BorderLine, version classification hoặc normalization làm invalid stage cache và các stage downstream tương ứng.

Preparation kết thúc tại raster `PreparedArtwork`. Từ đó các stage downstream không cần biết loại artwork. `FrameMode.Auto` dùng recommendation từ classification, `Enabled` buộc frame nếu Brand có frame tương thích, còn `Disabled` không dùng frame:

```text
ShouldApplyFrame = FrameAvailable &&
  (Auto => AutoFrameRecommended, Enabled => true, Disabled => false)
```

## Intro, Active và Frame

Identity của các trang Book Interior được gán ổn định trước khi lọc.

```text
HasIntro=false
→ AUTO
→ current Brand/IntroTemplate
→ all eligible images
→ filename ASC

HasIntro=true
→ CUSTOM
→ ordered Book interior selection
→ selected pages removed from normal Interior
→ no shuffle for Intro
```

Intro dùng cùng canonical normalization nhưng luôn forced `CropArt`, không chạy BorderLine/BorderPixel và không frame. Với CUSTOM, các trang Book Interior đã chọn bị tách khỏi tập Interior bình thường trước khi kiểm tra Active; vì vậy không xuất hiện hai lần và không đi vào `InteriorShuffleMap`. Các giá trị Active/Frame đã lưu của chúng được giữ nguyên nhưng bị bỏ qua khi đang là Intro.

Các trang Interior bình thường inactive bị loại trước image processing và shuffle. Sau khi xử lý, chỉ Interior bình thường active được deterministic shuffle. Intro giữ thứ tự đã chọn/filename.

## Assembly với Brand background

`background.png` của Brand là một trang final-size độc lập, không phải overlay. Nó phải đúng Final Page size, không classification, không vào page cache và không shuffle. Khi background bật, assembly xen sau từng Intro và từng Interior đã shuffle:

```text
intro1
background
intro2
background
interior-shuffled-1
background
interior-shuffled-2
background
```

## Thực thi nền và concurrency

`BackgroundTaskManager` của Desktop là scheduling/task boundary. Nó áp policy lane, duplicate/conflict và lifecycle cho `ProcessingSessionWorker`; UI hay WebView polling không sở hữu worker thread. Worker tạo snapshot queue, kiểm tra Brand/Intro/background, sau đó gọi orchestration của Core.

- Mỗi lần chỉ có một processing session active.
- Books chạy tuần tự trong session.
- Chỉ các trang của Book hiện tại chạy bounded concurrency, cấu hình hợp lệ `1..12`.
- Không có nested parallelism.
- Library Refresh có thể overlap processing theo policy của manager.
- Cancellation là cooperative: command cancel chuyển task sang `Cancelling`, worker quan sát `CancellationToken` và trạng thái terminal được publish khi unwind hoàn tất.

Snapshot session/worker vẫn observable qua bridge để WebView hiển thị Process và taskbar status. Khi đóng ứng dụng, Desktop dùng graceful-stop có thời hạn; startup recovery chỉ chuyển workspace stale `Running` thành `Interrupted`, không thay đổi Completed/Failed/Cancelled.

## Cache, output và Clear Cache

Page pipeline có cache stage-aware. `classification.json`, canonical source và raster stage có input stamp/version/fingerprint; thay đổi ở stage nào thì chỉ invalid stage đó và downstream. FrameMode-only có thể tái sử dụng classification/prepared, còn thay normalization hay BorderLine settings bắt đầu invalid sớm hơn.

Clear Cache xóa raster nặng (canonical/processed cache) nhưng giữ Book state và metadata classification theo hành vi production hiện tại. PDF đã publish không thuộc cache nên vẫn tồn tại; lần process sau dựng lại cache cần thiết.

## Kiểm thử

CI chỉ chạy fixture repository-owned, deterministic và redistributable. Corpus ảnh thật do user cung cấp nằm trong `TestResults/`, được đánh dấu `TestScope=LocalCorpus`, chỉ chạy explicit local opt-in và không được yêu cầu trên clean checkout/CI. Real-output certification phải kiểm tra file/raster/PDF thật thay vì chỉ mock.

Tài liệu liên quan: [processing background](background-process-session.md), [BorderLine V3](borderline-detector-v3.md), [shared Interior pipeline](interior-shared-pipeline-integration.md), [Intro](intro-template-processing.md) và [PDF engine](pdf-engine.md).
