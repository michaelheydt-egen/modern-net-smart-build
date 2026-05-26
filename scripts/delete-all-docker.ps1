<#
.SYNOPSIS
    Deletes every component (image:tag) from a Nexus hosted Docker repository.

.DESCRIPTION
    Enumerates all components in the configured Nexus repository via the REST
    API and deletes them one by one. Each component is a single image+tag pair,
    so deleting all components removes every image and every tag in the repo.
    Pagination is handled automatically. Idempotent and safe to re-run.

    Settings (URL, credentials, repository) are script-level variables below.
    Edit them before running.

    The script lists the components and asks for confirmation by default.
    Pass -Force to skip the prompt.

.PARAMETER Force
    Skip the interactive confirmation prompt and delete immediately.

.EXAMPLE
    ./scripts/delete-all-docker.ps1
    Lists the images and prompts before deleting.

.EXAMPLE
    ./scripts/delete-all-docker.ps1 -Force
    Deletes without prompting.

.NOTES
    Authentication: HTTP Basic. The password can be set either in the
    `$NexusPass` variable at the top of the script, or via the NEXUS_PASS
    environment variable (used as a fallback when `$NexusPass` is empty or
    still set to the CHANGE_ME placeholder).

    Do NOT commit a real password to source control. Add this file to
    .gitignore if you keep a real password in it, or prefer the env var.

    The Nexus REST `/service/rest/v1` endpoint runs on the Nexus admin port
    (typically 8081), NOT on the docker registry connector port (typically
    8082). $NexusUrl below should point at the admin port.

    Disk space is reclaimed by Nexus's 'Compact blob store' scheduled task,
    not immediately on delete. Docker layer blobs are shared across images,
    so reclaim only happens when no remaining component references them.
#>

[CmdletBinding()]
param(
    [switch]$Force
)

# --- Configuration --------------------------------------------------------
$NexusUrl   = 'http://localhost:8081'
$NexusUser  = 'admin'
$NexusPass  = $null         # set here OR leave empty/CHANGE_ME and set $env:NEXUS_PASS; do not commit a real password
$Repository = 'docker-private'
# --------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'

# Fall back to the NEXUS_PASS environment variable when the in-file value is
# empty or still the placeholder.
if ([string]::IsNullOrWhiteSpace($NexusPass) -or $NexusPass -eq 'CHANGE_ME') {
    $NexusPass = $env:NEXUS_PASS
}
if ([string]::IsNullOrWhiteSpace($NexusPass)) {
    Write-Error "NexusPass is not set. Either edit `$NexusPass at the top of the script or set the NEXUS_PASS environment variable."
    exit 2
}

$apiBase = "$($NexusUrl.TrimEnd('/'))/service/rest/v1"
$basic   = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${NexusUser}:${NexusPass}"))
$headers = @{ Authorization = "Basic $basic"; Accept = 'application/json' }

# Probe Nexus reachability + credentials before doing anything destructive
Write-Host "Probing Nexus at $NexusUrl ..."
try {
    Invoke-RestMethod -Uri "$apiBase/status" -Headers $headers -Method Get | Out-Null
} catch {
    Write-Error "Failed to reach Nexus at $NexusUrl. $($_.Exception.Message)"
    exit 1
}

# Enumerate components (handles pagination via continuationToken).
# For docker, each component is one image+tag pair.
Write-Host "Enumerating components in repository '$Repository' ..."
$components = [System.Collections.Generic.List[psobject]]::new()
$token = $null
do {
    $url = "$apiBase/components?repository=$([Uri]::EscapeDataString($Repository))"
    if ($token) { $url += "&continuationToken=$([Uri]::EscapeDataString($token))" }
    $page = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    foreach ($item in $page.items) { $components.Add($item) }
    $token = $page.continuationToken
} while ($token)

if ($components.Count -eq 0) {
    Write-Host "Repository '$Repository' is empty. Nothing to delete."
    return
}

Write-Host ""
Write-Host "Found $($components.Count) image(s):"
foreach ($c in $components) {
    Write-Host "  $($c.name):$($c.version)"
}
Write-Host ""

if (-not $Force) {
    $reply = Read-Host "Delete all $($components.Count) images from '$Repository'? Type 'yes' to confirm"
    if ($reply -ne 'yes') {
        Write-Host "Aborted. Nothing deleted."
        return
    }
}

$total = $components.Count
$deleted = 0
$failed = 0
foreach ($c in $components) {
    $idx = $deleted + $failed + 1
    try {
        $deleteUrl = "$apiBase/components/$([Uri]::EscapeDataString($c.id))"
        Invoke-RestMethod -Uri $deleteUrl -Headers $headers -Method Delete | Out-Null
        $deleted++
        Write-Host "  [$idx/$total] deleted $($c.name):$($c.version)"
    } catch {
        $failed++
        Write-Host "  [$idx/$total] FAILED  $($c.name):$($c.version): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Deleted $deleted/$total image(s) ($failed failed)."
Write-Host "Note: blob disk space is reclaimed by Nexus's 'Compact blob store' scheduled task, not immediately."

if ($failed -gt 0) { exit 1 }
