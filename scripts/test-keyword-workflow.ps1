param(
    [switch]$Fast,
    [switch]$Full
)

$ErrorActionPreference = "Stop"
if ($Fast -and $Full) { throw "Choose either -Fast or -Full." }
if (-not $Fast -and -not $Full) { $Fast = $true }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$frontend = Join-Path $repoRoot "src/PrintableBook.Desktop/Frontend"

function Assert-LastExit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath (Join-Path $frontend "node_modules/tailwindcss/lib/cli.js") -PathType Leaf)) {
        npm --prefix $frontend ci
        Assert-LastExit "Frontend dependency restore"
    }
    if ($Full) {
        dotnet restore PrintableBook.sln
        Assert-LastExit "Restore"
        dotnet build PrintableBook.sln --configuration Release --no-restore
        Assert-LastExit "Release build"

        $testProjects = @(
            "tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj",
            "tests/PrintableBook.UpdateSecurity.Tests/PrintableBook.UpdateSecurity.Tests.csproj",
            "tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj",
            "tests/PrintableBook.Updater.Tests/PrintableBook.Updater.Tests.csproj",
            "tests/PrintableBook.ReleaseTool.Tests/PrintableBook.ReleaseTool.Tests.csproj",
            "tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj"
        )
        foreach ($project in $testProjects) {
            $arguments = @("test", $project, "--configuration", "Release", "--no-build")
            if ($project -like "*Infrastructure.Tests*") {
                $arguments += @("--filter", "TestScope!=LocalCorpus&TestScope!=ExternalCloakBrowser&TestScope!=CapturedAmazonHtml")
            }
            & dotnet @arguments
            Assert-LastExit "Tests for $project"
        }
        & (Join-Path $repoRoot "tests/ReleaseScripts/release-orchestrator.test.ps1")
        Assert-LastExit "Release orchestrator tests"
        & (Join-Path $repoRoot "scripts/test-desktop-package.ps1") -Configuration Release -RuntimeIdentifier win-x64
        Assert-LastExit "Unsigned Desktop package smoke"
    }
    else {
        dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --no-restore
        Assert-LastExit "Core tests"
        dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --no-restore
        Assert-LastExit "Desktop tests"
    }

    node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
    Assert-LastExit "Frontend bridge tests"
    node src/PrintableBook.Desktop/Frontend/test-ui.mjs
    Assert-LastExit "Frontend UI contracts"
    node src/PrintableBook.Desktop/Frontend/test-production-ui.mjs
    Assert-LastExit "Production UI contracts"
    npm --prefix $frontend run verify:css
    Assert-LastExit "CSS verification"
    git diff --check
    Assert-LastExit "Whitespace verification"
}
finally {
    Pop-Location
}
