<#
.SYNOPSIS
Safely removes stale GitHub branches and their same-name local branches.

.DESCRIPTION
Default mode is dry-run.

A remote branch is eligible only when:
- it is not main;
- it is not the currently checked-out branch;
- it is behind origin/main;
- its full history is already contained in origin/main;
- if a same-name local branch exists, that local branch is also behind
  and fully contained in origin/main.

Deletion re-checks branch SHAs before acting. Remote deletion uses
--force-with-lease so a branch that changed on GitHub after the scan
will not be deleted.

.EXAMPLE
.\scripts\clear-branches.ps1

.EXAMPLE
.\scripts\clear-branches.ps1 -Delete
#>

[CmdletBinding()]
param(
    [switch]$Delete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Remote = "origin"
$BaseBranch = "main"
$BaseRef = "$Remote/$BaseBranch"

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

    return @{
        ExitCode = $exitCode
        Output   = @($output)
    }
}

function Get-GitLine {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    return ((Invoke-Git -Arguments $Arguments).Output -join "").Trim()
}

function Test-RefExists {
    param(
        [Parameter(Mandatory)]
        [string]$Ref
    )

    return (Invoke-Git -Arguments @(
        "show-ref", "--verify", "--quiet", $Ref
    ) -AllowFailure).ExitCode -eq 0
}

function Test-IsAncestor {
    param(
        [Parameter(Mandatory)]
        [string]$Ancestor,

        [Parameter(Mandatory)]
        [string]$Descendant
    )

    $result = Invoke-Git -Arguments @(
        "merge-base", "--is-ancestor", $Ancestor, $Descendant
    ) -AllowFailure

    if ($result.ExitCode -eq 0) {
        return $true
    }

    if ($result.ExitCode -eq 1) {
        return $false
    }

    throw "Could not compare '$Ancestor' with '$Descendant'."
}

function Get-Divergence {
    param(
        [Parameter(Mandatory)]
        [string]$BranchRef
    )

    $raw = Get-GitLine -Arguments @(
        "rev-list",
        "--left-right",
        "--count",
        "$BaseRef...$BranchRef"
    )

    $parts = $raw -split "\s+"
    if ($parts.Count -ne 2) {
        throw "Could not parse ahead/behind for '$BranchRef': '$raw'."
    }

    return @{
        Behind = [int]$parts[0]
        Ahead  = [int]$parts[1]
    }
}

function Get-RefSha {
    param(
        [Parameter(Mandatory)]
        [string]$Ref
    )

    return (Get-GitLine -Arguments @("rev-parse", "--verify", $Ref))
}

function Write-BranchStatus {
    param(
        [Parameter(Mandatory)]
        [string]$Status,

        [Parameter(Mandatory)]
        [string]$Branch,

        [Parameter(Mandatory)]
        [int]$Behind,

        [Parameter(Mandatory)]
        [int]$Ahead,

        [Parameter(Mandatory)]
        [string]$Reason
    )

    Write-Host (
        "{0,-8} {1,-48} behind {2,-4} ahead {3,-4} {4}" -f
        $Status, $Branch, $Behind, $Ahead, $Reason
    )
}

$inside = Get-GitLine -Arguments @("rev-parse", "--is-inside-work-tree")
if ($inside -ne "true") {
    throw "Run this script inside a Git working tree."
}

Invoke-Git -Arguments @("remote", "get-url", $Remote) | Out-Null
Invoke-Git -Arguments @("fetch", $Remote, "--prune") | Out-Null

if (-not (Test-RefExists -Ref "refs/remotes/$Remote/$BaseBranch")) {
    throw "Remote base branch '$BaseRef' was not found."
}

$currentBranch = Get-GitLine -Arguments @("branch", "--show-current")

$remotePrefix = "refs/remotes/$Remote/"

$remoteRefs = @(
    (Invoke-Git -Arguments @(
        "for-each-ref",
        "--format=%(refname)",
        "refs/remotes/$Remote"
    )).Output |
    ForEach-Object { ([string]$_).Trim() } |
    Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        $_ -ne "${remotePrefix}HEAD" -and
        $_ -ne "${remotePrefix}${BaseBranch}"
    }
)

$candidates = @()

Write-Host ""
Write-Host "Branch cleanup"
Write-Host "Base: $BaseRef"
Write-Host ("Mode: " + $(if ($Delete) { "DELETE" } else { "DRY-RUN" }))
Write-Host ""

