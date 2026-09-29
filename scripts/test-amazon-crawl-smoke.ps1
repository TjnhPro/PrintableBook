param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$required = @(
    "PrintableBook.exe",
    "Frontend/index.html",
    "Frontend/js/app.js",
    ".playwright/package/package.json",
    ".playwright/node/win32_x64/node.exe"
)
foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relativePath) -PathType Leaf)) {
        throw "Amazon crawl smoke prerequisite is missing: $relativePath"
    }
}
if (Test-Path -LiteralPath (Join-Path $root ".cloakbrowser")) {
    Write-Host "Existing .cloakbrowser runtime data will be reused."
} else {
    Write-Host "First Open Browser will download Chromium (~200 MB) into .cloakbrowser/cache."
}
if ([string]::IsNullOrWhiteSpace($env:CLOAKBROWSER_LICENSE_KEY) -and -not (Test-Path -LiteralPath (Join-Path $env:USERPROFILE ".cloakbrowser/license.key") -PathType Leaf)) {
    Write-Warning "No CloakBrowser access key was detected. Configure CLOAKBROWSER_LICENSE_KEY to use the latest free binary before the live smoke."
}

Write-Host "Manual checks:"
Write-Host "1. Open a Book and confirm ASIN Research is seeded from Book Keywords."
Write-Host "2. Open Browser; confirm Amazon Cart opens in a visible dedicated profile."
Write-Host "3. Crawl two phrases; verify ordered unique ASIN rows and Copy ASINs."
Write-Host "4. Use in Ads ASIN; verify the Book remains unsaved until Build & Save."
Write-Host "5. Restart the app; verify the browser cache/profile is reused."

if ($Launch) {
    Start-Process -FilePath (Join-Path $root "PrintableBook.exe") -WorkingDirectory $root
}
