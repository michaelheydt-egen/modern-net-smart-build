namespace Deployment.Domain.Mappings;

/// <summary>
/// One ordered step of a mapping's deployment recipe. Config is an open key/value bag for
/// step-specific settings (kept minimal for now; executors derive most config from the
/// Environment + mapping). Persisted as part of the mapping's JSON Steps column.
/// </summary>
public sealed record DeploymentStep(int Order, DeploymentStepKind Kind, IReadOnlyDictionary<string, string> Config)
{
    public static DeploymentStep Of(int order, DeploymentStepKind kind) =>
        new(order, kind, new Dictionary<string, string>());
}
