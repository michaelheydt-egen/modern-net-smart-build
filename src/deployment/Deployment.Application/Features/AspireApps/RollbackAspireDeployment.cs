using Microsoft.Extensions.Logging;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;
using Deployment.Domain.AspireApps.Runs;
using Deployment.Domain.Environments;
using Deployment.Domain.Runs;

namespace Deployment.Application.Features.AspireApps;

/// <summary>
/// Roll an Aspire application back to a previous <b>succeeded</b> run: repoint the app's current
/// manifest/version at that run's snapshot (so the rollback sticks), then create a new run deploying
/// it to the app's current Kubernetes environment. The new run drives the same executor + notifications
/// as any deploy; it's tagged <c>rollback:&lt;short-run-id&gt;</c>.
/// </summary>
public sealed record RollbackAspireDeploymentCommand(Guid ApplicationId, Guid TargetRunId, string? TriggeredBy);

public sealed class RollbackAspireDeploymentHandler
{
    private readonly IAspireApplicationRepository _apps;
    private readonly IEnvironmentRepository _envs;
    private readonly IAspireApplicationRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<RollbackAspireDeploymentHandler> _logger;

    public RollbackAspireDeploymentHandler(
        IAspireApplicationRepository apps, IEnvironmentRepository envs, IAspireApplicationRunRepository runs,
        IUnitOfWork uow, TimeProvider clock, ILogger<RollbackAspireDeploymentHandler> logger)
    {
        _apps = apps;
        _envs = envs;
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RequestAspireDeploymentResult> HandleAsync(RollbackAspireDeploymentCommand cmd, CancellationToken ct = default)
    {
        var app = await _apps.GetByIdAsync(cmd.ApplicationId, ct).ConfigureAwait(false);
        if (app is null) return new RequestAspireDeploymentResult(null, "application-not-found");
        if (!app.IsActive) return new RequestAspireDeploymentResult(null, "application-inactive");

        var target = await _runs.GetByIdAsync(cmd.TargetRunId, ct).ConfigureAwait(false);
        if (target is null || target.ApplicationId != app.Id) return new RequestAspireDeploymentResult(null, "run-not-found");
        if (target.Status != DeploymentRunStatus.Succeeded) return new RequestAspireDeploymentResult(null, "run-not-succeeded");

        // Deploy the old manifest to where the app currently lives (revert what's running here).
        var env = await _envs.GetByIdAsync(app.EnvironmentId, ct).ConfigureAwait(false);
        if (env is null) return new RequestAspireDeploymentResult(null, "environment-not-found");
        if (string.IsNullOrWhiteSpace(env.KubernetesContext) || string.IsNullOrWhiteSpace(env.KubernetesNamespace))
            return new RequestAspireDeploymentResult(null, "environment-not-kubernetes");

        var now = _clock.GetUtcNow();
        app.RollbackTo(target.ManifestSource, target.Version, now);

        var run = new AspireApplicationRun(
            id: Guid.NewGuid(),
            applicationId: app.Id,
            applicationName: app.Name,
            environmentId: env.Id,
            environmentName: env.Name,
            kubeContext: env.KubernetesContext!,
            @namespace: env.KubernetesNamespace!,
            manifestSource: target.ManifestSource,
            version: target.Version,
            triggeredBy: cmd.TriggeredBy ?? $"rollback:{target.Id.ToString("N")[..8]}",
            requestedAtUtc: now);

        await _runs.AddAsync(run, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("[aspire] Rollback of {App} to run {Target} (v{Version}) -> new run {Run}.",
            app.Name, target.Id, target.Version ?? "—", run.Id);
        return new RequestAspireDeploymentResult(run.Id, "requested");
    }
}
