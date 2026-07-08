namespace Deployment.Contracts.AspireApps;

public sealed record AspireApplicationDto(
    Guid Id,
    string Name,
    string? Description,
    Guid EnvironmentId,
    string EnvironmentName,
    string ManifestSource,
    string? Version,
    string? SourceKey,
    bool IsActive,
    bool AutoDeploy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateAspireApplicationRequest(string Name, string? Description, Guid EnvironmentId, string ManifestSource, string? Version, string? SourceKey = null);
public sealed record UpdateAspireApplicationRequest(string Name, string? Description, Guid EnvironmentId, string ManifestSource, string? Version, string? SourceKey = null);

/// <summary>Trigger an Aspire-app deployment.</summary>
public sealed record TriggerAspireDeploymentRequest(string? TriggeredBy);

/// <summary>Roll an Aspire app back to a previous succeeded run's manifest/version.</summary>
public sealed record RollbackAspireDeploymentRequest(Guid TargetRunId, string? TriggeredBy);

/// <summary>Promote an Aspire app's current manifest to a different Kubernetes environment.</summary>
public sealed record PromoteAspireDeploymentRequest(Guid TargetEnvironmentId, string? TriggeredBy);

/// <summary>Toggle whether a CI publish of this app auto-triggers a deployment.</summary>
public sealed record SetAspireAutoDeployRequest(bool AutoDeploy);

public enum AspireRunStatusDto { Pending = 0, Running = 1, Succeeded = 2, Failed = 3, AwaitingApproval = 4, Rejected = 5 }

public sealed record AspireApplicationRunDto(
    Guid Id,
    Guid ApplicationId,
    string ApplicationName,
    string EnvironmentName,
    string KubeContext,
    string Namespace,
    string ManifestSource,
    string? Version,
    AspireRunStatusDto Status,
    string TriggeredBy,
    string? Log,
    string? FailureReason,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? DecisionBy = null,
    IReadOnlyList<DeployedImageDto>? DeployedImages = null);

/// <summary>An image a successful run put on the cluster (workload → image ref, digest-pinned when pinned).</summary>
public sealed record DeployedImageDto(string Workload, string Image);

/// <summary>Approve / reject a run that's awaiting approval (protected environment).</summary>
public sealed record ApproveAspireRunRequest(string? ApprovedBy);
public sealed record RejectAspireRunRequest(string? RejectedBy, string? Reason);

/// <summary>Health of a single workload / the app overall. Ordered so the numerically-largest value is the worst.</summary>
public enum WorkloadHealthDto { Unknown = 0, Healthy = 1, Degraded = 2, Down = 3 }

/// <summary>Live workloads read from a namespace + a reachability flag (an unreachable cluster is data, not an error).</summary>
public sealed record ClusterWorkloadsDto(
    bool Reachable, string? Error, WorkloadHealthDto OverallHealth, IReadOnlyList<WorkloadStatusDto> Workloads);

/// <summary>Live + drift status of an Aspire app: cluster health for its target namespace plus whether the
/// app's current manifest/version has been deployed yet.</summary>
public sealed record AspireAppStatusDto(
    Guid ApplicationId,
    string ApplicationName,
    string EnvironmentName,
    string? KubeContext,
    string? Namespace,
    bool Reachable,
    string? Error,
    WorkloadHealthDto OverallHealth,
    bool HasUndeployedChanges,
    bool HasImageDrift,
    string? CurrentVersion,
    string? LastDeployedVersion,
    DateTimeOffset? LastDeployedAtUtc,
    IReadOnlyList<WorkloadStatusDto> Workloads);

/// <summary>A live Kubernetes Deployment for the app: the image it runs, desired vs. ready replicas, and its pods.</summary>
public sealed record WorkloadStatusDto(
    string Name,
    string? Image,
    int DesiredReplicas,
    int ReadyReplicas,
    int UpdatedReplicas,
    WorkloadHealthDto Health,
    IReadOnlyList<PodStatusDto> Pods,
    bool Drifted = false,
    string? ExpectedImage = null);

public sealed record PodStatusDto(string Name, string Phase, int Restarts, bool Ready);
