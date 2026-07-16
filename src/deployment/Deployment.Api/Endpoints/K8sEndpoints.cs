using Deployment.Application.Abstractions;
using Deployment.Contracts.Kubernetes;

namespace Deployment.Api.Endpoints;

/// <summary>
/// Read-only Kubernetes cluster browsing for the web-admin's K8s screens. Backed by <see cref="IKubeClusterReader"/>
/// (injected directly — these are pure reads with no cross-reader composition). <c>context</c> is an optional query
/// param; null means the kubeconfig's current context.
/// </summary>
public static class K8sEndpoints
{
    public static IEndpointRouteBuilder MapK8sEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/deployment/k8s").WithTags("Kubernetes");

        g.MapGet("contexts", async (IKubeClusterReader reader, CancellationToken ct) =>
            Results.Ok(await reader.ListContextsAsync(ct)));

        g.MapGet("namespaces", async (string? context, IKubeClusterReader reader, CancellationToken ct) =>
            Results.Ok(await reader.ListNamespacesAsync(context, ct)));

        g.MapGet("namespaces/{ns}", async (string ns, string? context, IKubeClusterReader reader, CancellationToken ct) =>
            Results.Ok(await reader.GetNamespaceAsync(context, ns, ct)));

        g.MapGet("namespaces/{ns}/pods/{pod}/logs", async (
            string ns, string pod, string? context, string? container, int? tail, IKubeClusterReader reader, CancellationToken ct) =>
            Results.Ok(await reader.GetPodLogAsync(context, ns, pod, container, tail ?? 500, ct)));

        // --- Lifecycle actions (mutating) ---
        g.MapPost("namespaces/{ns}/deployments/{name}/restart", async (
            string ns, string name, string? context, IKubeClusterAdmin admin, CancellationToken ct) =>
            await RunAsync(() => admin.RestartDeploymentAsync(context, ns, name, ct), "restart"));

        g.MapPost("namespaces/{ns}/deployments/{name}/scale", async (
            string ns, string name, string? context, ScaleDeploymentRequest body, IKubeClusterAdmin admin, CancellationToken ct) =>
            await RunAsync(() => admin.ScaleDeploymentAsync(context, ns, name, body.Replicas, ct), "scale"));

        g.MapDelete("namespaces/{ns}/pods/{pod}", async (
            string ns, string pod, string? context, IKubeClusterAdmin admin, CancellationToken ct) =>
            await RunAsync(() => admin.DeletePodAsync(context, ns, pod, ct), "delete pod"));

        return app;
    }

    private static async Task<IResult> RunAsync(Func<Task> action, string what)
    {
        try { await action(); return Results.NoContent(); }
        catch (Exception ex) { return Results.Problem(title: $"Could not {what}", detail: ex.Message, statusCode: 502); }
    }
}
