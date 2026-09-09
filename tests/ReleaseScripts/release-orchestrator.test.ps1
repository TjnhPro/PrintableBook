Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$releaseScript = Join-Path $repoRoot "scripts/release.ps1"

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        $Expected,

        [Parameter(Mandatory)]
        $Actual,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected='$Expected' Actual='$Actual'."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [Parameter(Mandatory)]
        [string]$MessageContains
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$MessageContains*") {
            throw "Unexpected error '$($_.Exception.Message)'."
        }

        return
    }

    throw "Expected failure containing '$MessageContains'."
}

. $releaseScript

Assert-Equal "0.2.0" (ConvertTo-StrictReleaseVersion "0.2.0").ToString(3) "Strict version parsing failed."
Assert-Equal "0.2.1" (Get-NextPatchVersion ([Version]"0.2.0")).ToString(3) "Patch bump failed."
Assert-Equal "0.3.10" (Get-NextPatchVersion ([Version]"0.3.9")).ToString(3) "Multi-digit patch bump failed."

Assert-Throws { ConvertTo-StrictReleaseVersion "0.2" } "M.m.p"
Assert-Throws { ConvertTo-StrictReleaseVersion "v0.2.1" } "M.m.p"
Assert-Throws { ConvertTo-StrictReleaseVersion "0.2.1-beta" } "M.m.p"

Assert-Equal "0.2.1" (Resolve-TargetVersion -CurrentVersion ([Version]"0.2.0") -RequestedVersion "").ToString(3) "Empty requested version must bump patch."
Assert-Equal "0.3.0" (Resolve-TargetVersion -CurrentVersion ([Version]"0.2.0") -RequestedVersion "0.3.0").ToString(3) "Explicit version was not honored."
Assert-Throws { Resolve-TargetVersion -CurrentVersion ([Version]"0.2.0") -RequestedVersion "0.2.0" } "must be greater"
Assert-Throws { Resolve-TargetVersion -CurrentVersion ([Version]"0.2.0") -RequestedVersion "0.1.9" } "must be greater"

$propsTemplate = @'
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <Version>0.2.0</Version>
    <AssemblyVersion>0.2.0.0</AssemblyVersion>
    <FileVersion>0.2.0.0</FileVersion>
    <InformationalVersion>$(Version)</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
  </PropertyGroup>
</Project>
'@

function Invoke-TestGit {
    param(
        [Parameter(Mandatory)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & git -C $WorkingDirectory @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }

    return @($output)
}

function New-FakeGh {
    param(
        [Parameter(Mandatory)]
        [string]$Directory
    )

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $runner = Join-Path $Directory "gh.cmd"
    $implementation = Join-Path $Directory "gh.fake.ps1"

    [IO.File]::WriteAllText($runner, @'
@echo off
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0gh.fake.ps1" %*
exit /b %ERRORLEVEL%
'@, [Text.UTF8Encoding]::new($false))

    [IO.File]::WriteAllText($implementation, @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ArgumentValue {
    param([string]$Name)
    $index = [Array]::IndexOf($Arguments, $Name)
    if ($index -ge 0 -and $index -lt ($Arguments.Count - 1)) { return $Arguments[$index + 1] }
    return ""
}

function Get-CurrentSha {
    return (& git rev-parse HEAD).Trim()
}

function Get-StateValue {
    if (-not $env:FAKE_GH_STATE_FILE -or -not (Test-Path -LiteralPath $env:FAKE_GH_STATE_FILE)) { return 0 }
    $text = [IO.File]::ReadAllText($env:FAKE_GH_STATE_FILE).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return 0 }
    return [int]$text
}

function Set-StateValue {
    param([int]$Value)
    if ($env:FAKE_GH_STATE_FILE) { [IO.File]::WriteAllText($env:FAKE_GH_STATE_FILE, "$Value", [Text.UTF8Encoding]::new($false)) }
}

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "auth" -and $Arguments[1] -eq "status") { exit 0 }

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "list") {
    $workflow = Get-ArgumentValue "--workflow"
    $sha = Get-CurrentSha
    if ($workflow -eq "build-and-test.yml") {
        $conclusion = if ($env:FAKE_GH_BUILD_CONCLUSION) { $env:FAKE_GH_BUILD_CONCLUSION } else { "success" }
        @(@{ headSha = $sha; status = "completed"; conclusion = $conclusion; databaseId = 1001; url = "https://example.invalid/build/1001" }) | ConvertTo-Json -Compress
        exit 0
    }

    if ($workflow -eq "release.yml") {
        @(@{ headSha = $sha; status = "completed"; conclusion = "success"; databaseId = 2001; url = "https://example.invalid/release/2001"; createdAt = "2026-09-09T00:00:00Z" }) | ConvertTo-Json -Compress
        exit 0
    }
}

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "watch") {
    $remainingFailures = Get-StateValue
    if ($remainingFailures -gt 0) { Set-StateValue ($remainingFailures - 1); exit 1 }
    if ($env:FAKE_GH_RELEASE_STATE_FILE) { [IO.File]::WriteAllText($env:FAKE_GH_RELEASE_STATE_FILE, "visible", [Text.UTF8Encoding]::new($false)) }
    exit 0
}

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "rerun") { exit 0 }

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "release" -and $Arguments[1] -eq "view") {
    if ($env:FAKE_GH_RELEASE_VISIBLE -eq "false") { exit 1 }
    if ($env:FAKE_GH_RELEASE_VISIBLE -eq "after-watch") {
        if (-not $env:FAKE_GH_RELEASE_STATE_FILE -or -not (Test-Path -LiteralPath $env:FAKE_GH_RELEASE_STATE_FILE) -or ([IO.File]::ReadAllText($env:FAKE_GH_RELEASE_STATE_FILE).Trim() -ne "visible")) { exit 1 }
    }
    $tag = $Arguments[2]
    $version = $tag.Substring(1)
    @{ tagName = $tag; isDraft = $false; isPrerelease = $false; url = "https://example.invalid/releases/$tag"; assets = @(
        @{ name = "PrintableBook-$version-win-x64.zip" },
        @{ name = "PrintableBook-$version-win-x64.zip.sha256" },
        @{ name = "PrintableBook-$version-win-x64.manifest.json" },
        @{ name = "PrintableBook-$version-win-x64.manifest.json.sig" }
    ) } | ConvertTo-Json -Compress -Depth 4
    exit 0
}

