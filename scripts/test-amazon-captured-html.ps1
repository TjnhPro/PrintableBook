param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$fixture = Join-Path $repoRoot "docs/screenshots/keyword.html"
$project = Join-Path $repoRoot "tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj"

if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) {
    throw "Captured Amazon HTML is missing: $fixture"
}

$env:PRINTABLEBOOK_RUN_CAPTURED_AMAZON_HTML_TESTS = "true"
dotnet test $project `
    --configuration $Configuration `
    --filter "TestScope=CapturedAmazonHtml" `
    --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "Captured Amazon HTML parser test failed." }

Write-Host "Captured Amazon HTML parser test passed."
Write-Host "Fixture: $fixture"
