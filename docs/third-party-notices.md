# Third-party components

## Amazon ASIN Research

- **CloakBrowser .NET wrapper 0.5.11** — MIT-licensed wrapper, distributed through NuGet. Its patched Chromium binary is not bundled by Printable Book; CloakBrowser downloads and verifies it at runtime under `.cloakbrowser/cache`. Access keys, binary availability and usage remain subject to CloakBrowser's current service/binary terms.
- **Microsoft.Playwright 1.49.0** — Apache-2.0. Its driver runtime is shipped in `.playwright/` because the CloakBrowser .NET wrapper depends on it.
- **Html Agility Pack 1.13.0** — MIT, used only to parse returned Amazon HTML in C#.

Printable Book does not include Amazon cookies, profile data, downloaded Chromium builds or a CloakBrowser license key in release artifacts.