throw "Unsupported fake gh invocation: $($Arguments -join ' ')"
'@, [Text.UTF8Encoding]::new($false))

    return $Directory
}

function New-ReleaseTestRepository {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $origin = Join-Path $Root "origin.git"
    $work = Join-Path $Root "work"
    $fakeBin = New-FakeGh -Directory (Join-Path $Root "fake-bin")
    & git init --bare $origin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create bare test origin." }
    & git init -b main $work | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create test worktree." }

    Push-Location $work
    try {
        & git config user.name "Release Script Test"
        & git config user.email "release-test@example.invalid"
        New-Item -ItemType Directory -Path "scripts" -Force | Out-Null
        Copy-Item $releaseScript "scripts/release.ps1"
        [IO.File]::WriteAllText((Join-Path $work "Directory.Build.props"), $propsTemplate, [Text.UTF8Encoding]::new($false))
        & git add .
        & git commit -m "test: initial main" | Out-Null
        & git remote add origin $origin
        & git push -u origin main | Out-Null
    }
    finally {
        Pop-Location
    }

    return @{ Origin = $origin; Work = $work; FakeBin = $fakeBin }
}

function Invoke-TestRelease {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Repository,

        [string]$Version = "",

        [hashtable]$Environment = @{}
    )

    $oldPath = $env:PATH
    $oldValues = @{}
    foreach ($item in $Environment.GetEnumerator()) {
        $oldValues[$item.Key] = [Environment]::GetEnvironmentVariable($item.Key, "Process")
        Set-Item -Path "Env:$($item.Key)" -Value ([string]$item.Value)
    }

    try {
        $env:PATH = "$($Repository.FakeBin);$oldPath"
        Push-Location $Repository.Work
        $arguments = @("-NoProfile", "-File", "scripts/release.ps1")
        if (-not [string]::IsNullOrWhiteSpace($Version)) { $arguments += @("-Version", $Version) }
        $output = & pwsh @arguments 2>&1
        return @{ ExitCode = $LASTEXITCODE; Output = @($output) }
    }
    finally {
        Pop-Location
        $env:PATH = $oldPath
        foreach ($item in $Environment.GetEnumerator()) {
            if ($null -eq $oldValues[$item.Key]) { Remove-Item -Path "Env:$($item.Key)" -ErrorAction SilentlyContinue }
            else { Set-Item -Path "Env:$($item.Key)" -Value $oldValues[$item.Key] }
        }
    }
}

function Get-RemoteMainFile {
    param([Parameter(Mandatory)][hashtable]$Repository)
    return (& git "--git-dir=$($Repository.Origin)" show "refs/heads/main:Directory.Build.props") -join "`n"
}

function Get-RemoteTagSha {
    param([Parameter(Mandatory)][hashtable]$Repository, [Parameter(Mandatory)][string]$TagName)
    $output = & git "--git-dir=$($Repository.Origin)" rev-parse "refs/tags/$TagName" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($output -join "").Trim()
}

