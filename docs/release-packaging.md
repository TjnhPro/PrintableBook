# Release packaging

Release hiện tại của Printable Book là gói portable Windows x64 **framework-dependent** trên .NET 10. Lệnh đóng gói chính thức là:

```powershell
scripts/publish-release.ps1 -ExpectedVersion 0.1.1
```

## Mô hình publish

- `PublishSingleFile=true`: managed application và native dependencies (bao gồm Magick) được bundle vào một `PrintableBook.exe`.
- `IncludeNativeLibrariesForSelfExtract=true`: native libraries được extract tự động khi ứng dụng chạy.
- WebView2 Loader được link tĩnh; gói không chứa `WebView2Loader.dll` hay các `Microsoft.Web.WebView2*.dll` ở cạnh EXE.
- Không dùng trimming, ReadyToRun hoặc compression để ưu tiên độ ổn định cho WPF, WebView2 và Magick.
- Không có installer hay code signing ở v0.1.

Thư mục publish chỉ có contract sau:

```text
PrintableBook-<version>-win-x64/
├─ PrintableBook.exe
└─ Frontend/
   ├─ index.html
   ├─ js/
   ├─ css/
   └─ assets/
```

`brands/`, `sources/`, `settings.json`, `.workspace/` và output của người dùng không nằm trong ZIP. Chúng được tạo hoặc đặt cạnh executable sau khi giải nén vào thư mục có quyền ghi.

## Update preparation assets and storage

Mỗi release xuất hai asset phục vụ quá trình chuẩn bị cập nhật:

```text
PrintableBook-<version>-win-x64.zip
PrintableBook-<version>-win-x64.zip.sha256
```

Sidecar SHA256 chứa đúng một dòng theo định dạng:

```text
<64-hex-sha256>  PrintableBook-<version>-win-x64.zip
```

Trước khi có updater riêng, ứng dụng chỉ tải, kiểm tra SHA256, giải nén và
stage payload đã kiểm chứng tại vị trí ngoài AppRoot:

```text
%LOCALAPPDATA%\PrintableBook\Updates\
├─ downloads\
└─ staging\
```

PR2 không ghi đè AppRoot, không cài đặt, và không restart ứng dụng. SHA256
sidecar chỉ kiểm tra tính toàn vẹn; code signing và xác thực chữ ký chưa có ở
giai đoạn này.

## Dữ liệu WebView2 lúc chạy

Chromium profile, GPU cache và crash data của WebView2 được lưu cố định tại
`%LOCALAPPDATA%\PrintableBook\WebView2`, không phải cạnh `PrintableBook.exe`.
Vì vậy gói portable giữ nguyên contract chỉ gồm EXE và `Frontend/`, đồng thời
profile vẫn được giữ lại qua các lần cập nhật ZIP. Ứng dụng không tự di chuyển
hay xóa các thư mục `PrintableBook.exe.WebView2` cũ để tránh làm mất dữ liệu
trong profile hiện có.

## Điều kiện chạy

- Windows x64.
- .NET Desktop Runtime 10 x64.
- Microsoft Edge WebView2 Runtime.

## Phạm vi thay đổi

Tối ưu single-file chỉ nằm trong `scripts/publish-release.ps1`. Các lệnh build Debug và Release thông thường vẫn giữ output multi-file để phát triển và kiểm thử thuận tiện.
