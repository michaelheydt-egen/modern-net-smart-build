namespace Deployment.Infrastructure.Gcp;

/// <summary>
/// Options for the GCP adapters. Bound from <c>Deployment:GoogleCloudRun</c>. Auth is ambient ADC
/// (GOOGLE_APPLICATION_CREDENTIALS / gcloud / Workload Identity) — never configured here.
/// </summary>
public sealed class GoogleCloudRunOptions
{
    public const string SectionName = "Deployment:GoogleCloudRun";

    /// <summary>crane executable for digest-preserving registry copy (Nexus → GAR). Must be on PATH.</summary>
    public string CraneExecutable { get; set; } = "crane";

    /// <summary>Create the Cloud Run service if it doesn't exist (bare defaults) instead of failing.</summary>
    public bool CreateServiceIfMissing { get; set; } = true;

    /// <summary>How long to wait for the new revision to become Ready.</summary>
    public int ReadinessTimeoutSeconds { get; set; } = 300;

    /// <summary>Delay between readiness polls.</summary>
    public int ReadinessPollSeconds { get; set; } = 5;
}
