using Deployment.Contracts.AspireApps;

namespace Deployment.Contracts.Kubernetes;

/// <summary>A kubeconfig context the deployment service can target.</summary>
public sealed record K8sContextDto(string Name, bool IsCurrent);

/// <summary>A namespace in the cluster (light list row — no per-namespace workload query).</summary>
public sealed record K8sNamespaceDto(
    string Name,
    string? Phase,
    DateTimeOffset? CreatedAtUtc,
    IReadOnlyDictionary<string, string>? Labels);

/// <summary>Full read of one namespace: workloads (+ pods, reusing the Aspire status shape), Services, Ingresses.
/// An unreachable/missing namespace is data (<see cref="Reachable"/> = false), not an exception.</summary>
public sealed record K8sNamespaceDetailDto(
    string Namespace,
    bool Reachable,
    string? Error,
    WorkloadHealthDto OverallHealth,
    IReadOnlyList<WorkloadStatusDto> Workloads,
    IReadOnlyList<K8sServiceDto> Services,
    IReadOnlyList<K8sIngressDto> Ingresses);

/// <summary>A live Kubernetes Service. <see cref="Ports"/> entries read like <c>http:8080→8080/TCP</c>.</summary>
public sealed record K8sServiceDto(
    string Name,
    string Type,
    string? ClusterIP,
    IReadOnlyList<string> Ports);

/// <summary>A live Kubernetes Ingress. <see cref="Urls"/> are the browsable <c>http(s)://host</c> forms of
/// <see cref="Hosts"/>; <see cref="Backends"/> read like <c>host/ → service:port</c>.</summary>
public sealed record K8sIngressDto(
    string Name,
    string? IngressClass,
    IReadOnlyList<string> Hosts,
    IReadOnlyList<string> Urls,
    IReadOnlyList<string> Backends);

/// <summary>A snapshot (tail) of a pod's logs.</summary>
public sealed record PodLogDto(string Pod, string? Container, string Log);

/// <summary>Set a Deployment's replica count.</summary>
public sealed record ScaleDeploymentRequest(int Replicas);
