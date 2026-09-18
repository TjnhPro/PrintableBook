# Changelog

## 0.1.1

### Added

- Brand validation certificate với fast metadata fingerprint, deep Validate và processing gate trước khi xử lý.
- Trạng thái Brand validation cùng thao tác **Validate Brand** trong ứng dụng desktop.
- Script local cài Brand/Book mẫu vào artifact đã publish để smoke test, không đưa sample vào Debug hoặc ZIP release mặc định.

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
