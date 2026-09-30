# Keyword Builder v4 migration and rollback

## Normal upgrade

1. Back up the Book `.workspace` directory before changing application versions.
2. Open **Book Detail → ASIN Research**.
3. A saved v4 build is rebuilt exactly and receives a fresh process receipt automatically.
4. A v1–v3 build stays readable, but Save and Crawl remain unavailable until **Shuffle** is pressed once.
5. Review the preview and press **Save**. The new state keeps the existing nine output fields and adds deterministic v4 metadata.

## Receipt recovery

Receipts are intentionally valid only for the current application process and Book. Press **Shuffle** again when the UI reports `keyword_preview_invalid`, `keyword_preview_stale`, `keyword_preview_version_unsupported`, or `keyword_legacy_shuffle_required`. Never copy receipts, seeds, signatures, or HMAC keys into logs or support tickets.

If library refresh fails after Save, the workspace state is already committed. Use **Retry refresh**; do not Shuffle unless a new order is wanted.

## ASIN crawl recovery

- Crawl always resolves Ads Keyword from a signed preview or exact saved BuildId on the server.
- If Book Keywords or Ads ASIN changes while crawl runs, retained rows remain visible but are not merged. Shuffle and crawl again.
- Partial/cancelled runs may merge valid rows only while both source and target revisions are current. They never auto-save.
- For CAPTCHA/rate-limit challenges, resolve Amazon in the app-owned browser and retry.

## Rollback

Rollback is a whole-release rollback. Close the app, restore the backed-up workspace if the older binary will write state, then install the older release. Unsaved receipts cannot and should not survive rollback. Older readers can still see the nine persisted output fields, but same-seed editing and trusted crawl are unavailable. Do not manually remove seed/fingerprint/digest properties from workspace JSON.
