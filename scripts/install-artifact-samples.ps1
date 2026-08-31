[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ArtifactRoot,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$sampleRoot = Join-Path $repoRoot ".booksample"

if (-not (Test-Path -LiteralPath $sampleRoot -PathType Container)) {
    throw "Sample data was not found at '$sampleRoot'. Add the local .booksample folder before preparing an artifact test."
}

if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
    throw "Artifact root '$ArtifactRoot' does not exist. Publish or extract the artifact before installing samples."
}

$resolvedArtifactRoot = (Resolve-Path -LiteralPath $ArtifactRoot).Path
foreach ($folderName in @("brands", "sources")) {
    $source = Join-Path $sampleRoot $folderName
    $destination = Join-Path $resolvedArtifactRoot $folderName

    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Sample folder '$source' is required."
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($sampleItem in Get-ChildItem -LiteralPath $source -Force) {
        $sampleDestination = Join-Path $destination $sampleItem.Name
        if ((Test-Path -LiteralPath $sampleDestination) -and -not $Force) {
            Write-Warning "Skipped '$sampleDestination' because it already exists. Use -Force to overwrite this sample."
            continue
        }

        Copy-Item -LiteralPath $sampleItem.FullName -Destination $sampleDestination -Recurse -Force
        Write-Host "Installed sample '$($sampleItem.Name)' into '$destination'."
    }
}

Write-Host "Artifact samples are ready. Run PrintableBook.exe from '$resolvedArtifactRoot' and use Refresh in the Book Library."
