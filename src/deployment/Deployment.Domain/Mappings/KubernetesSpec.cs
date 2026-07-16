namespace Deployment.Domain.Mappings;

/// <summary>
/// Per-mapping inputs for the <see cref="DeploymentStepKind.KubernetesApply"/> step: the platform
/// generates a Deployment (+ optional Service) from these plus the resolved image. The target cluster
/// context + namespace come from the environment. Persisted as JSON on the mapping.
/// </summary>
public sealed record KubernetesSpec(
    string DeploymentName,
    int ContainerPort,
    int Replicas,
    IReadOnlyDictionary<string, string> EnvVars,
    string? ImagePullSecret,
    bool CreateService,
    RolloutStrategy Strategy = RolloutStrategy.Direct,
    PromotionMode PromotionMode = PromotionMode.Automatic,
    int CanaryWeightPercent = 20,
    IReadOnlyList<int>? CanarySteps = null)
{
    public static KubernetesSpec Default(string deploymentName) =>
        new(deploymentName, 8080, 1, new Dictionary<string, string>(), null, true);

    /// <summary>The canary weight ladder (intermediate traffic %, ascending, 1–99). Manual promotion advances
    /// through these steps; the final promote cuts over to 100%. Falls back to a single step at
    /// <see cref="CanaryWeightPercent"/> when unset.</summary>
    public IReadOnlyList<int> NormalizedCanarySteps()
    {
        var raw = CanarySteps is { Count: > 0 } ? CanarySteps : new[] { CanaryWeightPercent <= 0 ? 20 : CanaryWeightPercent };
        return raw.Where(w => w is > 0 and < 100).Distinct().OrderBy(w => w).ToList() is { Count: > 0 } steps
            ? steps : new List<int> { 20 };
    }
}
