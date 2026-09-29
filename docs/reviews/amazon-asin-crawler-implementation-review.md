# Amazon ASIN crawler — implementation review

Date: 2026-09-29
Branch: `feat/amazon-asin-crawler`

## 1. CloakBrowser — PASS (lifecycle and package), Amazon smoke pending

- App-owned headed persistent context; profile is `<AppRoot>/.cloakbrowser/profile-v1` and binary cache is `<AppRoot>/.cloakbrowser/cache`.
- Fixed in-page fetch uses cookies and validates the current origin plus final redirect against exact Amazon HTTPS hosts.
- Startup/download/profile-lock/license/network failures are reduced to safe error codes; shutdown is bounded and closes the owned context.
- Published `win-x64` artifact contains external `.playwright/node/win32_x64/node.exe` (80,511,640 bytes), while `.cloakbrowser` is absent.
- Storage/module/publish contract tests pass.
- Three real CloakBrowser integration tests pass: open/close with lock release, close/reconnect with persisted profile cookie, and navigation to `https://www.google.com/`.
- The run downloaded and cryptographically verified free Chromium `146.0.7680.177.5` (535 MB) into `artifacts/cloakbrowser-integration/cache`; test profiles are retained under `artifacts/cloakbrowser-integration/profiles` for reuse.

Re-run all three cases with `./scripts/test-cloakbrowser-lifecycle.ps1`.

The latest free binary still requires a CloakBrowser access key; this workstation had no key, so the wrapper selected its keyless free v146 binary. Run `scripts/test-amazon-crawl-smoke.ps1 -ArtifactRoot <published-folder> -Launch` after configuring the key for the separate real Amazon checkpoint.

## 2. AmazonCrawl — PASS

- Html Agility Pack parses ordered result cards, excludes a card when its full text contains `Sponsored`, and extracts title through the four verified selector fallbacks before normalization; title policy, duplicate selection, empty/challenge/unsupported markup and safe reason codes are isolated from browser and Book code.
- A response is accepted as an Amazon search response only when Html Agility Pack finds `id="twotabsearchtextbox"`; challenge pages remain `NeedsAttention`, while other responses without the marker fail with `amazon_searchbox_missing`.
- The local 2,246,595-byte `keyword.html` capture passed: one searchbox and 60 result cards. Twelve Sponsored cards were excluded and 48 ordered organic ASIN/title pairs remained; ASINs appearing in both placements survive through their organic card, and four representative exact pairs matched.
- Input is normalized and bounded at 30 phrases / 200 graphemes; fetch is bounded at 20 seconds / 5 MiB and crawl at 10 minutes.
- Targeted Core Amazon tests: 14 passed. Targeted parser/browser-boundary tests: 15 passed, plus the opt-in captured HTML test.

Re-run the local capture check with `./scripts/test-amazon-captured-html.ps1`.

## 3. Task and bridge — PASS

- Dedicated Amazon lane has concurrency 1. Exact key reattaches; another request is rejected with `amazon_asin_crawl_active`.
- Typed Start/Get/Cancel projection preserves partial results and per-Book ownership; browser setup failure now publishes a terminal view instead of leaving the UI in `Idle`.
- Normal close, update restart and Windows session ending cancel the crawl and close the browser with bounded waits.
- Desktop suite: 168 passed. Bridge suite: 109 passed.

## 4. Keyword Builder UI — PASS

- Search draft is seeded once per Book and then independent. Crawl never saves Book data; `Use in Ads ASIN` only changes the Ads ASIN draft and `Build & Save` remains the persistence point.
- Polling patches only ASIN Research and restores scroll/focus/selection. Stale results cannot be applied; a previous `NeedsAttention` result does not permanently disable retry.
- Five-row input, ordered result rows, progress, partial/attention states, keyboard semantics and 1100/760 responsive layouts are covered by contract tests.
- Node bridge tests and Production UI certification pass.

## 5. Compatibility, update and release — PASS

- Existing Keyword Builder, Book metadata, Process Interior, Production, PDF Library and update flows remain green.
- Full .NET result: Core 340, Infrastructure 434, Desktop 168, Updater 55, UpdateSecurity 43, ReleaseTool 32; total 1,072 passed, 0 failed. Nine opt-in/local-corpus tests were skipped by their normal gates.
- Restore/build succeeds with 0 warnings and 0 errors. NuGet vulnerable-package scan reports none, including transitive dependencies.
- Updater backs up/replaces controlled `.playwright` and preserves `.cloakbrowser`; package validators and release verifier require both the driver package and Windows Node executable.

## Remaining external checkpoint

Run the opt-in live smoke with a configured CloakBrowser access key and a real Amazon session. This is intentionally outside deterministic CI because it downloads an external browser binary and depends on Amazon challenge/rate-limit state.
