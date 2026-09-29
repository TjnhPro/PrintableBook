param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj"
$env:PRINTABLEBOOK_RUN_CLOAKBROWSER_TESTS = "true"

dotnet test $project `
    --configuration $Configuration `
    --filter "TestScope=ExternalCloakBrowser" `
    --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "CloakBrowser lifecycle integration tests failed." }

Write-Host "CloakBrowser lifecycle integration tests passed."
Write-Host "Cache: $(Join-Path $repoRoot 'artifacts/cloakbrowser-integration/cache')"
Write-Host "Profiles: $(Join-Path $repoRoot 'artifacts/cloakbrowser-integration/profiles')"
