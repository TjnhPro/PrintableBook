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