foreach ($remoteRef in $remoteRefs) {
    if (-not $remoteRef.StartsWith($remotePrefix, [StringComparison]::Ordinal)) {
        Write-Warning "Unexpected remote ref '$remoteRef'; skipped."
        continue
    }

    $branch = $remoteRef.Substring($remotePrefix.Length)

    if ($branch -eq $currentBranch) {
        continue
    }

    $remoteDiff = Get-Divergence -BranchRef $remoteRef
    $remoteSafe =
        $remoteDiff.Behind -gt 0 -and
        (Test-IsAncestor -Ancestor $remoteRef -Descendant $BaseRef)

    if (-not $remoteSafe) {
        Write-BranchStatus "[KEEP]" $branch $remoteDiff.Behind $remoteDiff.Ahead `
            "remote is not a stale ancestor of $BaseRef"
        continue
    }

    $localRef = "refs/heads/$branch"
    $hasLocal = Test-RefExists -Ref $localRef
    $localSha = ""

    if ($hasLocal) {
        $localDiff = Get-Divergence -BranchRef $localRef
        $localSafe =
            $localDiff.Behind -gt 0 -and
            (Test-IsAncestor -Ancestor $localRef -Descendant $BaseRef)

        if (-not $localSafe) {
            Write-BranchStatus "[KEEP]" $branch $localDiff.Behind $localDiff.Ahead `
                "local branch still has work or is not behind $BaseRef"
            continue
        }

        $localSha = Get-RefSha -Ref $localRef
    }

    $candidate = [pscustomobject]@{
        Branch    = $branch
        RemoteSha = Get-RefSha -Ref $remoteRef
        HasLocal  = $hasLocal
        LocalSha  = $localSha
        Behind    = $remoteDiff.Behind
        Ahead     = $remoteDiff.Ahead
    }

    $candidates += $candidate

    Write-BranchStatus "[SAFE]" $branch $remoteDiff.Behind $remoteDiff.Ahead `
        $(if ($hasLocal) { "GitHub + local" } else { "GitHub only" })
}

Write-Host ""

if ($candidates.Count -eq 0) {
    Write-Host "No safe stale branches found."
    exit 0
}

if (-not $Delete) {
    Write-Host "$($candidates.Count) branch(es) can be removed safely."
    Write-Host "Dry-run only. Run again with -Delete to remove them."
    exit 0
}

$deletedRemote = 0
$deletedLocal = 0
$failures = @()

foreach ($candidate in $candidates) {
    $branch = [string]$candidate.Branch
    $remoteRef = "refs/remotes/$Remote/$branch"

    # Re-check the fetched remote SHA and ancestry immediately before deletion.
    if (-not (Test-RefExists -Ref $remoteRef)) {
        Write-Warning "Remote branch '$branch' disappeared after the scan; skipped."
        $failures += "$branch (remote changed)"
        continue
    }

    $currentRemoteSha = Get-RefSha -Ref $remoteRef
    if (
        $currentRemoteSha -ne $candidate.RemoteSha -or
        -not (Test-IsAncestor -Ancestor $remoteRef -Descendant $BaseRef)
    ) {
        Write-Warning "Remote branch '$branch' changed or is no longer safe; skipped."
        $failures += "$branch (remote changed)"
        continue
    }

    # The lease prevents deleting a GitHub branch that advanced after fetch.
    $lease = "--force-with-lease=refs/heads/$branch`:$($candidate.RemoteSha)"
    $remoteDelete = Invoke-Git -Arguments @(
        "push",
        $lease,
        $Remote,
        "--delete",
        $branch
    ) -AllowFailure

    if ($remoteDelete.ExitCode -ne 0) {
        Write-Warning (
            "Could not delete GitHub branch '$branch':`n" +
            ($remoteDelete.Output -join [Environment]::NewLine)
        )
        $failures += "$branch (GitHub delete failed)"
        continue
    }

    $deletedRemote++
    Write-Host "[DELETED] GitHub  $branch"

    if (-not $candidate.HasLocal) {
        continue
    }

    $localRef = "refs/heads/$branch"
    if (-not (Test-RefExists -Ref $localRef)) {
        continue
    }

    # Never delete a local branch that moved after the scan.
    $currentLocalSha = Get-RefSha -Ref $localRef
    if (
        $currentLocalSha -ne $candidate.LocalSha -or
        -not (Test-IsAncestor -Ancestor $localRef -Descendant $BaseRef)
    ) {
        Write-Warning "Local branch '$branch' changed after the scan; kept locally."
        $failures += "$branch (local changed)"
        continue
    }

    # -D is safe here because ancestry and SHA were explicitly verified above.
    $localDelete = Invoke-Git -Arguments @(
        "branch", "-D", $branch
    ) -AllowFailure

    if ($localDelete.ExitCode -ne 0) {
        Write-Warning (
            "Could not delete local branch '$branch':`n" +
            ($localDelete.Output -join [Environment]::NewLine)
        )
        $failures += "$branch (local delete failed)"
        continue
    }

    $deletedLocal++
    Write-Host "[DELETED] Local   $branch"
}

Invoke-Git -Arguments @("fetch", $Remote, "--prune") | Out-Null

Write-Host ""
Write-Host "Deleted: $deletedRemote GitHub branch(es), $deletedLocal local branch(es)."

if ($failures.Count -gt 0) {
    Write-Warning ("Skipped/failed: " + ($failures -join ", "))
    exit 1
}

exit 0
