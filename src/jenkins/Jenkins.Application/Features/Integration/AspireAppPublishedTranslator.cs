using Wolverine.Attributes;

namespace Jenkins.Application.Features.Integration;

/// <summary>
/// Translation edge (CI → bus): when the internal <see cref="Jenkins.Domain.Builds.Events.AspireManifestPublished"/>
/// domain event fires, map it to the cross-service <see cref="Cicd.IntegrationEvents.Ci.AspireAppPublished"/>
/// integration event. Returning it cascades through Wolverine onto the "ci.events" channel (persisted via the
/// outbox). Pure and dependency-free — the domain event carries everything — mirroring
/// <see cref="ContainerPublishedTranslator"/>.
///
/// [WolverineHandler] is REQUIRED: the convention only auto-discovers "*Handler"/"*Consumer" names, so a
/// "*Translator" is invisible (and the integration event never published) without it.
/// </summary>
[WolverineHandler]
public sealed class AspireAppPublishedTranslator
{
    public Cicd.IntegrationEvents.Ci.AspireAppPublished Handle(Jenkins.Domain.Builds.Events.AspireManifestPublished evt)
        => new(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: evt.OccurredAtUtc,
            BuildId: evt.BuildId,
            RepositoryId: evt.RepositoryId,
            AppName: evt.AppName,
            ManifestUrl: evt.ManifestUrl,
            Version: evt.Version,
            CommitSha: evt.CommitSha);
}
