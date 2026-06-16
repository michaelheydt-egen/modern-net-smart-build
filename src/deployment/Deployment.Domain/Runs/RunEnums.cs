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

/// <summary>Per-step outcome captured on the run (serialized as JSON on the aggregate).</summary>
public sealed record RunStepResult(int Order, Mappings.DeploymentStepKind Kind, string Status, string? Detail);
