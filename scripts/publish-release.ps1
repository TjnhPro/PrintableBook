param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$desktopProject = Join-Path $repoRoot "src/PrintableBook.Desktop/PrintableBook.Desktop.csproj"
$updaterProject = Join-Path $repoRoot "src/PrintableBook.Updater/PrintableBook.Updater.csproj"
$version = (& dotnet msbuild $desktopProject -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) { throw "Could not read Desktop project version." }
$updaterVersion = (& dotnet msbuild $updaterProject -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($updaterVersion)) { throw "Could not read Updater project version." }
if ($updaterVersion -ne $version) { throw "Desktop version '$version' does not match Updater version '$updaterVersion'." }
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $version -ne $ExpectedVersion) { throw "Project version '$version' does not match expected '$ExpectedVersion'." }
if ([string]::IsNullOrWhiteSpace($env:PRINTABLEBOOK_UPDATE_SIGNING_PRIVATE_KEY)) { throw "PRINTABLEBOOK_UPDATE_SIGNING_PRIVATE_KEY is required to publish a signed release." }

$releaseRoot = Join-Path $repoRoot "artifacts/release"
$packageName = "PrintableBook-$version-$RuntimeIdentifier"
$desktopPublishDirectory = Join-Path $releaseRoot "_desktop-publish"
$updaterPublishDirectory = Join-Path $releaseRoot "_updater-publish"
$packageDirectory = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$hashPath = "$zipPath.sha256"
$manifestPath = Join-Path $releaseRoot "$packageName.manifest.json"
$signaturePath = "$manifestPath.sig"

foreach ($path in @($desktopPublishDirectory, $updaterPublishDirectory, $packageDirectory)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
foreach ($path in @($zipPath, $hashPath, $manifestPath, $signaturePath)) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

dotnet publish $desktopProject `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:WebView2LoaderPreference=Static `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:EnableCompressionInSingleFile=false `
    -p:CopyDocumentationFilesFromPackages=false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    --output $desktopPublishDirectory
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }

Get-ChildItem -LiteralPath $desktopPublishDirectory -File -Filter "Microsoft.Web.WebView2*.xml" | Remove-Item -Force
foreach ($relativePath in @("Frontend/node_modules", "Frontend/package-lock.json", "Frontend/package.json", "Frontend/tailwind.config.js", "Frontend/test-production-ui.mjs", "Frontend/test-ui.mjs")) {
    $fullPath = Join-Path $desktopPublishDirectory $relativePath
    if (Test-Path -LiteralPath $fullPath) { Remove-Item -LiteralPath $fullPath -Recurse -Force }
}

foreach ($relativePath in @("PrintableBook.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/assets/printable-book-logo.png")) {
    if (-not (Test-Path -LiteralPath (Join-Path $desktopPublishDirectory $relativePath))) { throw "Published Desktop artifact is missing '$relativePath'." }
}
foreach ($relativePath in @("Frontend/css", "Frontend/js", "Frontend/assets")) {
    if (-not (Test-Path -LiteralPath (Join-Path $desktopPublishDirectory $relativePath) -PathType Container)) { throw "Published Desktop artifact is missing frontend directory '$relativePath'." }
}
if (Test-Path -LiteralPath (Join-Path $desktopPublishDirectory "Assets")) { throw "Single-file Desktop release must not contain an external Assets directory." }
$externalBinaryPatterns = @("*.dll", "*.pdb", "*.deps.json", "*.runtimeconfig.json")
$desktopExternalBinaries = foreach ($pattern in $externalBinaryPatterns) { Get-ChildItem -LiteralPath $desktopPublishDirectory -File -Filter $pattern }
if ($desktopExternalBinaries) { throw "Single-file Desktop release leaked external binary/runtime files: $($desktopExternalBinaries.Name -join ', ')" }
$desktopUnexpected = Get-ChildItem -LiteralPath $desktopPublishDirectory | Where-Object { $_.Name -notin @("PrintableBook.exe", "Frontend") }
if ($desktopUnexpected) { throw "Published Desktop artifact contains unexpected root entries: $($desktopUnexpected.Name -join ', ')" }

dotnet publish $updaterProject `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:EnableCompressionInSingleFile=false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    --output $updaterPublishDirectory
if ($LASTEXITCODE -ne 0) { throw "Updater publish failed." }
if (-not (Test-Path -LiteralPath (Join-Path $updaterPublishDirectory "PrintableBook.Updater.exe") -PathType Leaf)) { throw "Published Updater artifact is missing PrintableBook.Updater.exe." }
$updaterExternalBinaries = foreach ($pattern in $externalBinaryPatterns) { Get-ChildItem -LiteralPath $updaterPublishDirectory -File -Filter $pattern }
if ($updaterExternalBinaries) { throw "Single-file Updater release leaked external binary/runtime files: $($updaterExternalBinaries.Name -join ', ')" }
$updaterUnexpected = Get-ChildItem -LiteralPath $updaterPublishDirectory | Where-Object { $_.Name -ne "PrintableBook.Updater.exe" }
if ($updaterUnexpected) { throw "Published Updater artifact contains unexpected root entries: $($updaterUnexpected.Name -join ', ')" }

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item (Join-Path $desktopPublishDirectory "*") $packageDirectory -Recurse -Force
Copy-Item (Join-Path $updaterPublishDirectory "PrintableBook.Updater.exe") (Join-Path $packageDirectory "PrintableBook.Updater.exe") -Force
$allowedPackageRootEntries = @("PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend")
$unexpectedPackageEntries = Get-ChildItem -LiteralPath $packageDirectory | Where-Object { $_.Name -notin $allowedPackageRootEntries }
if ($unexpectedPackageEntries) { throw "Release package contains unexpected root entries: $($unexpectedPackageEntries.Name -join ', ')" }
foreach ($forbidden in @("brands", "sources", "settings.json", ".workspace", "Output")) {
    if (Test-Path -LiteralPath (Join-Path $packageDirectory $forbidden)) { throw "Release package must not contain '$forbidden'." }
}

Compress-Archive -Path $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash $zipPath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zipPath))" | Set-Content $hashPath -Encoding ascii

$releaseToolProject = Join-Path $repoRoot "tools/PrintableBook.ReleaseTool/PrintableBook.ReleaseTool.csproj"
dotnet run --project $releaseToolProject --configuration $Configuration -- sign-release --release-root $releaseRoot --version $version --runtime $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) { throw "Release signing failed." }
dotnet run --project $releaseToolProject --configuration $Configuration -- verify-release --release-root $releaseRoot --version $version --runtime $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) { throw "Release verification failed." }
foreach ($path in @($zipPath, $hashPath, $manifestPath, $signaturePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected release asset is missing: $path" }
}

Write-Host "Version: $version"
Write-Host "Package: $packageDirectory"
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $hashPath"
Write-Host "Manifest: $manifestPath"
Write-Host "Signature: $signaturePath"
