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
    exit 0
}

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "rerun") { exit 0 }

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "release" -and $Arguments[1] -eq "view") {
    if ($env:FAKE_GH_RELEASE_VISIBLE -eq "false") { exit 1 }
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
    return (& git --git-dir=$Repository.Origin show "refs/heads/main:Directory.Build.props") -join "`n"
}

function Get-RemoteTagSha {
    param([Parameter(Mandatory)][hashtable]$Repository, [Parameter(Mandatory)][string]$TagName)
    $output = & git --git-dir=$Repository.Origin rev-parse "refs/tags/$TagName" 2>$null
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
Assert-Equal 0 $defaultRelease.ExitCode "Default patch release failed."
Assert-True ((Get-RemoteMainFile $defaultRepository) -match '<Version>0.2.1</Version>') "Default release did not update Version."
Assert-True ((Get-RemoteMainFile $defaultRepository) -match '<AssemblyVersion>0.2.1.0</AssemblyVersion>') "Default release did not update AssemblyVersion."
Assert-Equal (Get-RemoteTagSha $defaultRepository "v0.2.1") ((Invoke-TestGit -WorkingDirectory $defaultRepository.Work -Arguments @("rev-parse", "HEAD"))[0].Trim()) "Default tag does not point to main."
Assert-Equal "chore: release v0.2.1" ((Invoke-TestGit -WorkingDirectory $defaultRepository.Work -Arguments @("log", "-1", "--format=%s"))[0].Trim()) "Default release commit message mismatch."
Assert-True ($null -eq (Get-RemoteTagSha $defaultRepository "v0.2.2")) "Default release created an unexpected next tag."

$explicitRepository = New-ReleaseTestRepository -Root (New-TestRoot)
$explicitRelease = Invoke-TestRelease -Repository $explicitRepository -Version "0.3.0"
Assert-Equal 0 $explicitRelease.ExitCode "Explicit release failed."
Assert-True ((Get-RemoteMainFile $explicitRepository) -match '<Version>0.3.0</Version>') "Explicit release did not update Version."
Assert-Equal $null (Get-RemoteTagSha $explicitRepository "v0.2.1") "Explicit release created default patch tag."
Assert-True ($null -ne (Get-RemoteTagSha $explicitRepository "v0.3.0")) "Explicit release tag was not pushed."
Assert-Equal "chore: release v0.3.0" ((Invoke-TestGit -WorkingDirectory $explicitRepository.Work -Arguments @("log", "-1", "--format=%s"))[0].Trim()) "Explicit release commit message mismatch."
