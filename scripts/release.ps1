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
