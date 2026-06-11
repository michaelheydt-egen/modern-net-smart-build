using Microsoft.Extensions.Logging;

namespace Deployment.Application.Features.Integration;

/// <summary>
/// Consumer edge (bus → deployment): handles the cross-service
/// <see cref="Cicd.IntegrationEvents.Ci.ContainerPublished"/> integration event from the
/// "ci.events" channel. Pilot implementation logs it — a real consumer might auto-materialize
/// a release or trigger a deployment. Idempotency is provided by Wolverine's SQL inbox.
/// </summary>
public sealed class ContainerPublishedConsumer
{
    public void Handle(
        Cicd.IntegrationEvents.Ci.ContainerPublished evt,
        ILogger<ContainerPublishedConsumer> logger)
    {
        logger.LogInformation(
            "[bus] ContainerPublished received — build {Build}, container '{Container}', version {Version}, uri {Uri}.",
            evt.BuildId, evt.ContainerName, evt.Version, evt.ArtifactUri);
    }
}
