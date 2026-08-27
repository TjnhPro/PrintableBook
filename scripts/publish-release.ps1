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
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    --output $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$requiredFiles = @(
    "PrintableBook.exe",
    "Frontend/index.html",
    "Frontend/js/app.js",
    "Assets/app-icon-source.png"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Published artifact is missing '$relativePath'."
    }
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
