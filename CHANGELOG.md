# Changelog

## Unreleased

### Changed

- Normal Interior chỉ còn **Frame** và **No Frame**; Book/page mới mặc định No Frame. State legacy Auto hoặc missing override được đọc thành No Frame và hiển thị cảnh báo review theo Book.
- Frame processing nay fail closed khi Brand frame thiếu, unreadable hoặc sai kích thước. Frame được stage theo Book run và cache stamp v5 dùng content SHA-256 để phát hiện cả thay đổi giữ nguyên length/timestamp.
- Workspace frame-mode state dùng contract v2, canonical writer chỉ lưu explicit Frame override; state hỏng được cô lập theo Book và không bị tự ghi đè.

### Compatibility

- PDF đã publish không tự đổi; output chỉ thay đổi khi user process/publish lại. Workspace đã được save bằng frame-mode contract v2 không hỗ trợ downgrade về binary cũ hiểu missing mode là Auto.

## 0.1.1

### Added

- Brand validation certificate với fast metadata fingerprint, deep Validate và processing gate trước khi xử lý.
- Trạng thái Brand validation cùng thao tác **Validate Brand** trong ứng dụng desktop.
- Script local cài Brand/Book mẫu vào artifact đã publish để smoke test, không đưa sample vào Debug hoặc ZIP release mặc định.
- Production Assets workflow: native PNG import, independent Cover/prefix processing, `17.47 × 8.75 inch` Cover PDF và Production Final Interior với provenance trong PDF Library.
- Brand template contract thêm `book_owner.psd`; release phải bổ sung file này cho mọi Brand và validate lại trước khi triển khai build mới.

### Changed

- Gói Windows x64 portable dùng framework-dependent single-file executable; WebView2 profile được lưu trong `%LOCALAPPDATA%\PrintableBook\WebView2`.
- **No frame** nay ép Interior qua CropArt (trim trắng, pad vuông trắng, resize chuẩn), bỏ qua detector và không áp Brand frame; các lựa chọn `disabled` đã lưu nhận semantics mới khi process lại.
- Cache Interior nâng lên stamp v4/classification metadata v2 với provenance detected/forced, migration chọn lọc từ v3 và atomic commit để retry an toàn.

## 0.1.0

### Added

- Local Book discovery, Brand assets và Books workspace.
- Interior settings, AUTO/CUSTOM Intro, Active/Inactive và Frame modes.
- PDF Library, Clear Cache, Diagnostics và background task visibility.

### Processing

- Canonical normalized image source cho toàn bộ Interior pipeline.
- BorderLine V3 với BorderPixel V1 fallback.
- Artwork preparation, optional frame, deterministic shuffle, Brand Background và PDF assembly.
- Bounded per-Book page concurrency và cooperative cancellation.

### Desktop

- WPF/WebView2 local desktop shell với JSON bridge v1.
- Printable Book application identity và Version 0.1.

### Documentation

- Kiến trúc v0.1, user guide, release notes và release checklist bằng tiếng Việt.
