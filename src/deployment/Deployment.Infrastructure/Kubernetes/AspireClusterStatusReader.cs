using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Deployment.Application.Features.AspireApps;
using Deployment.Contracts.AspireApps;

namespace Deployment.Infrastructure.Kubernetes;

/// <summary>
/// <see cref="IAspireClusterStatusReader"/> over the KubernetesClient .NET API: lists the Deployments in the
/// target namespace, matches their pods by label selector, and derives per-workload + overall health. All
/// failures (unreachable cluster, missing namespace, auth) are turned into a non-throwing
/// <see cref="ClusterWorkloadsDto"/> so the status view can render them.
/// </summary>
internal sealed class AspireClusterStatusReader : IAspireClusterStatusReader
{
    private const int RestartAlertThreshold = 3;

    private readonly IKubeClientFactory _factory;
    private readonly ILogger<AspireClusterStatusReader> _logger;

    public AspireClusterStatusReader(IKubeClientFactory factory, ILogger<AspireClusterStatusReader> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<ClusterWorkloadsDto> GetAsync(string? context, string @namespace, CancellationToken ct = default)
    {
        try
        {
            using var client = _factory.Create(context);
            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(@namespace, cancellationToken: ct).ConfigureAwait(false);
            var pods = await client.CoreV1.ListNamespacedPodAsync(@namespace, cancellationToken: ct).ConfigureAwait(false);

            var workloads = new List<WorkloadStatusDto>();
            foreach (var d in deployments.Items.OrderBy(x => x.Metadata?.Name, StringComparer.Ordinal))
            {
                var desired = d.Spec?.Replicas ?? 0;
                var ready = d.Status?.ReadyReplicas ?? 0;
                var updated = d.Status?.UpdatedReplicas ?? 0;
                var image = d.Spec?.Template?.Spec?.Containers?.FirstOrDefault()?.Image;

                var podDtos = MatchPods(pods.Items, d.Spec?.Selector?.MatchLabels)
                    .Select(ToPodDto)
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .ToList();

                workloads.Add(new WorkloadStatusDto(
                    d.Metadata?.Name ?? "(unnamed)", image, desired, ready, updated,
                    Health(desired, ready, podDtos), podDtos));
            }

            // Enum is ordered so the numerically-largest health is the worst; overall = worst workload.
            var overall = workloads.Count == 0
                ? WorkloadHealthDto.Unknown
                : workloads.Select(w => w.Health).Max();
            return new ClusterWorkloadsDto(true, null, overall, workloads);
        }
        catch (HttpOperationException http) when ((int)http.Response.StatusCode == 404)
        {
            // Reachable, but nothing deployed there yet.
            return new ClusterWorkloadsDto(true, $"Namespace '{@namespace}' does not exist on the cluster yet.",
                WorkloadHealthDto.Down, Array.Empty<WorkloadStatusDto>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[k8s] Live status read failed for namespace {Namespace} (context {Context}).", @namespace, context);
            return new ClusterWorkloadsDto(false, $"Cluster unreachable: {FirstLine(ex.Message)}",
                WorkloadHealthDto.Unknown, Array.Empty<WorkloadStatusDto>());
        }
    }

    private static IEnumerable<V1Pod> MatchPods(IEnumerable<V1Pod> pods, IDictionary<string, string>? selector)
    {
        if (selector is null || selector.Count == 0) return Enumerable.Empty<V1Pod>();
        return pods.Where(p => p.Metadata?.Labels is { } labels
            && selector.All(kv => labels.TryGetValue(kv.Key, out var v) && v == kv.Value));
    }

    private static PodStatusDto ToPodDto(V1Pod p)
    {
        var cs = p.Status?.ContainerStatuses;
        var restarts = cs?.Sum(c => c.RestartCount) ?? 0;
        var ready = cs is { Count: > 0 } && cs.All(c => c.Ready);
        var phase = string.IsNullOrWhiteSpace(p.Status?.Phase) ? "Unknown" : p.Status!.Phase;
        return new PodStatusDto(p.Metadata?.Name ?? "(unnamed)", phase, restarts, ready);
    }

    private static WorkloadHealthDto Health(int desired, int ready, IReadOnlyList<PodStatusDto> pods)
    {
        if (desired <= 0) return WorkloadHealthDto.Unknown;
        if (ready == 0) return WorkloadHealthDto.Down;
        if (ready < desired || pods.Any(p => p.Restarts >= RestartAlertThreshold)) return WorkloadHealthDto.Degraded;
        return WorkloadHealthDto.Healthy;
    }

    private static string FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "no detail";
        var nl = s.IndexOfAny(['\r', '\n']);
        return (nl >= 0 ? s[..nl] : s).Trim();
    }
}
