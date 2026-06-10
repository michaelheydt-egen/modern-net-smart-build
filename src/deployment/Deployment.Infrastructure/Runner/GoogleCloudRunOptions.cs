namespace Deployment.Infrastructure.Runner;

/// <summary>
/// Options for <see cref="GoogleCloudRunDeploymentAdapter"/>. Bound from
/// configuration section <c>"Deployment:GoogleCloudRun"</c>.
///
/// Authentication uses Application Default Credentials (ADC) — set
/// <c>GOOGLE_APPLICATION_CREDENTIALS</c> to a service-account key file, or run
/// under Workload Identity / a GCE/GKE metadata server. The bound service
/// account needs <c>roles/run.developer</c> on the target service and
/// <c>roles/iam.serviceAccountUser</c> on the runtime service account. Credentials
/// are intentionally ambient (ADC) — never configured here — per the project's
/// secret-handling rules.
/// </summary>
public sealed class GoogleCloudRunOptions
{
    public const string SectionName = "Deployment:GoogleCloudRun";

    /// <summary>
    /// How long to poll the Cloud Run service for the new revision to become
    /// Ready before giving up. The runner imposes its own outer ceiling
    /// (<c>Deployment:Runner:AdapterTimeoutSeconds</c>); keep this at or below it.
    /// </summary>
    public int ReadinessTimeoutSeconds { get; set; } = 300;

    /// <summary>Delay between readiness polls.</summary>
    public int ReadinessPollSeconds { get; set; } = 5;

    /// <summary>
    /// When true, a deploy whose target Cloud Run service does not exist yet
    /// <em>creates</em> it (with the release image + secret env; Cloud Run platform
    /// defaults for everything else) instead of failing. Off by default: service
    /// provisioning — runtime service account, scaling, ingress, auth — is normally
    /// explicit (see <c>scripts/Bootstrap-CloudRunService.ps1</c>), and an
    /// auto-created service gets only minimal defaults. Enable for self-service /
    /// ephemeral environments where a bare service is acceptable.
    /// </summary>
    public bool CreateServiceIfMissing { get; set; } = false;

    /// <summary>
    /// When true, copy the release's image (a Nexus digest ref) into Google Artifact
    /// Registry before deploying, then deploy the GAR ref (decision #6). When false,
    /// the release's <c>ArtifactUri</c> is deployed as-is (it must already be
    /// reachable by Cloud Run).
    /// </summary>
    public bool PromoteFromNexus { get; set; } = false;

    /// <summary>
    /// GAR repository name — the <c>{repo}</c> in
    /// <c>{region}-docker.pkg.dev/{project}/{repo}/{image}</c>. Project + region come
    /// from the target's Cloud Run service resource name. Required when
    /// <see cref="PromoteFromNexus"/> is true.
    /// </summary>
    public string ArtifactRegistryRepository { get; set; } = string.Empty;

    /// <summary>
    /// Executable used to copy images digest-preserving (default <c>crane</c>). It
    /// must be on PATH (or an absolute path) and authenticated to both the source
    /// (Nexus) and destination (GAR) registries.
    /// </summary>
    public string CraneExecutable { get; set; } = "crane";
}
