# Release packaging

Printable Book phát hành portable Windows x64 framework-dependent trên .NET 10. Lệnh đóng gói đọc version từ cả Desktop và Updater, yêu cầu chúng giống nhau, và yêu cầu private signing seed từ environment:

```powershell
scripts/publish-release.ps1 -ExpectedVersion 0.1.1
```

Package được xác minh trước khi dùng có contract:

```text
PrintableBook-<version>-win-x64/
├─ PrintableBook.exe
├─ PrintableBook.Updater.exe
└─ Frontend/
```

`brands/`, `sources/`, `settings.json`, `.workspace/`, `Output/`, user data và binary/runtime file rời không được phép trong root ZIP.

Mỗi release xuất chính xác bốn top-level assets:

```text
PrintableBook-<version>-win-x64.zip
PrintableBook-<version>-win-x64.zip.sha256
PrintableBook-<version>-win-x64.manifest.json
PrintableBook-<version>-win-x64.manifest.json.sig
```

`.sha256` là sidecar dễ đọc cho integrity. Signed manifest là binding đã xác thực giữa version, runtime, archive/checksum names, sizes và SHA256 hashes; signature Ed25519 được kiểm tra trước khi runtime dùng metadata đó.

`PublishSingleFile=true` giữ Desktop và Updater là executable đơn, không trimming, ReadyToRun hay compression. Desktop vẫn dùng WebView2 loader static và `Frontend` runtime assets; profile WebView2 nằm trong `%LOCALAPPDATA%\PrintableBook\WebView2`.

`v0.1.1` không chứa updater. `v0.2.0` là planned first updater-enabled release và người dùng `v0.1.1` phải cài thủ công một lần. Các release sau đó có thể auto-update.

Candidate packaging có thể chạy khi source version vẫn là `0.1.1`; chỉ tạo workflow artifact, không tag hay tạo GitHub Release. Một release-prep riêng mới thay source version thành `0.2.0`.
