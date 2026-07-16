using System.Globalization;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Deployment.Application.Abstractions;

namespace Deployment.Infrastructure.Kubernetes;

/// <summary><see cref="IKubeClusterAdmin"/> over the KubernetesClient — the mutating lifecycle actions behind
/// the Kubernetes admin screens (rolling restart, scale, delete pod). Merge-patches only; no resource is
/// created or replaced wholesale.</summary>
internal sealed class KubeClusterAdmin : IKubeClusterAdmin
{
    private readonly IKubeClientFactory _factory;
    private readonly ILogger<KubeClusterAdmin> _logger;

    public KubeClusterAdmin(IKubeClientFactory factory, ILogger<KubeClusterAdmin> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task RestartDeploymentAsync(string? context, string @namespace, string name, CancellationToken cancellationToken = default)
    {
        using var client = _factory.Create(context);
        var stamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        // Same mechanism as `kubectl rollout restart`: bump a pod-template annotation so the ReplicaSet rolls.
        var patch = $"{{\"spec\":{{\"template\":{{\"metadata\":{{\"annotations\":{{\"kubectl.kubernetes.io/restartedAt\":\"{stamp}\"}}}}}}}}}}";
        await client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(patch, V1Patch.PatchType.MergePatch), name, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[k8s] Restarted deployment {Name} in {Namespace}.", name, @namespace);
    }

    public async Task ScaleDeploymentAsync(string? context, string @namespace, string name, int replicas, CancellationToken cancellationToken = default)
    {
        if (replicas < 0) replicas = 0;
        using var client = _factory.Create(context);
        var patch = $"{{\"spec\":{{\"replicas\":{replicas}}}}}";
        await client.AppsV1.PatchNamespacedDeploymentScaleAsync(
            new V1Patch(patch, V1Patch.PatchType.MergePatch), name, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[k8s] Scaled deployment {Name} in {Namespace} to {Replicas}.", name, @namespace, replicas);
    }

    public async Task DeletePodAsync(string? context, string @namespace, string pod, CancellationToken cancellationToken = default)
    {
        using var client = _factory.Create(context);
        await client.CoreV1.DeleteNamespacedPodAsync(pod, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[k8s] Deleted pod {Pod} in {Namespace}.", pod, @namespace);
    }
}
