param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src/PrintableBook.Desktop/PrintableBook.Desktop.csproj"
$systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeRoot = Join-Path $systemTemp ("PrintableBook-package-smoke-" + [Guid]::NewGuid().ToString("N"))
$publishDirectory = Join-Path $smokeRoot "publish"
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

try {
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
    if ($LASTEXITCODE -ne 0) { throw "Unsigned Desktop publish failed." }

    foreach ($required in @("PrintableBook.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/css/book-workspace.css", "Metadata/cover_key.txt", "Metadata/interior_key.txt", ".playwright/package/package.json", ".playwright/node/win32_x64/node.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $required) -PathType Leaf)) {
            throw "Unsigned package is missing '$required'."
        }
    }
    foreach ($forbidden in @("Frontend/node_modules", "Frontend/package.json", "Frontend/package-lock.json", "Frontend/tailwind.config.js", "Frontend/test-ui.mjs", "Frontend/test-production-ui.mjs", "Frontend/verify-css.mjs", "Frontend/css/input.css")) {
        if (Test-Path -LiteralPath (Join-Path $publishDirectory $forbidden)) {
            throw "Unsigned package contains forbidden development content '$forbidden'."
        }
    }
    Write-Host "Unsigned Desktop package smoke passed."
}
finally {
    $resolvedSmokeRoot = [IO.Path]::GetFullPath($smokeRoot)
    if (-not $resolvedSmokeRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove smoke directory outside the system temp folder."
    }
    if (Test-Path -LiteralPath $resolvedSmokeRoot) {
        Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
    }
}
