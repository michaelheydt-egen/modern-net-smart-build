namespace Deployment.Application.Abstractions;

/// <summary>
/// Mutating cluster lifecycle actions for the web-admin Kubernetes screens — the write-side counterpart to
/// <see cref="IKubeClusterReader"/>. Implemented in Infrastructure over the Kubernetes client. Throws on
/// failure (the endpoint layer surfaces it as a problem response).
/// </summary>
public interface IKubeClusterAdmin
{
    /// <summary>Trigger a rolling restart of a Deployment (patches the pod-template restartedAt annotation).</summary>
    Task RestartDeploymentAsync(string? context, string @namespace, string name, CancellationToken cancellationToken = default);

    /// <summary>Set a Deployment's replica count.</summary>
    Task ScaleDeploymentAsync(string? context, string @namespace, string name, int replicas, CancellationToken cancellationToken = default);

    /// <summary>Delete a pod (the workload's controller reschedules a replacement).</summary>
    Task DeletePodAsync(string? context, string @namespace, string pod, CancellationToken cancellationToken = default);
}
