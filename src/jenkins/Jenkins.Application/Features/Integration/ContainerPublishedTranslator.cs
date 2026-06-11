using Jenkins.Domain.Builds;

namespace Jenkins.Application.Features.Integration;

/// <summary>
/// Translation edge (CI → bus): when the internal <see cref="Jenkins.Domain.Builds.Events.ContainerPublished"/>
/// domain event fires, enrich it from the Build aggregate and publish the cross-service
/// <see cref="Cicd.IntegrationEvents.Ci.ContainerPublished"/> integration event. Returning the
/// event cascades it through Wolverine onto the bus (routed to the "ci.events" channel by the
/// topology, persisted via the outbox). Keeps the domain decoupled from the wire contract.
/// </summary>
public sealed class ContainerPublishedTranslator
{
    public async Task<Cicd.IntegrationEvents.Ci.ContainerPublished?> Handle(
        Jenkins.Domain.Builds.Events.ContainerPublished evt,
        IBuildStore builds,
        CancellationToken cancellationToken)
    {
        var build = await builds.GetByIdAsync(evt.BuildId, cancellationToken).ConfigureAwait(false);
        if (build is null) return null;

        return new Cicd.IntegrationEvents.Ci.ContainerPublished(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: evt.OccurredAtUtc,
            BuildId: evt.BuildId,
            RepositoryId: build.RepositoryId,
            ContainerName: evt.ContainerName,
            ArtifactUri: evt.Reference,
            Version: build.Versions?.PackageVersion ?? string.Empty,
            CommitSha: build.SourceRevision.CommitSha);
    }
}
