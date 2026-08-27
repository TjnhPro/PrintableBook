# Printable Book v0.1.0

## Thông tin release

- Ngày release: 2026-08-27
- Platform: Windows x64
- Package: portable ZIP, self-contained, multi-file

## Tính năng chính

- Local Book discovery và Brand assets.
- Canonical image normalization.
- BorderLine V3 và BorderPixel V1 fallback.
- Active/Inactive và Frame modes.
- Intro AUTO/CUSTOM cùng Brand Background.
- Bounded Interior processing, deterministic shuffle và cooperative cancellation.
- PDF Library, Clear Cache và Diagnostics.

## Cài đặt

1. Download file ZIP của `v0.1.0`.
2. Giải nén vào thư mục writable, ví dụ `C:\PrintableBook\`.
3. Chạy `PrintableBook.exe`.
4. Thêm thư mục Brand vào `brands/` và Book vào `sources/`.
5. Trong app chọn Brand và nhấn **Refresh**.

Không chạy bản portable chính từ `Program Files`: app cần ghi `brands/`, `sources/`, `settings.json` và workspace cạnh executable.

## Known limitations

- Chỉ Windows x64.
- Binary chưa code-signed, Windows có thể hiện cảnh báo xác nhận.
- Mô hình portable yêu cầu thư mục writable.
- Không có installer.
- Không có auto update.
