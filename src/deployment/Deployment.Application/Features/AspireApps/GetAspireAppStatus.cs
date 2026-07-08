using Deployment.Application.Features.Environments;
using Deployment.Contracts.AspireApps;

namespace Deployment.Application.Features.AspireApps;

/// <summary>Reads live workload health for an Aspire app's target namespace from the cluster. Implemented in
/// Infrastructure over the Kubernetes client; a cluster that can't be reached returns
/// <see cref="ClusterWorkloadsDto.Reachable"/> = false rather than throwing.</summary>
public interface IAspireClusterStatusReader
{
    Task<ClusterWorkloadsDto> GetAsync(string? context, string @namespace, CancellationToken ct = default);
}

public sealed record GetAspireAppStatusQuery(Guid AppId);

/// <summary>
/// Composes an Aspire app's live status: the app's target environment (kube context + namespace) drives a
/// live cluster read, and the last successful run tells us what's actually deployed — the gap between that
/// and the app's current manifest/version is surfaced as <see cref="AspireAppStatusDto.HasUndeployedChanges"/>.
/// </summary>
public sealed class GetAspireAppStatusHandler
{
    private readonly IAspireApplicationReader _apps;
    private readonly IEnvironmentReader _envs;
    private readonly IAspireApplicationRunReader _runs;
    private readonly IAspireClusterStatusReader _cluster;

    public GetAspireAppStatusHandler(
        IAspireApplicationReader apps, IEnvironmentReader envs,
        IAspireApplicationRunReader runs, IAspireClusterStatusReader cluster)
    {
        _apps = apps; _envs = envs; _runs = runs; _cluster = cluster;
    }

    public async Task<AspireAppStatusDto?> HandleAsync(GetAspireAppStatusQuery q, CancellationToken ct = default)
    {
        var app = await _apps.GetByIdAsync(q.AppId, ct).ConfigureAwait(false);
        if (app is null) return null;

        var env = await _envs.GetByIdAsync(app.EnvironmentId, ct).ConfigureAwait(false);
        var context = env?.KubernetesContext;
        var ns = env?.KubernetesNamespace;

        // Last successful run = what's actually running.
        var runs = await _runs.ListAsync(app.Id, ct).ConfigureAwait(false);
        var lastDeploy = runs
            .Where(r => r.Status == AspireRunStatusDto.Succeeded)
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefault();

        // Undeployed changes: the app's current manifest/version differs from the last successful deploy
        // (e.g. a CI publish arrived with AutoDeploy off, or a manual edit hasn't been rolled out yet).
        var hasUndeployed = lastDeploy is not null
            && (!string.Equals(app.ManifestSource, lastDeploy.ManifestSource, StringComparison.Ordinal)
                || !string.Equals(app.Version, lastDeploy.Version, StringComparison.Ordinal));

        var cluster = string.IsNullOrWhiteSpace(ns)
            ? new ClusterWorkloadsDto(false, "No Kubernetes namespace is configured for this app's environment.",
                WorkloadHealthDto.Unknown, Array.Empty<WorkloadStatusDto>())
            : await _cluster.GetAsync(context, ns!, ct).ConfigureAwait(false);

        // Image drift: what each workload runs now vs. the image the last successful run recorded deploying.
        var (workloads, hasImageDrift) = ApplyImageDrift(cluster.Workloads, lastDeploy?.DeployedImages);

        return new AspireAppStatusDto(
            app.Id, app.Name, app.EnvironmentName, context, ns,
            cluster.Reachable, cluster.Error, cluster.OverallHealth,
            hasUndeployed, hasImageDrift, app.Version, lastDeploy?.Version, lastDeploy?.CompletedAtUtc,
            workloads);
    }

    private static (IReadOnlyList<WorkloadStatusDto> Workloads, bool HasDrift) ApplyImageDrift(
        IReadOnlyList<WorkloadStatusDto> live, IReadOnlyList<DeployedImageDto>? deployed)
    {
        if (deployed is not { Count: > 0 }) return (live, false);

        var expected = deployed
            .GroupBy(i => i.Workload, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Image, StringComparer.Ordinal);

        var hasDrift = false;
        var result = new List<WorkloadStatusDto>(live.Count);
        foreach (var w in live)
        {
            if (expected.TryGetValue(w.Name, out var exp)
                && !string.IsNullOrEmpty(w.Image)
                && !string.Equals(exp, w.Image, StringComparison.Ordinal))
            {
                hasDrift = true;
                result.Add(w with { Drifted = true, ExpectedImage = exp });
            }
            else
            {
                result.Add(w);
            }
        }

        // An expected workload that's no longer live is also drift (surfaced via the app-level flag).
        var liveNames = live.Select(w => w.Name).ToHashSet(StringComparer.Ordinal);
        if (expected.Keys.Any(name => !liveNames.Contains(name))) hasDrift = true;

        return (result, hasDrift);
    }
}
