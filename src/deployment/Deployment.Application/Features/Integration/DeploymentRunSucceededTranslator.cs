using Deployment.Domain.Runs.Events;
using Wolverine.Attributes;

namespace Deployment.Application.Features.Integration;

/// <summary>
/// Producer edge (deployment → bus): translates the internal <see cref="DeploymentRunSucceeded"/>
/// domain event into the cross-service <see cref="Cicd.IntegrationEvents.Deployment.ServiceDeployed"/>
/// integration event. The cascaded return is published through the SQL outbox onto the
/// "deployment.events" channel.
///
/// [WolverineHandler] is REQUIRED: Wolverine's convention only auto-discovers types whose names end
/// in "Handler"/"Consumer", so a "*Translator" is invisible without it (and the integration event is
/// never published).
/// </summary>
[WolverineHandler]
public sealed class DeploymentRunSucceededTranslator
{
    public Cicd.IntegrationEvents.Deployment.ServiceDeployed Handle(DeploymentRunSucceeded evt)
        => new(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: evt.OccurredAtUtc,
            RunId: evt.RunId,
            ServiceId: evt.ServiceId,
            ServiceName: evt.ServiceName,
            EnvironmentId: evt.EnvironmentId,
            ContainerName: evt.ContainerName,
            Version: evt.Version,
            GcpProject: evt.GcpProject,
            Region: evt.Region,
            CloudRunServiceName: evt.CloudRunServiceName,
            RemoteImageRef: evt.RemoteImageRef,
            CloudRunRevision: evt.CloudRunRevision);
}
