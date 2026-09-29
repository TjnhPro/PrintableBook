# CloakBrowser / Amazon crawl compatibility decision

Date: 2026-09-29

## Decision

- Wrapper: `CloakBrowser` NuGet `0.5.11`.
- HTML parser: `HtmlAgilityPack` `1.13.0`.
- Runtime: `.NET 10`, Windows x64, headed persistent context.
- Browser binary: latest Stable resolved by CloakBrowser at launch. The Chromium version is intentionally not pinned so CloakBrowser can download a newer compatible build.
- Persistent profile: `<AppRoot>/.cloakbrowser/profile-v1`.
- Download/cache: `<AppRoot>/.cloakbrowser/cache`, supplied through `CLOAKBROWSER_CACHE_DIR` before the first wrapper call.
- Browser ownership: one in-process, app-owned `CloakContextHandle`; it is reused until it disconnects or the app exits.
- Fetch: fixed JavaScript function evaluated in the Amazon page context. Neither arbitrary JavaScript nor arbitrary URLs cross the WebView bridge.

## Compatibility findings

The official .NET wrapper exposes `LaunchPersistentContextAsync(userDataDir, LaunchContextOptions)` and returns a standard Playwright-backed context. This provides the required persistent cookies/local storage, headed browser window, navigation, JavaScript evaluation, and orderly async disposal without maintaining a second CDP connection.

The app therefore uses the wrapper-owned Playwright connection rather than publishing a remote debugging endpoint. This is stricter than the draft CDP design: there is no port to scan, persist, expose, or reconnect to, and all browser processes remain owned by this application instance. Persistence across app restarts comes from the dedicated profile and binary cache, not from attaching to an orphaned process.

## Release shape

The NuGet wrapper brings its Playwright driver payload through normal publish output. Downloaded Chromium data and the user profile are runtime data under `.cloakbrowser/`; they are never added to the release archive and must not be replaced by the updater. The app must still start offline when no browser binary has been downloaded. Only opening the Amazon browser requires the first download.

## Safety constraints

- Only `https://www.amazon.com` and `https://amazon.com` are accepted.
- Redirect destinations are checked again before HTML is returned.
- HTML is capped and discarded after parsing.
- No cookies, HTML, query URLs, profile paths, license values, or internal endpoints are logged or returned to the WebView.
- A profile lock or failed first download disables only ASIN Research; it does not block application startup.

## Manual release smoke

1. Publish `win-x64` with the normal release command.
2. Start once with `.cloakbrowser/cache` absent; verify the app starts without network access.
3. Select **Open Browser** with network access; verify the binary downloads into the app-local cache and Amazon opens in a visible window.
4. Close and reopen the app; verify the existing profile and binary cache are reused.
5. Run the updater and verify `.cloakbrowser/` remains unchanged.
