using Microsoft.Extensions.Logging;
using Deployment.Application.Abstractions;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Runs;

namespace Deployment.Application.Features.Runs;

/// <summary>Promote or roll back a per-service deployment run parked in
/// <see cref="DeploymentRunStatus.AwaitingPromotion"/> (blue-green, manual promotion). Promote cuts the
/// Service selector over to green and retires blue; Rollback deletes green and leaves blue live.</summary>
public sealed record DeploymentRunDecisionResult(bool Applied, string Outcome);

public sealed record PromoteDeploymentRunCommand(Guid RunId, string? PromotedBy);
public sealed record RollbackDeploymentRunCommand(Guid RunId, string? RolledBackBy, string? Reason);

public sealed class PromoteDeploymentRunHandler
{
    private readonly IDeploymentRunRepository _runs;
    private readonly IRolloutDeployer _rollout;
    private readonly Observability.DeploymentTelemetry _telemetry;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<PromoteDeploymentRunHandler> _logger;

    public PromoteDeploymentRunHandler(IDeploymentRunRepository runs, IRolloutDeployer rollout, Observability.DeploymentTelemetry telemetry, IUnitOfWork uow, TimeProvider clock, ILogger<PromoteDeploymentRunHandler> logger)
    { _runs = runs; _rollout = rollout; _telemetry = telemetry; _uow = uow; _clock = clock; _logger = logger; }

    public async Task<DeploymentRunDecisionResult> HandleAsync(PromoteDeploymentRunCommand cmd, CancellationToken ct = default)
    {
        var run = await _runs.GetByIdAsync(cmd.RunId, ct).ConfigureAwait(false);
        if (run is null) return new DeploymentRunDecisionResult(false, "run-not-found");
        if (run.Status != DeploymentRunStatus.AwaitingPromotion) return new DeploymentRunDecisionResult(false, "run-not-awaiting-promotion");
        if (!RolloutTarget.TryResolve(run, out var t)) return new DeploymentRunDecisionResult(false, "run-missing-rollout-context");

        // Canary progressive ramp: if there's a higher step in the ladder, bump the traffic weight and stay
        // parked; only the final "promote" (no higher step) cuts over to 100%.
        if (t.Strategy == Domain.Mappings.RolloutStrategy.Canary)
        {
            var current = run.RolloutCanaryWeight ?? t.CanarySteps[0];
            var next = t.CanarySteps.FirstOrDefault(s => s > current);
            if (next > 0)
            {
                await _rollout.SetCanaryWeightAsync(t.Context, t.Namespace, t.Name, next, ct).ConfigureAwait(false);
                run.AdvanceCanary(next);
                await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("[deploy] Run {Run} canary advanced {From}% -> {To}% by {By}.", run.Id, current, next, cmd.PromotedBy);
                return new DeploymentRunDecisionResult(true, $"advanced-to-{next}");
            }
            await _rollout.PromoteCanaryAsync(t.Context, t.Namespace, t.Name, t.GreenSlot, t.ActiveSlot, t.Replicas, ct).ConfigureAwait(false);
        }
        else
        {
            await _rollout.PromoteBlueGreenAsync(t.Context, t.Namespace, t.Name, t.GreenSlot, t.ActiveSlot, ct).ConfigureAwait(false);
        }
        run.SetKubernetesResource($"service/{t.Name} -> deployment/{t.Name}-{t.GreenSlot}");
        var now = _clock.GetUtcNow();
        run.PromoteRollout(cmd.PromotedBy ?? "unknown", now);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        _telemetry.RecordRun("kubernetes", run.Status.ToString(), t.Strategy.ToString(), (now - run.RequestedAtUtc).TotalSeconds);
        _logger.LogInformation("[deploy] Run {Run} promoted to '{Slot}' by {By}.", run.Id, t.GreenSlot, run.DecisionBy);
        return new DeploymentRunDecisionResult(true, "promoted");
    }
}

public sealed class RollbackDeploymentRunHandler
{
    private readonly IDeploymentRunRepository _runs;
    private readonly IRolloutDeployer _rollout;
    private readonly Observability.DeploymentTelemetry _telemetry;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<RollbackDeploymentRunHandler> _logger;

    public RollbackDeploymentRunHandler(IDeploymentRunRepository runs, IRolloutDeployer rollout, Observability.DeploymentTelemetry telemetry, IUnitOfWork uow, TimeProvider clock, ILogger<RollbackDeploymentRunHandler> logger)
    { _runs = runs; _rollout = rollout; _telemetry = telemetry; _uow = uow; _clock = clock; _logger = logger; }

    public async Task<DeploymentRunDecisionResult> HandleAsync(RollbackDeploymentRunCommand cmd, CancellationToken ct = default)
    {
        var run = await _runs.GetByIdAsync(cmd.RunId, ct).ConfigureAwait(false);
        if (run is null) return new DeploymentRunDecisionResult(false, "run-not-found");
        if (run.Status != DeploymentRunStatus.AwaitingPromotion) return new DeploymentRunDecisionResult(false, "run-not-awaiting-promotion");
        if (!RolloutTarget.TryResolve(run, out var t)) return new DeploymentRunDecisionResult(false, "run-missing-rollout-context");

        if (t.Strategy == Domain.Mappings.RolloutStrategy.Canary)
            await _rollout.RollbackCanaryAsync(t.Context, t.Namespace, t.Name, t.GreenSlot, ct).ConfigureAwait(false);
        else
            await _rollout.RollbackAsync(t.Context, t.Namespace, t.Name, t.GreenSlot, ct).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        run.RollbackRollout(cmd.RolledBackBy ?? "unknown", cmd.Reason, now);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        _telemetry.RecordRun("kubernetes", run.Status.ToString(), t.Strategy.ToString(), (now - run.RequestedAtUtc).TotalSeconds);
        _logger.LogInformation("[deploy] Run {Run} rolled back (green '{Slot}' deleted) by {By}.", run.Id, t.GreenSlot, run.DecisionBy);
        return new DeploymentRunDecisionResult(true, "rolled-back");
    }
}

/// <summary>The resolved rollout target for a parked run (coordinates, the two slots, strategy + replicas).</summary>
internal readonly record struct RolloutTarget(
    string Context, string Namespace, string Name, string GreenSlot, string ActiveSlot,
    Domain.Mappings.RolloutStrategy Strategy, int Replicas, IReadOnlyList<int> CanarySteps)
{
    public static bool TryResolve(DeploymentRun run, out RolloutTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(run.KubernetesContext) || string.IsNullOrWhiteSpace(run.KubernetesNamespace)) return false;
        if (string.IsNullOrWhiteSpace(run.RolloutGreenSlot) || string.IsNullOrWhiteSpace(run.RolloutActiveSlot)) return false;

        var name = run.KubernetesSpec is { DeploymentName: { Length: > 0 } d } ? d : LeafName(run.ContainerName);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var strategy = run.KubernetesSpec?.Strategy ?? Domain.Mappings.RolloutStrategy.BlueGreen;
        var replicas = run.KubernetesSpec?.Replicas ?? 1;
        var steps = run.KubernetesSpec?.NormalizedCanarySteps() ?? new List<int> { 20 };
        target = new RolloutTarget(run.KubernetesContext!, run.KubernetesNamespace!, name, run.RolloutGreenSlot!, run.RolloutActiveSlot!, strategy, replicas, steps);
        return true;
    }

    private static string LeafName(string containerName)
    {
        var n = containerName.Trim().Trim('/');
        var slash = n.LastIndexOf('/');
        return slash >= 0 ? n[(slash + 1)..] : n;
    }
}
