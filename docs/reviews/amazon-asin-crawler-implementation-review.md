# Amazon ASIN crawler — implementation review

Date: 2026-09-29
Branch: `feat/amazon-asin-crawler`

## 1. CloakBrowser — PASS (code/package), live smoke pending

- App-owned headed persistent context; profile is `<AppRoot>/.cloakbrowser/profile-v1` and binary cache is `<AppRoot>/.cloakbrowser/cache`.
- Fixed in-page fetch uses cookies and validates the current origin plus final redirect against exact Amazon HTTPS hosts.
- Startup/download/profile-lock/license/network failures are reduced to safe error codes; shutdown is bounded and closes the owned context.
- Published `win-x64` artifact contains external `.playwright/node/win32_x64/node.exe` (80,511,640 bytes), while `.cloakbrowser` is absent.
- Storage/module/publish contract tests pass.

Live launch was not run on this workstation because neither `CLOAKBROWSER_LICENSE_KEY` nor the default license file is configured. Run `scripts/test-amazon-crawl-smoke.ps1 -ArtifactRoot <published-folder> -Launch` after configuring the access key.

## 2. AmazonCrawl — PASS

- Html Agility Pack parses ordered result cards; ASIN/title normalization, title policy, duplicate selection, empty/challenge/unsupported markup and safe reason codes are isolated from browser and Book code.
- Input is normalized and bounded at 30 phrases / 200 graphemes; fetch is bounded at 20 seconds / 5 MiB and crawl at 10 minutes.
- Targeted Core Amazon tests: 13 passed. Targeted parser/browser-boundary tests: 8 passed.

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
- Full .NET result: Core 339, Infrastructure 427, Desktop 168, Updater 55, UpdateSecurity 43, ReleaseTool 32; total 1,064 passed, 0 failed.
- Restore/build succeeds with 0 warnings and 0 errors. NuGet vulnerable-package scan reports none, including transitive dependencies.
- Updater backs up/replaces controlled `.playwright` and preserves `.cloakbrowser`; package validators and release verifier require both the driver package and Windows Node executable.

## Remaining external checkpoint

Run the opt-in live smoke with a configured CloakBrowser access key and a real Amazon session. This is intentionally outside deterministic CI because it downloads an external browser binary and depends on Amazon challenge/rate-limit state.
