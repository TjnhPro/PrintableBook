# Amazon Crawl by Language Market

## Trạng thái

Phase 4 đã được triển khai trên crawler hiện có. Đây là phạm vi MVP: một catalog cấu hình chung, một crawler, một parser và một browser client; không có marketplace selector, profile migration, lease hay provenance cho ASIN.

## Market catalog

Backend resolve market duy nhất từ `Book.Language`. Frontend chỉ gửi `bookId` và không được gửi domain, locale hoặc market override.

| Language | Market | Domain | Locale | Persistent profile |
|---|---|---|---|---|
| `en` | United States | `amazon.com` | `en-US` | `.cloakbrowser/profile-v1` |
| `de` | Germany | `amazon.de` | `de-DE` | `.cloakbrowser/profile-de-v1` |
| `fr` | France | `amazon.fr` | `fr-FR` | `.cloakbrowser/profile-fr-v1` |
| `es` | Spain | `amazon.es` | `es-ES` | `.cloakbrowser/profile-es-v1` |
| `it` | Italy | `amazon.it` | `it-IT` | `.cloakbrowser/profile-it-v1` |
| `pt` | Brazil | `amazon.com.br` | `pt-BR` | `.cloakbrowser/profile-br-v1` |
| `ja` | Japan | `amazon.co.jp` | `ja-JP` | `.cloakbrowser/profile-jp-v1` |
| `nl` | Netherlands | `amazon.nl` | `nl-NL` | `.cloakbrowser/profile-nl-v1` |

US giữ path cũ để không làm mất cookie/profile hiện tại. Các market còn lại có profile riêng. Profile là persistent, nhưng browser context không được reuse giữa hai crawl.

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
- Search URL, allowed hosts, locale/fingerprint và title terms nằm trong `AmazonMarketplaceCatalog`.
- Title matching dùng data cấu hình MVP theo market. Có thể bổ sung term khi quan sát thực tế mà không đổi kiến trúc.
- Parser chung ưu tiên structural selectors để phát hiện sponsored result; localized text marker chỉ là fallback best-effort.
- Thiếu localized sponsored/no-result marker không tự làm fail toàn crawl. Challenge hoặc markup không nhận diện mới fail closed theo error contract hiện có.

## UI

ASIN Research hiển thị `Amazon Market: <market> — <domain>` từ status/view do backend trả. Open Browser và Crawl ASINs bị disable trong lúc crawl; sau terminal response, UI phản ánh browser context đã `Closed`.

## Ngoài phạm vi

- Không thay đổi Keyword Builder generation, Publishing ASIN hoặc S3.
- Không tạo crawler/parser riêng theo quốc gia.
- Không xóa/recreate profile sau mỗi crawl.
- Không cho user chọn marketplace thủ công.
- Không thêm profile switching/migration, browser lease hoặc state machine phức tạp.
