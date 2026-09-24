# Background Process Session

> Xem kiến trúc tổng thể tại [architecture.md](architecture.md) và task/lane policy tại [background-application-loading.md](background-application-loading.md).

## Ownership hiện hành

`BackgroundTaskManager` trong Desktop là scheduling và task boundary duy nhất cho Interior Processing. `IProcessSessionService` là facade semantic cho bridge: nó tạo/lấy/hủy `ProcessingSession` task, còn `ProcessingSessionWorker` mới chạy snapshot validation và queue orchestration. WPF/WebView chỉ gửi command và đọc snapshot; polling UI không tạo hay sở hữu worker thread.

```text
WebView process.start
  → IProcessSessionService
  → IBackgroundTaskManager.StartAsync(ProcessingSession)
  → ProcessingSessionWorker
  → IPrintableBookApplication.ProcessBooksAsync
  → terminal task + observable ProcessSessionSnapshot
```

Manager áp dụng duplicate/conflict policy và lane limit. Chỉ một ProcessingSession active; Books lần lượt chạy trong session, còn bounded page concurrency chỉ nằm bên trong Book đang xử lý. `process.get` là snapshot read. `process.cancel` chỉ gửi cooperative cancellation request, trả `Cancelling` mà không đợi image/PDF worker hoàn tất.

`LibraryRefresh` có thể đồng thời chạy với Processing theo policy hiện hành. `CacheCleanup` conflict với cả hai để không xoá workspace trong lúc đọc/ghi.

Một `ProcessingSession` luôn mang theo `BookProcessingMode` trong request và snapshot:

- `InteriorOnly` (**Process Interior**) xử lý Intro + active Interior, validate assembly và lưu processed-page previews; không export/publish PDF.
- `ProductionInterior` (**Build Final Interior**) tự xử lý hai Production prefix cùng current Intro/Interior rồi export và publish Final Interior PDF.

Vì mode nằm trong snapshot, refresh hoặc chuyển route giữa phiên vẫn khôi phục đúng stage, busy label và terminal message. Một phiên pages-only không được hiển thị `PDF export` hay làm nút **Build Final Interior** thành `Building…`; nút chỉ disabled kèm lý do conflict.

## Snapshot và shutdown

Worker cập nhật mode, queue, Book hiện tại, step, page progress và worker count vào task view. Những snapshot đó được bridge trả về cho Process page, taskbar status và Diagnostics; frontend không suy luận trạng thái processing từ polling riêng. Nếu `InteriorOnly` fail/cancel sau khi page work bắt đầu, worker xóa preview manifest có thể đã thay đổi nhưng giữ nguyên PDF/provenance hiện có.

Khi đóng cửa sổ, `ProcessWindowShutdownCoordinator` dùng graceful-stop bounded timeout. Hết thời hạn, user mới được chọn tiếp tục chờ hoặc force exit; dispatcher không bị block. Windows session ending dùng cùng best-effort stop không hiện UI. Sau restart, interrupted recovery chỉ chuyển workspace stale `Running` thành terminal `Interrupted`, giữ nguyên Completed/Failed/Cancelled và metadata có thể retry.

## Verification

Core tests kiểm tra task chạy ngoài caller context, cancellation observable trước worker unwind, timeout bounded và cleanup terminal cho phép session tiếp theo. Desktop tests kiểm tra close coordinator. Bridge/frontend tests kiểm tra poll global, cancel control và observable task/session snapshot.
