[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-StrictReleaseVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    if ($Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        throw "Release version '$Value' must match M.m.p."
    }

    $parsed = [Version]$Value

    if ($parsed.Revision -ge 0) {
        throw "Release version '$Value' must have exactly three components."
    }

    return $parsed
}

function Get-NextPatchVersion {
    param(
        [Parameter(Mandatory)]
        [Version]$Current
    )

    return [Version]::new($Current.Major, $Current.Minor, $Current.Build + 1)
}

function Resolve-TargetVersion {
    param(
        [Parameter(Mandatory)]
        [Version]$CurrentVersion,

        [string]$RequestedVersion = ""
    )

    if ([string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return Get-NextPatchVersion $CurrentVersion
    }

    $target = ConvertTo-StrictReleaseVersion $RequestedVersion
    if ($target -le $CurrentVersion) {
        throw "Target version $($target.ToString(3)) must be greater than current version $($CurrentVersion.ToString(3))."
    }

    return $target
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $output = & git @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }

    return @{ ExitCode = $exitCode; Output = @($output) }
}

function Invoke-Gh {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $output = & gh @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "gh $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }

    return @{ ExitCode = $exitCode; Output = @($output) }
}

function Assert-MainBranch {
    $branch = ((Invoke-Git -Arguments @("branch", "--show-current")).Output -join "").Trim()
    if ($branch -ne "main") {
        throw "Release must run from branch 'main'. Current branch is '$branch'."
    }
}

function Assert-CleanWorkingTree {
    $status = @((Invoke-Git -Arguments @("status", "--porcelain")).Output | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_)
    })

    if ($status.Count -gt 0) {
        throw "Working tree must be clean before release."
    }
}

function Sync-Main {
    Invoke-Git -Arguments @("fetch", "origin", "--prune", "--tags") | Out-Null
    Invoke-Git -Arguments @("pull", "--ff-only", "origin", "main") | Out-Null

    $head = ((Invoke-Git -Arguments @("rev-parse", "HEAD")).Output -join "").Trim()
    $originMain = ((Invoke-Git -Arguments @("rev-parse", "origin/main")).Output -join "").Trim()
    if ($head -ne $originMain) {
        throw "Local main does not match origin/main."
    }

    return $head
}

function Assert-GreenMainCi {
    param(
        [Parameter(Mandatory)]
        [string]$HeadSha
    )

    Invoke-Gh -Arguments @("auth", "status") | Out-Null
    $result = Invoke-Gh -Arguments @(
        "run", "list", "--workflow", "build-and-test.yml", "--commit", $HeadSha,
        "--limit", "20", "--json", "headSha,status,conclusion,databaseId,url")
    $runs = @(($result.Output -join [Environment]::NewLine) | ConvertFrom-Json)
    $success = $runs | Where-Object {
        $_.headSha -eq $HeadSha -and $_.status -eq "completed" -and $_.conclusion -eq "success"
    } | Select-Object -First 1

    if ($null -eq $success) {
        throw "Build and test must be successful for main SHA $HeadSha before release."
    }

    return $success
}

function Get-SourceVersion {
    param(
        [Parameter(Mandatory)]
        [string]$DirectoryBuildPropsPath
    )

    $text = [IO.File]::ReadAllText($DirectoryBuildPropsPath)
    $match = [regex]::Match($text, '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>')

    if (-not $match.Success) {
        throw "Directory.Build.props must contain one strict Version element."
    }

    return ConvertTo-StrictReleaseVersion $match.Groups[1].Value
}

function Set-SourceVersion {
    param(
        [Parameter(Mandatory)]
        [string]$DirectoryBuildPropsPath,

        [Parameter(Mandatory)]
        [Version]$TargetVersion
    )

    $text = [IO.File]::ReadAllText($DirectoryBuildPropsPath)
    $target = $TargetVersion.ToString(3)
    $replacements = @(
        @{ Pattern = '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>'; Value = "<Version>$target</Version>" },
        @{ Pattern = '<AssemblyVersion>[0-9]+\.[0-9]+\.[0-9]+\.0</AssemblyVersion>'; Value = "<AssemblyVersion>$target.0</AssemblyVersion>" },
        @{ Pattern = '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+\.0</FileVersion>'; Value = "<FileVersion>$target.0</FileVersion>" }
    )

    foreach ($replacement in $replacements) {
        $matches = [regex]::Matches($text, $replacement.Pattern)
        if ($matches.Count -ne 1) {
            throw "Directory.Build.props does not match the expected release version contract."
        }

        $text = [regex]::Replace($text, $replacement.Pattern, $replacement.Value)
    }

    [IO.File]::WriteAllText($DirectoryBuildPropsPath, $text, [Text.UTF8Encoding]::new($false))
}

function Invoke-PrintableBookRelease {
    param(
        [string]$RequestedVersion = ""
    )

    throw "Release orchestration is not implemented yet."
}

if ($MyInvocation.InvocationName -ne ".") {
    Invoke-PrintableBookRelease -RequestedVersion $Version
}
