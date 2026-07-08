using Microsoft.Extensions.Logging;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;
using Deployment.Domain.AspireApps.Runs;
using Deployment.Domain.Environments;

namespace Deployment.Application.Features.AspireApps;

/// <summary>
/// Promote an Aspire application's current manifest to a <b>different</b> Kubernetes environment
/// (e.g. dev → staging). Because deploys digest-pin the images, promoting runs the exact same
/// artifacts elsewhere. Creates a new run against the target environment using the app's current
/// <c>ManifestSource</c>/<c>Version</c>; the app's home environment is left unchanged.
/// </summary>
public sealed record PromoteAspireDeploymentCommand(Guid ApplicationId, Guid TargetEnvironmentId, string? TriggeredBy);

public sealed class PromoteAspireDeploymentHandler
{
    private readonly IAspireApplicationRepository _apps;
    private readonly IEnvironmentRepository _envs;
    private readonly IAspireApplicationRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<PromoteAspireDeploymentHandler> _logger;

    public PromoteAspireDeploymentHandler(
        IAspireApplicationRepository apps, IEnvironmentRepository envs, IAspireApplicationRunRepository runs,
        IUnitOfWork uow, TimeProvider clock, ILogger<PromoteAspireDeploymentHandler> logger)
    {
        _apps = apps;
        _envs = envs;
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RequestAspireDeploymentResult> HandleAsync(PromoteAspireDeploymentCommand cmd, CancellationToken ct = default)
    {
        var app = await _apps.GetByIdAsync(cmd.ApplicationId, ct).ConfigureAwait(false);
        if (app is null) return new RequestAspireDeploymentResult(null, "application-not-found");
        if (!app.IsActive) return new RequestAspireDeploymentResult(null, "application-inactive");
        if (string.IsNullOrWhiteSpace(app.ManifestSource)) return new RequestAspireDeploymentResult(null, "no-manifest");

        var env = await _envs.GetByIdAsync(cmd.TargetEnvironmentId, ct).ConfigureAwait(false);
        if (env is null) return new RequestAspireDeploymentResult(null, "environment-not-found");
        if (string.IsNullOrWhiteSpace(env.KubernetesContext) || string.IsNullOrWhiteSpace(env.KubernetesNamespace))
            return new RequestAspireDeploymentResult(null, "environment-not-kubernetes");

        var run = new AspireApplicationRun(
            id: Guid.NewGuid(),
            applicationId: app.Id,
            applicationName: app.Name,
            environmentId: env.Id,
            environmentName: env.Name,
            kubeContext: env.KubernetesContext!,
            @namespace: env.KubernetesNamespace!,
            manifestSource: app.ManifestSource,
            version: app.Version,
            triggeredBy: cmd.TriggeredBy ?? $"promote:{env.Name}",
            requestedAtUtc: _clock.GetUtcNow());

        await _runs.AddAsync(run, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("[aspire] Promote {App} ({Version}) -> {Env} ({Context}/{Namespace}) (run {Run}).",
            app.Name, app.Version ?? "—", env.Name, env.KubernetesContext, env.KubernetesNamespace, run.Id);
        return new RequestAspireDeploymentResult(run.Id, "requested");
    }
}
