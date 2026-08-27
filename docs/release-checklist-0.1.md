# Release checklist — Printable Book v0.1.0

> Cập nhật checkbox chỉ bằng kết quả thực tế. Không tag/publish nếu mục release-blocking chưa PASS.

## Source và CI

- [x] Release baseline recorded: `db8e192`.
- [ ] Build and test CI green for release commit.
- [x] Repository-owned release gate PASS: Core 188, Infrastructure 252, Desktop 89, bridge 72 and production UI certification 16.

## Documentation và branding

- [x] Architecture và technical docs aligned with v0.1.
- [x] README/user guide/release notes reviewed.
- [x] Approved PNG/ICO committed and EXE/window/sidebar icon verified.
- [x] Visible `Version 0.1`; project `0.1.0`; FileVersion `0.1.0.0`.
- [x] 18 demo-only screenshots reviewed: no personal paths, usernames or production/customer art.

## Artifact và smoke

- [x] Portable `win-x64` package created with ZIP and SHA256.
- [x] ZIP has one versioned top-level folder and required Frontend/Assets files.
- [x] Clean writable folder launch PASS.
- [x] Path-with-spaces launch/Refresh PASS.
- [ ] CUSTOM Intro, Active/Frame, process, cancel, PDF Library, Open/Reveal/Copy, Clear Cache and restart PASS.

## Publish

- [x] `release.yml` validated locally against the release gate and packaging command.
- [ ] `main` clean and current CI green.
- [ ] Annotated tag `v0.1.0` pushed.
- [ ] GitHub Release includes ZIP and SHA256.
- [ ] Downloaded published ZIP smoke-tested.
