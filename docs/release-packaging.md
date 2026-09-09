# Release packaging

## Normal release

Feature work is merged through normal pull requests and must pass the
repository `Build and test` workflow. After `main` is green, release with:

```powershell
.\scripts\release.ps1
```

With no arguments the script increments the patch version automatically. For
example, `0.2.0` becomes `0.2.1`. To choose a specific higher version:

```powershell
.\scripts\release.ps1 -Version 0.3.0
```

The script validates `main`, a clean working tree and successful CI for the
exact pre-release `main` SHA. It changes only `Directory.Build.props`, creates
the release commit and tag, atomically pushes both, waits for `Publish release`
and reports the final GitHub Release URL. No local production signing key is
needed.

If validation fails before the atomic push, the local version and tag changes
are rolled back. If a remote tag was already pushed but publication did not
finish, rerun `./scripts/release.ps1`; it resumes the current tagged version
instead of incrementing to another patch release.

## Package and security contract

Printable Book phát hành portable Windows x64 framework-dependent trên .NET
10. The tag workflow uses the existing production signing secret and
`scripts/publish-release.ps1`; local users never provide that key. Its internal
packaging command reads matching Desktop and Updater versions:

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

`v0.1.1` không chứa updater. `v0.2.0` là first updater-enabled release và
người dùng `v0.1.1` phải cài thủ công một lần. Các release sau đó có thể
auto-update.

## Optional deep validation

`Build release candidate` remains available as an optional diagnostic/deep
validation workflow. It is not required for routine private/local releases.
