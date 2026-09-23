# Printable Book

## Printable Book dùng để làm gì?

Printable Book là phần mềm Windows chạy local để tổ chức Book Coloring, kiểm tra dữ liệu đầu vào, chuẩn bị Interior artwork và xuất PDF cho workflow KDP. Ứng dụng dùng ảnh và thư mục ngay trên máy của bạn; không cần upload Book lên dịch vụ bên ngoài.

## Yêu cầu hệ thống

- Windows x64.
- Bản portable v0.1: giải nén vào thư mục có quyền ghi, ví dụ `C:\PrintableBook\`.
- Không đặt bản chạy chính trong `Program Files`: `brands/`, `sources/`, `settings.json` và `.workspace/` được quản lý cạnh executable.
- Release hiện tại yêu cầu .NET Desktop Runtime 10 x64 đã được cài trên máy.
- Microsoft Edge WebView2 Runtime phải có sẵn trên Windows.
- Để build source: .NET 10 SDK, Node.js 24 và Windows (WPF/WebView2 host).

## Cấu trúc thư mục

```text
PrintableBook/
├─ PrintableBook.exe
├─ Frontend/
├─ brands/
├─ sources/
└─ settings.json
```

Mỗi thư mục trực tiếp trong `sources/` là một Book. Gói Book mới có thể chứa `Main book/Book cover/` và `Clone book/`: ảnh được hỗ trợ đầu tiên trong `Main book/Book cover/` chỉ dùng làm thumbnail, còn toàn bộ ảnh xử lý vẫn đọc từ cấu trúc folder hiện có bên trong `Clone book/`. Các folder Main khác chưa được đọc. Book theo cấu trúc phẳng cũ vẫn được hỗ trợ khi không có `Clone book/`. Khi xử lý, Book có `.workspace/` riêng cho state/cache và `Output/` cho PDF đã publish.

## Bắt đầu nhanh

1. Download ZIP release, giải nén vào thư mục writable.
2. Chạy `PrintableBook.exe`.
3. Thêm Brand vào `brands/` và Book vào `sources/`. Brand hợp lệ cần `cover.psd`, `app_plus.psd` và `book_owner.psd` ở root.
4. Trong **Books**, nhấn **Refresh** và chọn Brand.
5. Mở Book detail để kiểm tra Interior, Intro, Active và Frame mode.
6. Chọn Book, nhấn **Process Interior**, rồi kiểm tra các trang đã chuẩn bị trong tab **Interior pages**.
7. Khi cần PDF giao production, mở tab **Production** và nhấn **Build Final Interior**; PDF xuất hiện trong **PDF Library**.

Để dùng workflow Production, mở tab **Production** trong Book detail, upload ba PNG canonical, build Cover và Final Interior theo hướng dẫn trong [User Guide](docs/user-guide.md#8-production-assets).

## Workflow

```text
Brand + Book folders
→ Refresh
→ kiểm tra/chọn Interior
→ Process Interior
→ kiểm tra Interior pages
→ Build Final Interior
→ PDF Library
```

Mỗi trang Interior được chuẩn hoá thành `normalized-source.png`, sau đó classification dùng BorderLine V3 và BorderPixel V1 fallback, preparation, frame (nếu có) và assembly. **Process Interior** dừng tại đây, lưu preview trang và không tạo/ghi đè PDF. **Build Final Interior** dùng lại pipeline hiện tại rồi export/publish PDF. Chi tiết kỹ thuật nằm trong [architecture](docs/architecture.md).

Interior bình thường chỉ có hai mode: **Frame** và **No Frame**. Book/page mới mặc định **No Frame**. **Frame** bắt buộc dùng `frame.png` hợp lệ của Brand và sẽ fail rõ ràng nếu asset bị thiếu hoặc sai; **No Frame** dùng CropArt và không overlay frame. State cũ dùng Auto được đọc thành No Frame và có cảnh báo review theo Book; PDF đã publish không tự thay đổi cho đến khi user process/publish lại.

## Intro AUTO và CUSTOM

- **AUTO** (`HasIntro=false`): dùng toàn bộ ảnh hợp lệ trong `Brand/IntroTemplate/`, theo tên file tăng dần. Ảnh Brand đúng kích thước Final Interior Page (mặc định `2588x2625`) được đưa thẳng vào PDF; `1024x1024` và `2048x2048` vẫn dùng luồng CropArt.
- **CUSTOM** (`HasIntro=true`): dùng danh sách Book Interior do bạn chọn và giữ đúng thứ tự đó. Những trang đã chọn không lặp lại trong Interior normal hoặc shuffle.

Intro legacy và Custom luôn được xử lý theo CropArt, không chạy detector và không dùng frame. AUTO Brand artwork ở kích thước Final Interior Page cũng không dùng detector hoặc frame, nhưng bỏ qua toàn bộ xử lý raster vì đã là trang in hoàn chỉnh. Nếu bật Brand Background, background được chèn sau từng trang Intro và Interior.

![CUSTOM Intro](docs/assets/screenshots/0.1/04-book-interior-settings-custom-intro.png)

## Process Interior

**Process** hiển thị queue, current stage, số worker và tiến độ. Action này chuẩn bị Intro + Interior page để preview, không build hay thay thế PDF. Mỗi session chỉ xử lý một Book tại một thời điểm; concurrency chỉ áp dụng các trang trong Book hiện tại, từ 1 đến 12 worker. Bạn có thể request **Cancel session**; cancellation là cooperative nên trạng thái sẽ chuyển terminal khi worker đã dừng an toàn. Nếu fail/cancel sau khi bắt đầu, preview dở dang bị xóa nhưng PDF hiện có được giữ nguyên.

![Process running](docs/assets/screenshots/0.1/09-process-running.png)

## Production Assets

Workflow Production sở hữu việc publish PDF; **Process Interior** chỉ chuẩn bị page preview:

```text
final_cover.png             → Build Cover PDF
interior_cover.png          → No Frame / CropArt ┐
interior_book_owner.png     → No Frame / CropArt ├→ Build Final Interior
Intro + randomized Interior ─────────────────────┘
```

Cover PNG phải đúng `5242 × 2626 px`; PDF Cover dùng trang `17.47 × 8.75 inch`. Hai trang prefix Interior không bắt buộc kích thước input nhưng luôn dùng policy No Frame/CropArt. **Build Final Interior** là action duy nhất tạo mới/thay thế `<BookId> - Interior.pdf`, theo thứ tự Interior Cover, Book Owner, Intro, rồi Interior đã shuffle; nếu `HasBackground` bật, một background được xen sau mỗi artwork. PDF Library vẫn đọc provenance `Base` và `Legacy` từ output cũ, nhưng output Interior mới luôn là `Production`.

## PDF Library

**PDF Library** hiển thị mọi Cover/Interior PDF đã publish, kể cả output thành công gần nhất còn được giữ lại sau một lần chạy lỗi. Từ card bạn có thể **Open**, **Reveal** trong Explorer hoặc **Copy** path. Clear Cache xóa raster trung gian của Book Completed có output hợp lệ hoặc processed previews; không xóa PDF đã publish.

![PDF Library](docs/assets/screenshots/0.1/11-pdf-library.png)

## Screenshots

![Books Library](docs/assets/screenshots/0.1/01-books-library.png)

Hướng dẫn thao tác đầy đủ bằng tiếng Việt: [User Guide](docs/user-guide.md).

## Build & Test

```powershell
dotnet restore PrintableBook.sln
dotnet build PrintableBook.sln --configuration Release --no-restore
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "TestScope!=LocalCorpus"
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --configuration Release --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
node src/PrintableBook.Desktop/Frontend/test-production-ui.mjs
```

Corpus ảnh do user cung cấp ở `TestResults/` thuộc `LocalCorpus`, chỉ chạy local opt-in và không phải dependency của CI. Xem [Testing policy](docs/architecture.md#kiểm-thử).

## Kiểm thử artifact với Book mẫu

`.booksample/` là dữ liệu local bị Git ignore, chỉ dùng để smoke test **artifact đã publish hoặc giải nén**; không được copy vào Debug output hay đóng gói mặc định trong bản phát hành. Sau khi publish hoặc giải nén artifact vào một thư mục riêng, cài Brand và Book mẫu bằng:

```powershell
pwsh ./scripts/install-artifact-samples.ps1 -ArtifactRoot "C:\PrintableBook-test"
```

Script mặc định không ghi đè Brand hoặc Book mẫu cùng tên đã có. Với một artifact test sạch nhưng cần cập nhật lại sample cùng tên, dùng `-Force`. Sau đó chạy `PrintableBook.exe` từ artifact root, nhấn **Refresh**, validate Brand `demo`, rồi kiểm tra Book `book-sample` trong Books và Process Interior.

## Tài liệu kỹ thuật

- [Kiến trúc v0.1](docs/architecture.md)
- [Background process session](docs/background-process-session.md)
- [BorderLine V3](docs/borderline-detector-v3.md)
- [BorderPixel V1](docs/borderpixel-detector-spec.md)
- [Interior pipeline](docs/interior-shared-pipeline-integration.md)
- [Intro processing](docs/intro-template-processing.md)
- [Release packaging](docs/release-packaging.md)

## Release

v0.1.0 là portable, unsigned Windows x64 ZIP. Release notes: [docs/release-0.1.md](docs/release-0.1.md). Hướng dẫn tạo artifact: `scripts/publish-release.ps1 -ExpectedVersion 0.1.0`.
