#requires -Version 7.0
<#
.SYNOPSIS
    One-time provisioning of a Google Cloud Run service (and optionally its GAR repo)
    so the Deployment platform can deploy revisions into it afterwards.

.DESCRIPTION
    The GoogleCloudRunDeploymentAdapter deploys by *updating an existing* Cloud Run
    service in place — it does not provision one (unless
    Deployment:GoogleCloudRun:CreateServiceIfMissing is enabled, which only creates a
    bare service). This script does the explicit, durable provisioning via gcloud:
    the runtime service account, scaling, ingress, and auth posture you want the
    service to keep across deploys (the adapter never changes those — it only swaps
    the image + secret env and shifts traffic).

    Idempotent: if the service already exists it is left untouched (so you don't
    clobber config); re-run safely. Requires the gcloud CLI on PATH, authenticated.

    Defaults are pre-filled for this project's configured target
    (egen-gcr / us-west1 / egen-cicd-net).

.EXAMPLE
    ./Bootstrap-CloudRunService.ps1 -ServiceName orders-api

.EXAMPLE
    # Provision with a runtime SA, public ingress, and the GAR repo created too.
    ./Bootstrap-CloudRunService.ps1 -ServiceName orders-api -CreateArtifactRegistry `
        -ServiceAccount run-orders@egen-gcr.iam.gserviceaccount.com -AllowUnauthenticated
#>
[CmdletBinding()]
param(
    # --- GCP target (defaults match web-admin appsettings Google:Projects[0]) ---
    [string] $GcpProject = "egen-gcr",
    [string] $Region     = "us-west1",

    [Parameter(Mandatory)] [string] $ServiceName,

    # Initial image. A placeholder is fine — the first real deployment swaps it.
    [string] $Image = "us-docker.pkg.dev/cloudrun/container/hello",

    # --- Durable service config the adapter will preserve across deploys ---
    [string] $ServiceAccount,                 # runtime SA email; blank = project default
    [int]    $MinInstances = 0,
    [int]    $MaxInstances = 4,
    [int]    $Concurrency  = 80,
    [string] $Cpu          = "1",
    [string] $Memory       = "512Mi",
    [ValidateSet("all", "internal", "internal-and-cloud-load-balancing")]
    [string] $Ingress      = "all",
    [switch] $AllowUnauthenticated,           # default: authenticated-only

    # --- Optional GAR repo provisioning ---
    [string] $ArtifactRegistry      = "egen-cicd-net",
    [switch] $CreateArtifactRegistry,

    # Print the planned gcloud commands without running them.
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }

if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    throw "gcloud CLI not found on PATH. Install the Google Cloud SDK and 'gcloud auth login' first."
}

# Run gcloud, or just print it in dry-run. Returns $true on success (exit 0).
function Invoke-Gcloud {
    param([string[]]$GcloudArgs, [switch]$AllowFailure)
    if ($DryRun) {
        Write-Host "    [dry-run] gcloud $($GcloudArgs -join ' ')" -ForegroundColor DarkYellow
        return $true
    }
    & gcloud @GcloudArgs
    $ok = ($LASTEXITCODE -eq 0)
    if (-not $ok -and -not $AllowFailure) {
        throw "gcloud $($GcloudArgs -join ' ') -> exit $LASTEXITCODE"
    }
    return $ok
}

# --------------------------------------------------------------------------
# 1. (optional) Artifact Registry repository — reuse if present
# --------------------------------------------------------------------------
if ($CreateArtifactRegistry) {
    Step "Artifact Registry '$ArtifactRegistry' ($Region)"
    $exists = Invoke-Gcloud -AllowFailure -GcloudArgs @(
        'artifacts', 'repositories', 'describe', $ArtifactRegistry,
        '--location', $Region, '--project', $GcpProject, '--format', 'value(name)')
    if ($exists -and -not $DryRun) {
        Write-Host "    repository already exists — leaving as is"
    } else {
        Invoke-Gcloud -GcloudArgs @(
            'artifacts', 'repositories', 'create', $ArtifactRegistry,
            '--repository-format', 'docker', '--location', $Region, '--project', $GcpProject) | Out-Null
        Write-Host "    created repository $ArtifactRegistry"
    }
}

# --------------------------------------------------------------------------
# 2. Cloud Run service — create only if absent (never clobber existing config)
# --------------------------------------------------------------------------
Step "Cloud Run service '$ServiceName' ($GcpProject / $Region)"
$present = Invoke-Gcloud -AllowFailure -GcloudArgs @(
    'run', 'services', 'describe', $ServiceName,
    '--region', $Region, '--project', $GcpProject, '--format', 'value(metadata.name)')

if ($present -and -not $DryRun) {
    Write-Host "    service already exists — nothing to do."
    Write-Host "    resourceId: projects/$GcpProject/locations/$Region/services/$ServiceName"
    exit 0
}

$deployArgs = [System.Collections.Generic.List[string]]::new()
$deployArgs.AddRange([string[]]@(
    'run', 'deploy', $ServiceName,
    '--image', $Image,
    '--region', $Region,
    '--project', $GcpProject,
    '--platform', 'managed',
    '--min-instances', "$MinInstances",
    '--max-instances', "$MaxInstances",
    '--concurrency', "$Concurrency",
    '--cpu', $Cpu,
    '--memory', $Memory,
    '--ingress', $Ingress))
if ($ServiceAccount) { $deployArgs.AddRange([string[]]@('--service-account', $ServiceAccount)) }
$deployArgs.Add($(if ($AllowUnauthenticated) { '--allow-unauthenticated' } else { '--no-allow-unauthenticated' }))

Invoke-Gcloud -GcloudArgs $deployArgs.ToArray() | Out-Null

Step "Provisioned."
Write-Host "    resourceId: projects/$GcpProject/locations/$Region/services/$ServiceName"
Write-Host "    Use that as the Cloud Run target's ResourceId, then deploy with Deploy-CloudRun.ps1."
