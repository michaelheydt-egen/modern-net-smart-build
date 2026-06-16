using Microsoft.Extensions.Logging;
using Deployment.Application.Features.Runs;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Containers;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Services;

namespace Deployment.Application.Features.Integration;

/// <summary>
/// Consumer edge (bus → deployment): handles <see cref="Cicd.IntegrationEvents.Ci.ContainerPublished"/>
/// from "ci.events". Records the container in the light inventory, then — for every active service
/// bound to that container name whose mapping has auto-deploy enabled — requests an automatic
/// deployment. Idempotency: Wolverine's SQL inbox + the per-run executor.
/// </summary>
public sealed class ContainerPublishedConsumer
{
    public async Task Handle(
        Cicd.IntegrationEvents.Ci.ContainerPublished evt,
        IKnownContainerRepository containers,
        IServiceRepository services,
        IDeploymentMappingRepository mappings,
        RequestDeploymentHandler request,
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<ContainerPublishedConsumer> logger,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // 1) Upsert the light inventory (latest push wins).
        var known = await containers.FindByNameAsync(evt.ContainerName, ct).ConfigureAwait(false);
        if (known is null)
        {
            known = new KnownContainer(Guid.NewGuid(), evt.ContainerName, evt.Version, evt.ArtifactUri, now);
            await containers.AddAsync(known, ct).ConfigureAwait(false);
        }
        else
        {
            known.Observe(evt.Version, evt.ArtifactUri, now);
        }
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // 2) Auto-deploy: active services on this container × their auto-enabled mappings.
        var matchedServices = await services.ListActiveByContainerNameAsync(evt.ContainerName, ct).ConfigureAwait(false);
        var requested = 0;
        foreach (var service in matchedServices)
        {
            foreach (var mapping in await mappings.ListAutoByServiceAsync(service.Id, ct).ConfigureAwait(false))
            {
                var result = await request.HandleAsync(
                    new RequestDeploymentCommand(mapping.Id, evt.Version, DeploymentTrigger.Auto, $"auto:{evt.ContainerName}"), ct)
                    .ConfigureAwait(false);
                if (result.RunId is not null) requested++;
            }
        }

        logger.LogInformation(
            "[bus] ContainerPublished '{Container}' {Version} -> recorded; {Count} auto-deployment(s) requested.",
            evt.ContainerName, evt.Version, requested);
    }
}
