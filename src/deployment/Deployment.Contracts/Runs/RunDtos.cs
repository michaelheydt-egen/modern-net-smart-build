namespace Deployment.Contracts.Runs;

public enum DeploymentRunStatusDto { Pending = 0, Running = 1, Succeeded = 2, Failed = 3 }
public enum DeploymentTriggerDto { Manual = 0, Auto = 1 }

public sealed record RunStepResultDto(int Order, string Kind, string Status, string? Detail, string? FailureKind = null);

public sealed record DeploymentRunDto(
    Guid Id,
    Guid MappingId,
    Guid ServiceId,
    string ServiceName,
    Guid EnvironmentId,
    string ContainerName,
    string Version,
    string SourceRef,
    string GcpProject,
    string Region,
    string CloudRunServiceName,
    DeploymentTriggerDto Trigger,
    string TriggeredBy,
    DeploymentRunStatusDto Status,
    string? RemoteImageRef,
    string? CloudRunRevision,
    string? FailureReason,
    IReadOnlyList<RunStepResultDto> Steps,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc);

/// <summary>Manually trigger a deployment for a mapping. Version optional — defaults to the latest known container.</summary>
public sealed record TriggerDeploymentRequest(string? Version, string? TriggeredBy);

public sealed record KnownContainerDto(
    Guid Id, string ContainerName, string Version, string? ImageDigest, string NexusRef,
    DateTimeOffset FirstSeenAtUtc, DateTimeOffset LastSeenAtUtc);

/// <summary>Manually seed the inventory (for testing the deploy flow without a live CI push).</summary>
public sealed record AddKnownContainerRequest(string ContainerName, string Version, string NexusRef);
