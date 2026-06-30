namespace Deployment.Domain.Runs;

public enum DeploymentRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

public enum DeploymentTrigger
{
    Manual = 0,
    Auto = 1,
}

/// <summary>
/// Category of a step failure, mapped at the adapter boundary (crane / Cloud Run) where the original
/// error is richest. Lets the UI say <em>why</em> a deploy failed (auth vs missing tooling vs timeout)
/// instead of echoing a raw process/RPC blob.
/// </summary>
public enum StepFailureKind
{
    Unknown = 0,
    ToolMissing = 1,     // crane (or other CLI) not installed / not on PATH
    RegistryAuth = 2,    // Nexus/GAR rejected the push/pull credentials
    RegistryError = 3,   // crane copy failed for a non-auth reason
    CloudRunAuth = 4,    // Cloud Run admin API returned Unauthenticated/PermissionDenied
    CloudRunNotFound = 5,// target Cloud Run service missing (and create disabled)
    Timeout = 6,         // readiness poll / deadline exceeded
    Config = 7,          // missing required input (source ref, GAR repo, project/region, …)
}

/// <summary>Per-step outcome captured on the run (serialized as JSON on the aggregate).</summary>
public sealed record RunStepResult(
    int Order, Mappings.DeploymentStepKind Kind, string Status, string? Detail, StepFailureKind? FailureKind = null);
