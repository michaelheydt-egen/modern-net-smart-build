using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;

namespace Deployment.Application.Abstractions;

/// <summary>
/// Mutable per-run context threaded through the recipe's steps. Steps read the inputs and set the
/// outputs (GarPush sets <see cref="RemoteImageRef"/>, CloudRunDeploy sets <see cref="CloudRunRevision"/>).
/// </summary>
public sealed class DeploymentContext
{
    public required string ContainerName { get; init; }
    public required string Version { get; init; }
    public required string SourceRef { get; init; }       // Nexus pull ref (digest-pinned when available)

    // Cloud Run target (empty for a Kubernetes-only deploy).
    public string GcpProject { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string GarRepository { get; init; } = string.Empty;
    public string? CloudRunServiceName { get; init; }

    // Kubernetes target (set for a KubernetesApply deploy).
    public string? KubernetesContext { get; init; }
    public string? KubernetesNamespace { get; init; }
    public KubernetesSpec? Kubernetes { get; init; }

    public string? RemoteImageRef { get; set; }            // set by GarPush
    public string? CloudRunRevision { get; set; }          // set by CloudRunDeploy
    public string? KubernetesResource { get; set; }        // set by KubernetesApply

    /// <summary>The image a deploy step should run: the promoted GAR ref if present, else the source.</summary>
    public string ImageToDeploy => string.IsNullOrWhiteSpace(RemoteImageRef) ? SourceRef : RemoteImageRef!;
}

public sealed record StepOutcome(
    bool Success, string? Detail, StepFailureKind? FailureKind = null,
    bool Paused = false, string? GreenSlot = null, string? ActiveSlot = null, int? CanaryWeight = null)
{
    public static StepOutcome Ok(string? detail = null) => new(true, detail);

    /// <summary>A step that failed up-front on a missing input defaults to <see cref="StepFailureKind.Config"/>.</summary>
    public static StepOutcome Fail(string detail, StepFailureKind kind = StepFailureKind.Config) => new(false, detail, kind);

    /// <summary>Blue-green / canary manual promotion: the new slot is deployed + healthy but hasn't taken over.
    /// The run executor parks the run in <c>AwaitingPromotion</c> instead of settling it. <paramref name="canaryWeight"/>
    /// is the traffic % the canary Ingress currently routes (null for blue-green).</summary>
    public static StepOutcome PausedForPromotion(string greenSlot, string activeSlot, string? detail = null, int? canaryWeight = null)
        => new(false, detail, null, Paused: true, GreenSlot: greenSlot, ActiveSlot: activeSlot, CanaryWeight: canaryWeight);
}

/// <summary>
/// Thrown by a step adapter (crane promoter, Cloud Run deployer) to carry a categorized failure up to
/// the run executor, which records the <see cref="Kind"/> on the step and folds it into the run's
/// failure reason / completion toast. Use this instead of a bare exception when the cause is known.
/// </summary>
public sealed class DeploymentStepException : Exception
{
    public StepFailureKind Kind { get; }

    public DeploymentStepException(StepFailureKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;
}

/// <summary>One handler per <see cref="DeploymentStepKind"/>. The run executor dispatches by Kind.</summary>
public interface IDeploymentStepExecutor
{
    DeploymentStepKind Kind { get; }
    Task<StepOutcome> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the registered <see cref="IDeploymentStepExecutor"/> for a given step kind. Exists so the
/// Wolverine run handler depends on a single service instead of injecting
/// <c>IEnumerable&lt;IDeploymentStepExecutor&gt;</c> directly: Wolverine's generated handler code
/// mis-resolves an injected executor collection (it duplicates the last registration and drops the
/// others), whereas this registry is built by the DI container's own (correct) IEnumerable injection.
/// </summary>
public interface IStepExecutorRegistry
{
    bool TryGet(DeploymentStepKind kind, out IDeploymentStepExecutor executor);
}

/// <summary>
/// Default registry: indexes the DI-provided executors by their <see cref="IDeploymentStepExecutor.Kind"/>
/// (last registration wins on a duplicate kind). Constructed via normal constructor injection, so the
/// executor collection is materialised correctly.
/// </summary>
public sealed class StepExecutorRegistry : IStepExecutorRegistry
{
    private readonly IReadOnlyDictionary<DeploymentStepKind, IDeploymentStepExecutor> _byKind;

    public StepExecutorRegistry(IEnumerable<IDeploymentStepExecutor> executors)
    {
        var map = new Dictionary<DeploymentStepKind, IDeploymentStepExecutor>();
        foreach (var e in executors) map[e.Kind] = e;
        _byKind = map;
    }

    public bool TryGet(DeploymentStepKind kind, out IDeploymentStepExecutor executor)
        => _byKind.TryGetValue(kind, out executor!);
}
