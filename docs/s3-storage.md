# S3 Storage MVP

## Phạm vi

S3 Storage publish output của đúng một Book mỗi lần. Một Book có bảy file cố định và worker xử lý tối đa bốn file đồng thời. Không có bulk upload, remote delete, multipart resume hoặc fallback sang private object.

Destination:

```text
key = {folder}/{UPPERCASE-ASIN}/{file-name}
url = https://s3.dualstack.{region}.amazonaws.com/{bucket}/{escaped-key}
```

Ví dụ:

```text
https://s3.dualstack.us-east-1.amazonaws.com/vxgroup.tinh/coloring/B0FCC8Q6JP/back_cover.jpg
```

## Cấu hình

1. Mở **Settings → Configuration → S3 Storage**.
2. Nhập Region, Bucket và Folder rồi nhấn **Save**.
3. Nhập cả Access Key và Secret Key rồi nhấn **Replace credentials**.
4. Credential được mã hóa cho Windows user hiện tại trong `s3.credentials.dat`; không nằm trong `settings.json`.

Để publish, Book phải có ASIN gồm đúng 10 ký tự chữ/số và ASIN đó không được trùng Book khác. Folder được chuẩn hóa thành các path segment; `//`, `.` và `..` bị từ chối.

## IAM tối thiểu

Thay bucket/folder trong policy sau bằng destination thật:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ListPublicationPrefix",
      "Effect": "Allow",
      "Action": "s3:ListBucket",
      "Resource": "arn:aws:s3:::vxgroup.tinh",
      "Condition": {
        "StringLike": {
          "s3:prefix": ["coloring/*"]
        }
      }
    },
    {
      "Sid": "PublishAndVerifyObjects",
      "Effect": "Allow",
      "Action": ["s3:GetObject", "s3:PutObject", "s3:PutObjectAcl"],
      "Resource": "arn:aws:s3:::vxgroup.tinh/coloring/*"
    }
  ]
}
```

MVP gửi ACL `public-read`. Bucket phải cho phép ACL và anonymous HTTP HEAD/GET cho prefix publish. Bucket dùng Object Ownership `Bucket owner enforced` sẽ trả `AccessControlListNotSupported`; app báo lỗi và không tự chuyển sang private object. Không cấp `s3:DeleteObject` vì workflow không xóa remote.

## Check và Upload

Trong **Book Detail → Settings → S3 Storage**:

- **Check** luôn khả dụng khi config/credential/ASIN hợp lệ, kể cả khi local còn thiếu file. Nó LIST prefix, kiểm tra từng object có trong prefix và xác nhận length, SHA-256 metadata, public access. Check không PUT.
- **Upload** chỉ khả dụng khi đủ bảy local artifact. Worker copy file vào immutable staging, hash và chạy toàn bộ remote preflight trước PUT đầu tiên. File đã sync được skip; chỉ file missing/changed/not-public được upload và verify lại.
- Mỗi row hiển thị facts Local, Remote và Public. File local thiếu ghi rõ action Production cần chạy.
- Khi một Book đang Check/Upload, Book khác chỉ hiển thị owner đang active và không thể tạo queue thứ hai.

Manifest:

1. `{BookId} - Cover.pdf`
2. `{BookId} - Interior.pdf`
3. `{BookId} - Cover_thumbnail.pdf`
4. `{BookId} - Cover_thumbnail.png`
5. `{BookId} - Interior_thumbnail.pdf`
6. `back_cover.jpg`
7. `front_cover.jpg`

## Cancel, crash và retry

Cancellation là cooperative. Request đang bay nhận cancellation token; waiter chưa lấy được slot sẽ không bắt đầu. Nếu cancel/failure xảy ra sau khi PUT đã bắt đầu, remote có thể chứa một phần phiên bản mới vì stable-key publish không phải transaction. UI sẽ báo partial publication.

Recovery:

1. Không sửa/xóa object bằng tay khi chưa cần thiết.
2. Chạy **Check** để lấy facts mới nhất.
3. Chạy **Upload** lại; file đã verified sẽ được skip và file còn lệch được sửa.

Nếu app crash, receipt `Running` được đổi thành `Interrupted` ở lần đọc kế tiếp. Đóng app hoặc restart để update trong khi S3 chạy sẽ hiện cảnh báo, gửi cancel và chờ tối đa 5 giây; chỉ force exit khi chấp nhận rủi ro partial remote set.

## Mã lỗi thường gặp

| Mã | Ý nghĩa / cách xử lý |
| --- | --- |
| `s3_asin_invalid` | Lưu ASIN 10 ký tự chữ/số trong Book Information. |
| `s3_asin_duplicate` | Đổi ASIN để mỗi Book sở hữu một destination riêng. |
| `s3_upload_preflight_failed` | Tạo đủ file được liệt kê trước khi Upload. |
| `s3_bucket_not_found` | Kiểm tra Bucket và AWS account. |
| `s3_region_mismatch` | Chọn đúng region của Bucket. |
| `s3_access_denied` | Kiểm tra IAM, bucket policy và public access settings. |
| `s3_public_acl_unsupported` | Bucket không chấp nhận `public-read`; chọn/cấu hình bucket tương thích. |
| `s3_remote_verification_failed` | Object sau PUT không khớp length/hash; chạy Check rồi retry. |
| `s3_public_verification_failed` | Object tồn tại nhưng anonymous HEAD không thành công. |
| `s3_publication_interrupted` | Operation trước bị dừng; chạy Check trước Upload. |

Không đưa Access Key, Secret Key hoặc nội dung `s3.credentials.dat` vào log, screenshot hoặc ticket hỗ trợ.
