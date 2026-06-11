using Microsoft.Extensions.Logging;

namespace Jenkins.Application.Features.Integration;

/// <summary>
/// Consumer edge (bus → CI): handles the cross-service
/// <see cref="Cicd.IntegrationEvents.Deployment.DeploymentSucceeded"/> integration event that CI
/// subscribes to (the "deployment.events" channel). Pilot implementation just logs it — a real
/// consumer might mark the originating build/release as deployed, or notify. Idempotency is
/// provided by Wolverine's SQL inbox (at-least-once + dedupe).
/// </summary>
public sealed class DeploymentSucceededConsumer
{
    public void Handle(
        Cicd.IntegrationEvents.Deployment.DeploymentSucceeded evt,
        ILogger<DeploymentSucceededConsumer> logger)
    {
        logger.LogInformation(
            "[bus] DeploymentSucceeded received — deployment {Deployment}, release {Release}, environment {Environment}.",
            evt.DeploymentId, evt.ReleaseId, evt.EnvironmentId);
    }
}
