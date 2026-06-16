using Deployment.Domain.Mappings;

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
    public required string GcpProject { get; init; }
    public required string Region { get; init; }
    public required string GarRepository { get; init; }
    public required string CloudRunServiceName { get; init; }

    public string? RemoteImageRef { get; set; }            // set by GarPush
    public string? CloudRunRevision { get; set; }          // set by CloudRunDeploy

    /// <summary>The image a deploy step should run: the promoted GAR ref if present, else the source.</summary>
    public string ImageToDeploy => string.IsNullOrWhiteSpace(RemoteImageRef) ? SourceRef : RemoteImageRef!;
}

public sealed record StepOutcome(bool Success, string? Detail)
{
    public static StepOutcome Ok(string? detail = null) => new(true, detail);
    public static StepOutcome Fail(string detail) => new(false, detail);
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
