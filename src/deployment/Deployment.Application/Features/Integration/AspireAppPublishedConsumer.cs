using Microsoft.Extensions.Logging;
using Deployment.Application.Features.AspireApps;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;

namespace Deployment.Application.Features.Integration;

/// <summary>
/// Consumer edge (bus → deployment): handles <see cref="Cicd.IntegrationEvents.Ci.AspireAppPublished"/>
/// from "ci.events". Finds the registered <see cref="AspireApplication"/> by name (== the CI app name),
/// refreshes its <c>ManifestSource</c>/<c>Version</c> from the CI publish, and — if the app has
/// <c>AutoDeploy</c> enabled — requests a deployment. <b>Update-only</b>: a new app must first be
/// registered in the UI (its Kubernetes environment is a human choice), so an unmatched name is a no-op.
/// Idempotency: Wolverine's SQL inbox + the domain's no-op on an unchanged manifest.
/// </summary>
public sealed class AspireAppPublishedConsumer
{
    public async Task Handle(
        Cicd.IntegrationEvents.Ci.AspireAppPublished evt,
        IAspireApplicationRepository apps,
        RequestAspireDeploymentHandler request,
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<AspireAppPublishedConsumer> logger,
        CancellationToken ct)
    {
        var app = await apps.FindByNameAsync(evt.AppName, ct).ConfigureAwait(false);
        if (app is null)
        {
            logger.LogInformation(
                "[bus] AspireAppPublished '{App}' {Version} -> no registered Aspire application by that name; ignored " +
                "(register it with a Kubernetes environment to enable the handoff).",
                evt.AppName, evt.Version);
            return;
        }

        var changed = app.ApplyPublishedManifest(evt.ManifestUrl, evt.Version, clock.GetUtcNow());
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!changed)
        {
            logger.LogInformation(
                "[bus] AspireAppPublished '{App}' {Version} -> manifest unchanged; no deployment requested.",
                evt.AppName, evt.Version);
            return;
        }

        if (!app.AutoDeploy)
        {
            logger.LogInformation(
                "[bus] AspireAppPublished '{App}' {Version} -> manifest updated; auto-deploy off (manual deploy).",
                evt.AppName, evt.Version);
            return;
        }

        var result = await request.HandleAsync(
            new RequestAspireDeploymentCommand(app.Id, $"auto:ci:{evt.CommitSha}"), ct).ConfigureAwait(false);

        logger.LogInformation(
            "[bus] AspireAppPublished '{App}' {Version} -> manifest updated + auto-deploy requested (run {Run}, outcome {Outcome}).",
            evt.AppName, evt.Version, result.RunId, result.Outcome);
    }
}
