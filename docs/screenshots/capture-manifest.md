# Printable Book v0.1 screenshot capture manifest

## Quy tắc chung

- Chỉ capture từ `C:\PrintableBook Demo\rc\PrintableBook-0.1.0-win-x64\PrintableBook.exe` đã package.
- Window `1650×950`, Windows scaling 100%, light theme, PNG, chỉ application window.
- Không taskbar/desktop/tool overlay, username, đường dẫn cá nhân, artwork production/customer.
- Dùng local-only **Demo Brand**, **Garden Animals Demo**, **Flowers Demo** và **Needs Review Demo**. Demo folders không được commit và không nằm trong ZIP final.

## Sequence

| File | Route/state bắt buộc | Mục đích |
| --- | --- | --- |
| `01-books-library.png` | Books; Demo Brand; nhiều Book, one Ready/one PDF ready và cover thumbnail | Product hero |
| `02-book-overview.png` | Garden Animals Demo → Overview | status/preflight/page count |
| `03-book-interior-settings-auto-intro.png` | Garden Animals Demo → Interior settings; `HasIntro=false`, Background enabled | AUTO Brand IntroTemplate |
| `04-book-interior-settings-custom-intro.png` | Garden Animals Demo → Interior settings; `HasIntro=true`, 2 Book Interior pages selected | CUSTOM Intro order/candidates |
| `05-book-interior-artwork.png` | Garden Animals Demo → Interior artwork; mix Active/Inactive + Auto/Frame/No frame | Tile controls |
| `06-book-interior-artwork-bulk.png` | Interior artwork; multiple selected; Status + Frame bulk controls | Bulk change |
| `07-book-processed-pages.png` | Garden Animals Demo → Interior pages after completed run | final page preview |
| `08-process-selected-queue.png` | Process → Selected queue; 3 Demo Books selected | editable/paged queue |
| `09-process-running.png` | Process → Overview; real active session with non-zero progress | current Book/stage/workers |
| `10-process-completed.png` | Process → Overview; terminal completed session | final summary |
| `11-pdf-library.png` | PDF Library; at least two completed Demo Books | PDF rows/actions |
| `12-brands-templates.png` | Brands & templates; Demo Brand | IntroTemplate/frame/background present |
| `13-settings-basic.png` | Settings; Application/Interior/PDF fields visible | basic settings |
| `14-settings-advanced-detection.png` | Settings; advanced detection | normalized size, two passes and tolerance |
| `15-needs-review.png` | Needs Review Demo; CUSTOM enabled but empty selection | safe validation problem |
| `16-diagnostics-summary.png` | Diagnostics → Summary | health summary |
| `17-diagnostics-tasks.png` | Diagnostics → Tasks | recent task rows |
| `18-diagnostics-performance.png` | Diagnostics → Performance | timings/diagnostic content |

## Documentation mapping

- README: `01`, `04`, `09`, `11` only.
- User Guide: `01` through `15` where relevant.
- Support/troubleshooting: `15` through `18`.

## Capture review gate

Sau mỗi PNG: mở file, kiểm tra không broken image/cursor che nội dung, tên app và `Version 0.1` còn đúng, không có path/user private. Chỉ commit toàn bộ bộ 18 ảnh sau khi review đủ.
