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

/// <summary>Toggle whether a CI publish of this app auto-triggers a deployment.</summary>
public sealed record SetAspireAutoDeployRequest(bool AutoDeploy);

public enum AspireRunStatusDto { Pending = 0, Running = 1, Succeeded = 2, Failed = 3 }

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
    DateTimeOffset? CompletedAtUtc);
