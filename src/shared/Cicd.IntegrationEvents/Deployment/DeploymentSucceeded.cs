namespace Cicd.IntegrationEvents.Deployment;

/// <summary>
/// A deployment of a release into an environment completed successfully. Emitted by the
/// deployment service; downstream services (CI for pipeline feedback, notifications) may react.
/// </summary>
public sealed record DeploymentSucceeded(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid DeploymentId,
    Guid ReleaseId,
    Guid EnvironmentId) : IIntegrationEvent;