function New-TestRoot {
    $root = Join-Path $env:TEMP ("PrintableBook-release-script-test-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root | Out-Null
    return $root
}

$defaultRepository = New-ReleaseTestRepository -Root (New-TestRoot)
$defaultRelease = Invoke-TestRelease -Repository $defaultRepository
if ($defaultRelease.ExitCode -ne 0) {
    throw "Default patch release failed: $($defaultRelease.Output -join [Environment]::NewLine)"
}
Assert-True ((Get-RemoteMainFile $defaultRepository) -match '<Version>0.2.1</Version>') "Default release did not update Version."
Assert-True ((Get-RemoteMainFile $defaultRepository) -match '<AssemblyVersion>0.2.1.0</AssemblyVersion>') "Default release did not update AssemblyVersion."
Assert-Equal (Get-RemoteTagSha $defaultRepository "v0.2.1") ((Invoke-TestGit -WorkingDirectory $defaultRepository.Work -Arguments @("rev-parse", "HEAD") | Select-Object -First 1).Trim()) "Default tag does not point to main."
Assert-Equal "chore: release v0.2.1" ((Invoke-TestGit -WorkingDirectory $defaultRepository.Work -Arguments @("log", "-1", "--format=%s") | Select-Object -First 1).Trim()) "Default release commit message mismatch."
Assert-True ($null -eq (Get-RemoteTagSha $defaultRepository "v0.2.2")) "Default release created an unexpected next tag."

$explicitRepository = New-ReleaseTestRepository -Root (New-TestRoot)
$explicitRelease = Invoke-TestRelease -Repository $explicitRepository -Version "0.3.0"
Assert-Equal 0 $explicitRelease.ExitCode "Explicit release failed."
Assert-True ((Get-RemoteMainFile $explicitRepository) -match '<Version>0.3.0</Version>') "Explicit release did not update Version."
Assert-True ($null -eq (Get-RemoteTagSha $explicitRepository "v0.2.1")) "Explicit release created default patch tag."
Assert-True ($null -ne (Get-RemoteTagSha $explicitRepository "v0.3.0")) "Explicit release tag was not pushed."
Assert-Equal "chore: release v0.3.0" ((Invoke-TestGit -WorkingDirectory $explicitRepository.Work -Arguments @("log", "-1", "--format=%s") | Select-Object -First 1).Trim()) "Explicit release commit message mismatch."

$resumeRepository = New-ReleaseTestRepository -Root (New-TestRoot)
$watchState = Join-Path (Split-Path $resumeRepository.Work -Parent) "watch-state.txt"
$releaseState = Join-Path (Split-Path $resumeRepository.Work -Parent) "release-state.txt"
[IO.File]::WriteAllText($watchState, "2", [Text.UTF8Encoding]::new($false))
$failedPublication = Invoke-TestRelease -Repository $resumeRepository -Environment @{
    FAKE_GH_STATE_FILE = $watchState
    FAKE_GH_RELEASE_STATE_FILE = $releaseState
    FAKE_GH_RELEASE_VISIBLE = "after-watch"
}
Assert-True ($failedPublication.ExitCode -ne 0) "Failed publication must exit non-zero."
Assert-True ((Get-RemoteMainFile $resumeRepository) -match '<Version>0.2.1</Version>') "Failed publication did not preserve pushed target version."
Assert-True ($null -ne (Get-RemoteTagSha $resumeRepository "v0.2.1")) "Failed publication did not preserve pushed tag."
Assert-True ($null -eq (Get-RemoteTagSha $resumeRepository "v0.2.2")) "Failed publication created a next patch tag."

$resumedPublication = Invoke-TestRelease -Repository $resumeRepository -Environment @{
    FAKE_GH_STATE_FILE = $watchState
    FAKE_GH_RELEASE_STATE_FILE = $releaseState
    FAKE_GH_RELEASE_VISIBLE = "after-watch"
}
Assert-Equal 0 $resumedPublication.ExitCode "Existing tagged release did not resume."
Assert-True ($null -eq (Get-RemoteTagSha $resumeRepository "v0.2.2")) "Resume created a next patch tag."
Assert-Equal "chore: release v0.2.1" ((Invoke-TestGit -WorkingDirectory $resumeRepository.Work -Arguments @("log", "-1", "--format=%s") | Select-Object -First 1).Trim()) "Resume created a second version commit."

$dirtyRepository = New-ReleaseTestRepository -Root (New-TestRoot)
[IO.File]::WriteAllText((Join-Path $dirtyRepository.Work "untracked.txt"), "dirty", [Text.UTF8Encoding]::new($false))
$dirtyResult = Invoke-TestRelease -Repository $dirtyRepository
Assert-True ($dirtyResult.ExitCode -ne 0) "Dirty working tree was accepted."
Assert-True ((Get-RemoteMainFile $dirtyRepository) -match '<Version>0.2.0</Version>') "Dirty working tree changed source version."
Assert-True ($null -eq (Get-RemoteTagSha $dirtyRepository "v0.2.1")) "Dirty working tree created a tag."

$branchRepository = New-ReleaseTestRepository -Root (New-TestRoot)
Invoke-TestGit -WorkingDirectory $branchRepository.Work -Arguments @("checkout", "-b", "feature/test") | Out-Null
$branchResult = Invoke-TestRelease -Repository $branchRepository
Assert-True ($branchResult.ExitCode -ne 0) "Non-main branch was accepted."
Assert-True (($branchResult.Output -join "`n") -like "*branch 'main'*") "Non-main branch error was unclear."
Assert-True ($null -eq (Get-RemoteTagSha $branchRepository "v0.2.1")) "Non-main branch created a tag."

$ciRepository = New-ReleaseTestRepository -Root (New-TestRoot)
$ciResult = Invoke-TestRelease -Repository $ciRepository -Environment @{ FAKE_GH_BUILD_CONCLUSION = "failure" }
Assert-True ($ciResult.ExitCode -ne 0) "Failed main CI was accepted."
Assert-True ((Get-RemoteMainFile $ciRepository) -match '<Version>0.2.0</Version>') "Failed main CI changed source version."
Assert-True ($null -eq (Get-RemoteTagSha $ciRepository "v0.2.1")) "Failed main CI created a tag."

foreach ($invalidVersion in @("0.2.0", "0.1.9")) {
    $invalidRepository = New-ReleaseTestRepository -Root (New-TestRoot)
    $invalidResult = Invoke-TestRelease -Repository $invalidRepository -Version $invalidVersion
    Assert-True ($invalidResult.ExitCode -ne 0) "Non-increasing version $invalidVersion was accepted."
    Assert-True ($null -eq (Get-RemoteTagSha $invalidRepository "v0.2.1")) "Non-increasing version $invalidVersion created a tag."
}

$existingTagRepository = New-ReleaseTestRepository -Root (New-TestRoot)
Invoke-TestGit -WorkingDirectory $existingTagRepository.Work -Arguments @("tag", "v0.2.1") | Out-Null
Invoke-TestGit -WorkingDirectory $existingTagRepository.Work -Arguments @("push", "origin", "refs/tags/v0.2.1") | Out-Null
$existingTagSha = Get-RemoteTagSha $existingTagRepository "v0.2.1"
$existingTagResult = Invoke-TestRelease -Repository $existingTagRepository
Assert-True ($existingTagResult.ExitCode -ne 0) "Existing target tag was accepted."
Assert-Equal $existingTagSha (Get-RemoteTagSha $existingTagRepository "v0.2.1") "Existing target tag was moved."

$malformedRepository = New-ReleaseTestRepository -Root (New-TestRoot)
$malformedPath = Join-Path $malformedRepository.Work "Directory.Build.props"
[IO.File]::WriteAllText($malformedPath, (([IO.File]::ReadAllText($malformedPath)).Replace('<Version>0.2.0</Version>', '<Version>0.2</Version>')), [Text.UTF8Encoding]::new($false))
Invoke-TestGit -WorkingDirectory $malformedRepository.Work -Arguments @("add", "Directory.Build.props") | Out-Null
Invoke-TestGit -WorkingDirectory $malformedRepository.Work -Arguments @("commit", "-m", "test: malformed version") | Out-Null
Invoke-TestGit -WorkingDirectory $malformedRepository.Work -Arguments @("push", "origin", "main") | Out-Null
$malformedResult = Invoke-TestRelease -Repository $malformedRepository
Assert-True ($malformedResult.ExitCode -ne 0) "Malformed source version was accepted."
Assert-True ($null -eq (Get-RemoteTagSha $malformedRepository "v0.2.1")) "Malformed source version created a tag."

$releaseWorkflow = Get-Content (Join-Path $repoRoot ".github/workflows/release.yml") -Raw
foreach ($requiredWorkflowText in @(
    "./scripts/publish-release.ps1",
    "PRINTABLEBOOK_UPDATE_SIGNING_PRIVATE_KEY",
    "softprops/action-gh-release@v2",
    "fail_on_unmatched_files: true"
)) {
    Assert-True ($releaseWorkflow.Contains($requiredWorkflowText)) "Release workflow is missing '$requiredWorkflowText'."
}

foreach ($forbiddenWorkflowText in @(
    "dotnet test tests/PrintableBook.Core.Tests",
    "node --test tests/PrintableBook.Desktop.Bridge.Tests",
    "test-production-ui.mjs"
)) {
    Assert-True (-not $releaseWorkflow.Contains($forbiddenWorkflowText)) "Release workflow still runs redundant test '$forbiddenWorkflowText'."
}

$candidateWorkflow = Get-Content (Join-Path $repoRoot ".github/workflows/release-candidate.yml") -Raw
Assert-True ($candidateWorkflow.Contains("workflow_dispatch")) "Release candidate workflow must remain available."

Write-Output "all release orchestrator tests passed"
