using Deployment.Domain.Common;

namespace Deployment.Domain.Runs.Events;

/// <summary>A run was requested — drives the executor that runs the recipe.</summary>
public sealed record DeploymentRunRequested(
    Guid RunId, Guid MappingId, Guid ServiceId, Guid EnvironmentId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>The deployment completed — translated to the Deployment.DeploymentSucceeded integration event.</summary>
public sealed record DeploymentRunSucceeded(
    Guid RunId, Guid ServiceId, Guid EnvironmentId, string ServiceName, string ContainerName, string Version,
    string GcpProject, string Region, string CloudRunServiceName, string RemoteImageRef, string CloudRunRevision,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>
/// A run failed. <paramref name="FailedStep"/> (the <c>DeploymentStepKind</c> that failed) and
/// <paramref name="Category"/> (the <c>StepFailureKind</c>) are present when the failure came from a
/// step, so the UI can raise a specific toast ("Deploy failed at GarPush — registry auth").
/// </summary>
public sealed record DeploymentRunFailed(
    Guid RunId, Guid ServiceId, Guid EnvironmentId, string Reason,
    string? FailedStep, string? Category, DateTimeOffset OccurredAtUtc) : IDomainEvent;
