# Amazon Crawl by Language Market

## Trạng thái

Phase 4 đã được triển khai trên crawler hiện có. Đây là phạm vi MVP: một bộ profile trong Global Settings, một crawler, một parser và một browser client; không có marketplace selector trong Book, profile migration, lease hay provenance cho ASIN.

## Market settings

Backend resolve market duy nhất từ `Book.Language`. Frontend của ASIN Research chỉ gửi `bookId` và không được gửi domain, locale hoặc market override. Configuration cho phép sửa profile của từng Language rồi lưu toàn bộ map atomically trong `settings.json`.

| Language | Market | Default Base URL | Default Locale | Default profile key |
|---|---|---|---|---|
| `en` | United States | `https://www.amazon.com/` | `en-US` | `us` |
| `de` | Germany | `https://www.amazon.de/` | `de-DE` | `de` |
| `fr` | France | `https://www.amazon.fr/` | `fr-FR` | `fr` |
| `es` | Spain | `https://www.amazon.es/` | `es-ES` | `es` |
| `it` | Italy | `https://www.amazon.it/` | `it-IT` | `it` |
| `pt` | Brazil | `https://www.amazon.com.br/` | `pt-BR` | `br` |
| `ja` | Japan | `https://www.amazon.co.jp/` | `ja-JP` | `jp` |
| `nl` | Netherlands | `https://www.amazon.nl/` | `nl-NL` | `nl` |

Settings legacy chưa có `amazonMarketplaceProfiles` được materialize trong memory bằng bảng default trên và không tự rewrite file. Save mới ghi đủ tám profile. Profile key được expand thành `.cloakbrowser/profile-<key>-v1`; riêng default `en/us` giữ `.cloakbrowser/profile-v1` để không làm mất cookie hiện tại. Profile là persistent, nhưng browser context không được reuse giữa hai crawl.

Mỗi profile có đúng bốn field MVP:

- `ProfileKey`: lowercase letter/number/hyphen, unique giữa các Language.
- `BaseUrl`: HTTPS origin của Amazon domain đã gán cho Language, không path/query/fragment/credential/custom port.
- `Locale`: locale không rỗng truyền vào browser context.
- `TitleTerms`: input một dòng; các phrase phân tách bằng dấu phẩy, được normalize và dedupe khi save/load.

## Lifecycle contract

```text
Book.Language
→ resolve AmazonMarketplaceProfile
→ close context hiện có nếu có
→ open context mới bằng persistent profile của market
→ crawl keyword và parse kết quả
→ close context trong finally
```

- `amazon.browser.status` là read-only: không open, close, switch hoặc mutate browser context.
- `amazon.browser.open` dùng market của Book hiện tại và bị reject bằng `amazon_asin_crawl_active` khi crawl đang chạy.
- `book.keywords.asin-crawl.start` không nhận keyword/domain từ UI ngoài receipt đã ký; backend resolve Book, trusted keywords và market.
- Chỉ có một Amazon crawl app-wide tại một thời điểm theo background-task policy hiện có.
- Mọi terminal path (`Completed`, `Partial`, `Failed`, `NeedsAttention`, `Cancelled`, exception) đều chạy `CloseAsync` trong `finally` với `CancellationToken.None`.
- Nếu final close lỗi, diagnostics ghi `amazon.browser.close.warning`; outcome/view đã xác định của crawl không bị thay thế.
- Lần crawl tiếp theo luôn gọi `OpenFreshAsync`, do đó không mang context/session đang mở của market trước sang market mới. Cookie/settings vẫn được giữ trong persistent profile riêng của market.

## Search và parser

- Keyword không được dịch trong crawler; nguồn keyword theo ngôn ngữ đến từ Keyword Builder.
- Search URL, allowed hosts, locale/fingerprint và title terms được dựng từ profile đã load trong Global Settings; crawler không có fallback runtime về catalog hardcode.
- Title matching dùng data cấu hình MVP theo market. Có thể bổ sung term khi quan sát thực tế mà không đổi kiến trúc.
- Parser chung ưu tiên structural selectors để phát hiện sponsored result; localized text marker chỉ là fallback best-effort.
- Thiếu localized sponsored/no-result marker không tự làm fail toàn crawl. Challenge hoặc markup không nhận diện mới fail closed theo error contract hiện có.

## UI

Configuration có group **Amazon market profiles** với Language selector và bốn input nêu trên. Draft của từng Language được giữ khi đổi selector; nút Save chung ghi atomically cùng các Global Settings khác. ASIN Research hiển thị `Amazon Market: <market> — <domain>` từ status/view do backend trả. Open Browser và Crawl ASINs bị disable trong lúc crawl; sau terminal response, UI phản ánh browser context đã `Closed`.

## Ngoài phạm vi

- Không thay đổi Keyword Builder generation, Publishing ASIN hoặc S3.
- Không tạo crawler/parser riêng theo quốc gia.
- Không xóa/recreate profile sau mỗi crawl.
- Không cho user chọn marketplace thủ công.
- Không thêm profile switching/migration, browser lease hoặc state machine phức tạp.
