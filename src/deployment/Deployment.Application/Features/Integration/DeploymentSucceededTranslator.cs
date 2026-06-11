using Deployment.Domain.Deployments;

namespace Deployment.Application.Features.Integration;

/// <summary>
/// Translation edge (deployment → bus): when the internal
/// <see cref="Deployment.Domain.Deployments.Events.DeploymentSucceeded"/> domain event fires,
/// enrich it from the Deployment aggregate and publish the cross-service
/// <see cref="Cicd.IntegrationEvents.Deployment.DeploymentSucceeded"/> integration event.
/// Returning it cascades it onto the bus (routed to "deployment.events", persisted via the outbox).
/// </summary>
public sealed class DeploymentSucceededTranslator
{
    public async Task<Cicd.IntegrationEvents.Deployment.DeploymentSucceeded?> Handle(
        Deployment.Domain.Deployments.Events.DeploymentSucceeded evt,
        IDeploymentRepository deployments,
        CancellationToken cancellationToken)
    {
        var deployment = await deployments.GetByIdAsync(evt.DeploymentId, cancellationToken).ConfigureAwait(false);
        if (deployment is null) return null;

        return new Cicd.IntegrationEvents.Deployment.DeploymentSucceeded(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: evt.OccurredAtUtc,
            DeploymentId: evt.DeploymentId,
            ReleaseId: deployment.ReleaseId,
            EnvironmentId: deployment.EnvironmentId);
    }
}
