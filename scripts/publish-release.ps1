param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src/PrintableBook.Desktop/PrintableBook.Desktop.csproj"
$version = (& dotnet msbuild $project -nologo -getProperty:Version).Trim()

if ($LASTEXITCODE -ne 0) {
    throw "Could not read Desktop project version."
}

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Desktop project Version is empty."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $version -ne $ExpectedVersion) {
    throw "Project version '$version' does not match expected '$ExpectedVersion'."
}

$releaseRoot = Join-Path $repoRoot "artifacts/release"
$packageName = "PrintableBook-$version-$RuntimeIdentifier"
$publishDirectory = Join-Path $releaseRoot "_publish"
$packageDirectory = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$hashPath = "$zipPath.sha256"

foreach ($path in @($publishDirectory, $packageDirectory)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

foreach ($path in @($zipPath, $hashPath)) {
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

dotnet publish $project `
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
    --output $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

# WebView2 package reference documentation is not required at runtime and is
# emitted alongside the bundle by the package targets. Remove only these known
# documentation files before enforcing the release-root contract below.
Get-ChildItem `
    -LiteralPath $publishDirectory `
    -File `
    -Filter "Microsoft.Web.WebView2*.xml" |
    Remove-Item -Force

# Frontend development tooling is copied by the Desktop project's broad content
# glob for normal developer builds. It is not part of the physical frontend
# runtime contract, so remove it only from the release publish output.
$releaseOnlyFrontendExclusions = @(
    "Frontend/node_modules",
    "Frontend/package-lock.json",
    "Frontend/package.json",
    "Frontend/tailwind.config.js",
    "Frontend/test-production-ui.mjs",
    "Frontend/test-ui.mjs"
)

foreach ($relativePath in $releaseOnlyFrontendExclusions) {
    $fullPath = Join-Path $publishDirectory $relativePath
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

$requiredFiles = @(
    "PrintableBook.exe",
    "Frontend/index.html",
    "Frontend/js/app.js",
    "Frontend/assets/printable-book-logo.png"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Published artifact is missing '$relativePath'."
    }
}

$requiredFrontendDirectories = @(
    "Frontend/css",
    "Frontend/js",
    "Frontend/assets"
)

foreach ($relativePath in $requiredFrontendDirectories) {
    $fullPath = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw "Published artifact is missing frontend directory '$relativePath'."
    }
}

$externalAssetsDirectory = Join-Path $publishDirectory "Assets"
if (Test-Path -LiteralPath $externalAssetsDirectory) {
    throw "Single-file release must not contain an external Assets directory."
}

$externalBinaryPatterns = @(
    "*.dll",
    "*.pdb",
    "*.deps.json",
    "*.runtimeconfig.json"
)

$externalBinaries = foreach ($pattern in $externalBinaryPatterns) {
    Get-ChildItem -LiteralPath $publishDirectory -File -Filter $pattern
}

if ($externalBinaries) {
    $binaryNames = $externalBinaries.Name -join ", "
    throw "Single-file release leaked external binary/runtime files: $binaryNames"
}

$allowedRootEntries = @(
    "PrintableBook.exe",
    "Frontend"
)

$unexpectedRootEntries = Get-ChildItem -LiteralPath $publishDirectory |
    Where-Object { $_.Name -notin $allowedRootEntries }

if ($unexpectedRootEntries) {
    $names = $unexpectedRootEntries.Name -join ", "
    throw "Published artifact contains unexpected root entries: $names"
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item (Join-Path $publishDirectory "*") $packageDirectory -Recurse -Force
Compress-Archive -Path $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal

$hash = Get-FileHash $zipPath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zipPath))" |
    Set-Content $hashPath -Encoding ascii

Write-Host "Version: $version"
Write-Host "Package: $packageDirectory"
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $hashPath"
