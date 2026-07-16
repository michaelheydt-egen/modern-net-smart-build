namespace Deployment.Application.Abstractions;

/// <summary>Inputs for a slot deploy (blue-green or canary). <see cref="CanaryWeightPercent"/> is the initial
/// canary Ingress traffic weight (canary only; ignored by blue-green).</summary>
public sealed record RolloutDeployRequest(
    string Context, string Namespace, string Name, string Image, int ContainerPort, int Replicas,
    IReadOnlyDictionary<string, string> EnvVars, string? ImagePullSecret, int CanaryWeightPercent = 0);

/// <summary>Result of deploying + health-gating a new slot. <see cref="NewSlot"/> is the slot just
/// deployed; <see cref="ActiveSlot"/> is the currently-live slot (equal to NewSlot only on the first
/// bootstrap deploy, where the new version immediately takes traffic).</summary>
public sealed record RolloutDeployResult(string NewSlot, string ActiveSlot, bool Healthy, string Detail);

/// <summary>
/// Rollout mechanics on vanilla Kubernetes using two slot Deployments (<c>{name}-blue</c>/<c>{name}-green</c>)
/// and one Service. Blue-green: the Service selects one slot; promotion swaps the selector. Canary: the
/// Service selects the app (both slots) and traffic splits by replica ratio; promotion scales the canary to
/// full and retires the stable slot. Implemented in Infrastructure over the KubernetesClient.
/// </summary>
public interface IRolloutDeployer
{
    // ---- Blue-green ----
    /// <summary>Deploy the new version to the inactive slot and health-gate it. The Service selects one
    /// slot; traffic does not move (except the first bootstrap deploy, which points the Service at it).</summary>
    Task<RolloutDeployResult> DeployGreenAsync(RolloutDeployRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cut traffic over to <paramref name="newSlot"/> (swap the Service selector) and retire
    /// <paramref name="oldSlot"/> (scale to zero).</summary>
    Task<string> PromoteBlueGreenAsync(string context, string @namespace, string name, string newSlot, string oldSlot, CancellationToken cancellationToken = default);

    // ---- Canary (ingress-weighted) ----
    /// <summary>Deploy the canary slot at full replicas behind its own Service, and stamp an ingress-nginx
    /// canary Ingress routing <see cref="RolloutDeployRequest.CanaryWeightPercent"/>% of real traffic to it
    /// (the stable Service keeps the rest); then health-gate the canary.</summary>
    Task<RolloutDeployResult> DeployCanaryAsync(RolloutDeployRequest request, CancellationToken cancellationToken = default);

    /// <summary>Progressive ramp: set the canary Ingress traffic weight (%) to <paramref name="weight"/>.</summary>
    Task SetCanaryWeightAsync(string context, string @namespace, string name, int weight, CancellationToken cancellationToken = default);

    /// <summary>Complete a canary: repoint the stable Service at <paramref name="newSlot"/>, delete the canary
    /// Ingress + canary Service, and retire <paramref name="oldSlot"/>. <paramref name="fullReplicas"/> is unused
    /// (the canary already runs at full) — kept for signature compatibility.</summary>
    Task<string> PromoteCanaryAsync(string context, string @namespace, string name, string newSlot, string oldSlot, int fullReplicas, CancellationToken cancellationToken = default);

    /// <summary>Roll a canary back: delete the canary Deployment + its Service + the canary Ingress. Stable stays live.</summary>
    Task RollbackCanaryAsync(string context, string @namespace, string name, string canarySlot, CancellationToken cancellationToken = default);

    // ---- Shared ----
    /// <summary>Roll back a blue-green green slot: delete the new-slot Deployment. The active slot stays live.</summary>
    Task RollbackAsync(string context, string @namespace, string name, string newSlot, CancellationToken cancellationToken = default);
}
