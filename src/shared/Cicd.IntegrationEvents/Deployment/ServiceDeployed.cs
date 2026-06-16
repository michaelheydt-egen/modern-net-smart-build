namespace Cicd.IntegrationEvents.Deployment;

/// <summary>
/// A service's container was deployed to a Cloud Run environment (promoted to GAR then rolled out).
/// Emitted by the deployment service on the "deployment.events" channel; downstream services
/// (CI feedback, notifications) may react.
/// </summary>
public sealed record ServiceDeployed(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid RunId,
    Guid ServiceId,
    string ServiceName,
    Guid EnvironmentId,
    string ContainerName,
    string Version,
    string GcpProject,
    string Region,
    string CloudRunServiceName,
    string RemoteImageRef,
    string CloudRunRevision) : IIntegrationEvent;
