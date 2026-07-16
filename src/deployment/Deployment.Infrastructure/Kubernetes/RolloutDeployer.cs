using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Deployment.Application.Abstractions;
using Deployment.Domain.Runs;

namespace Deployment.Infrastructure.Kubernetes;

/// <summary>
/// <see cref="IRolloutDeployer"/> over the KubernetesClient. Two slot Deployments
/// (<c>{name}-blue</c>/<c>{name}-green</c>) share one Service. Blue-green: the Service selects one slot and
/// promotion swaps the selector. Canary: the Service selects the app (both slots) so traffic splits by
/// replica ratio, the active slot is tracked by a Service annotation, and promotion scales the canary to
/// full + retires the stable slot. Reuses the SSA + readiness-poll patterns from <see cref="KubernetesDeployer"/>.
/// </summary>
internal sealed class RolloutDeployer : IRolloutDeployer
{
    private const string FieldManager = "cicd-deployment";
    private const string Blue = "blue";
    private const string Green = "green";
    private const string ActiveSlotAnnotation = "rollout.cicd/active-slot";

    private readonly IKubeClientFactory _factory;
    private readonly IOptionsMonitor<KubernetesOptions> _options;
    private readonly ILogger<RolloutDeployer> _logger;

    public RolloutDeployer(IKubeClientFactory factory, IOptionsMonitor<KubernetesOptions> options, ILogger<RolloutDeployer> logger)
    {
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    // ==================== Blue-green ====================

    public async Task<RolloutDeployResult> DeployGreenAsync(RolloutDeployRequest req, CancellationToken ct = default)
    {
        try
        {
            using var client = _factory.Create(req.Context);
            await EnsureNamespaceAsync(client, req.Namespace, ct).ConfigureAwait(false);

            var active = await ResolveSelectorSlotAsync(client, req.Namespace, req.Name, ct).ConfigureAwait(false);
            var target = Other(active);

            await ApplyDeploymentAsync(client, req, target, Math.Max(1, req.Replicas), ct).ConfigureAwait(false);

            // First deploy: no Service yet → create it selecting this slot (it becomes active immediately).
            if (active is null)
                await ApplyServiceAsync(client, req, selectorSlot: target, activeSlot: target, ct).ConfigureAwait(false);

            var healthy = await HealthGateAsync(client, req, target, ct).ConfigureAwait(false);
            _logger.LogInformation("[k8s] blue-green {Name} slot={Slot} active={Active} healthy={Healthy}.", req.Name, target, active ?? target, healthy);
            return new RolloutDeployResult(target, active ?? target, healthy, $"deployment/{SlotName(req.Name, target)} ({(healthy ? "healthy" : "unhealthy")})");
        }
        catch (Exception ex) { throw Wrap(ex, "blue-green deploy"); }
    }

    public async Task<string> PromoteBlueGreenAsync(string context, string ns, string name, string newSlot, string oldSlot, CancellationToken ct = default)
    {
        using var client = _factory.Create(context);
        // Merge-patch only the selector (a server-side apply would strip the ports it didn't restate).
        await client.CoreV1.PatchNamespacedServiceAsync(
            MergePatch($"{{\"spec\":{{\"selector\":{{\"app\":\"{name}\",\"slot\":\"{newSlot}\"}}}}}}"), name, ns, cancellationToken: ct).ConfigureAwait(false);
        await RetireSlotAsync(client, ns, name, oldSlot, newSlot, ct).ConfigureAwait(false);
        _logger.LogInformation("[k8s] blue-green promote {Name} -> slot={Slot} (retired {Old}).", name, newSlot, oldSlot);
        return $"service/{name} -> deployment/{SlotName(name, newSlot)}";
    }

    // ==================== Canary ====================

    public async Task<RolloutDeployResult> DeployCanaryAsync(RolloutDeployRequest req, CancellationToken ct = default)
    {
        try
        {
            using var client = _factory.Create(req.Context);
            await EnsureNamespaceAsync(client, req.Namespace, ct).ConfigureAwait(false);

            var stable = await ResolveAnnotationSlotAsync(client, req.Namespace, req.Name, ct).ConfigureAwait(false);
            var full = Math.Max(1, req.Replicas);

            // Bootstrap: deploy the first slot at full replicas; the stable Service selects only that slot and a
            // primary Ingress makes it reachable — later canaries attach a weighted Ingress to the same host.
            if (stable is null)
            {
                await ApplyDeploymentAsync(client, req, Blue, full, ct).ConfigureAwait(false);
                await ApplyServiceAsync(client, req, selectorSlot: Blue, activeSlot: Blue, ct).ConfigureAwait(false);
                await ApplyPrimaryIngressAsync(client, req, ct).ConfigureAwait(false);
                var ok = await HealthGateAsync(client, req, Blue, ct).ConfigureAwait(false);
                return new RolloutDeployResult(Blue, Blue, ok, $"deployment/{SlotName(req.Name, Blue)} ({(ok ? "healthy" : "unhealthy")})");
            }

            var canary = Other(stable);
            var weight = Math.Clamp(req.CanaryWeightPercent <= 0 ? 20 : req.CanaryWeightPercent, 1, 99);

            // Canary runs at FULL replicas; the split is real traffic weighting via the canary Ingress, not
            // an approximation by replica count.
            await ApplyDeploymentAsync(client, req, canary, full, ct).ConfigureAwait(false);
            await ApplyCanaryServiceAsync(client, req, canary, ct).ConfigureAwait(false);
            await ApplyPrimaryIngressAsync(client, req, ct).ConfigureAwait(false); // ensure the primary exists to attach to
            await ApplyCanaryIngressAsync(client, req, weight, ct).ConfigureAwait(false);
            var healthy = await HealthGateAsync(client, req, canary, ct).ConfigureAwait(false);
            _logger.LogInformation("[k8s] canary {Name} slot={Slot} weight={Weight}% stable={Stable} healthy={Healthy}.",
                req.Name, canary, weight, stable, healthy);
            return new RolloutDeployResult(canary, stable, healthy, $"deployment/{SlotName(req.Name, canary)} (canary {weight}% traffic, {(healthy ? "healthy" : "unhealthy")})");
        }
        catch (Exception ex) { throw Wrap(ex, "canary deploy"); }
    }

    public async Task SetCanaryWeightAsync(string context, string ns, string name, int weight, CancellationToken ct = default)
    {
        using var client = _factory.Create(context);
        var w = Math.Clamp(weight, 1, 99);
        await client.NetworkingV1.PatchNamespacedIngressAsync(
            MergePatch($"{{\"metadata\":{{\"annotations\":{{\"nginx.ingress.kubernetes.io/canary-weight\":\"{w}\"}}}}}}"),
            CanaryName(name), ns, cancellationToken: ct).ConfigureAwait(false);
        _logger.LogInformation("[k8s] canary {Name} weight -> {Weight}%.", name, w);
    }

    public async Task<string> PromoteCanaryAsync(string context, string ns, string name, string newSlot, string oldSlot, int fullReplicas, CancellationToken ct = default)
    {
        using var client = _factory.Create(context);
        // Cut over: repoint the stable Service at the canary slot, drop the canary Ingress + Service, retire old.
        await client.CoreV1.PatchNamespacedServiceAsync(
            MergePatch($"{{\"metadata\":{{\"annotations\":{{\"{ActiveSlotAnnotation}\":\"{newSlot}\"}}}},\"spec\":{{\"selector\":{{\"app\":\"{name}\",\"slot\":\"{newSlot}\"}}}}}}"),
            name, ns, cancellationToken: ct).ConfigureAwait(false);
        await IgnoreNotFoundAsync(() => client.NetworkingV1.DeleteNamespacedIngressAsync(CanaryName(name), ns, cancellationToken: ct)).ConfigureAwait(false);
        await IgnoreNotFoundAsync(() => client.CoreV1.DeleteNamespacedServiceAsync(CanaryName(name), ns, cancellationToken: ct)).ConfigureAwait(false);
        await RetireSlotAsync(client, ns, name, oldSlot, newSlot, ct).ConfigureAwait(false);
        _logger.LogInformation("[k8s] canary promote {Name} -> slot={Slot} (retired {Old}).", name, newSlot, oldSlot);
        return $"service/{name} -> deployment/{SlotName(name, newSlot)} (100%)";
    }

    public async Task RollbackCanaryAsync(string context, string ns, string name, string canarySlot, CancellationToken ct = default)
    {
        using var client = _factory.Create(context);
        await IgnoreNotFoundAsync(() => client.NetworkingV1.DeleteNamespacedIngressAsync(CanaryName(name), ns, cancellationToken: ct)).ConfigureAwait(false);
        await IgnoreNotFoundAsync(() => client.CoreV1.DeleteNamespacedServiceAsync(CanaryName(name), ns, cancellationToken: ct)).ConfigureAwait(false);
        await IgnoreNotFoundAsync(() => client.AppsV1.DeleteNamespacedDeploymentAsync(SlotName(name, canarySlot), ns, cancellationToken: ct)).ConfigureAwait(false);
        _logger.LogInformation("[k8s] canary rollback {Name}: dropped canary ingress/service/deployment (slot={Slot}).", name, canarySlot);
    }

    // ==================== Shared ====================

    public async Task RollbackAsync(string context, string ns, string name, string newSlot, CancellationToken ct = default)
    {
        using var client = _factory.Create(context);
        await IgnoreNotFoundAsync(() => client.AppsV1.DeleteNamespacedDeploymentAsync(SlotName(name, newSlot), ns, cancellationToken: ct)).ConfigureAwait(false);
        _logger.LogInformation("[k8s] rollout rollback {Name}: deleted slot={Slot}.", name, newSlot);
    }

    // ==================== Helpers ====================

    private async Task EnsureNamespaceAsync(IKubernetes client, string ns, CancellationToken ct) =>
        await IgnoreConflictAsync(() => client.CoreV1.PatchNamespaceAsync(
            Apply(new V1Namespace { ApiVersion = "v1", Kind = "Namespace", Metadata = new V1ObjectMeta { Name = ns } }),
            ns, fieldManager: FieldManager, force: true, cancellationToken: ct)).ConfigureAwait(false);

    private static async Task ApplyDeploymentAsync(IKubernetes client, RolloutDeployRequest req, string slot, int replicas, CancellationToken ct) =>
        await client.AppsV1.PatchNamespacedDeploymentAsync(
            Apply(BuildDeployment(req, slot, replicas)), SlotName(req.Name, slot), req.Namespace,
            fieldManager: FieldManager, force: true, cancellationToken: ct).ConfigureAwait(false);

    private static async Task ApplyServiceAsync(IKubernetes client, RolloutDeployRequest req, string? selectorSlot, string activeSlot, CancellationToken ct)
    {
        var selector = new Dictionary<string, string> { ["app"] = req.Name };
        if (selectorSlot is not null) selector["slot"] = selectorSlot;         // blue-green: pin one slot
        await client.CoreV1.PatchNamespacedServiceAsync(Apply(new V1Service
        {
            ApiVersion = "v1", Kind = "Service",
            Metadata = new V1ObjectMeta
            {
                Name = req.Name, NamespaceProperty = req.Namespace,
                Labels = new Dictionary<string, string> { ["app"] = req.Name },
                Annotations = new Dictionary<string, string> { [ActiveSlotAnnotation] = activeSlot },
            },
            Spec = new V1ServiceSpec
            {
                Selector = selector,
                Ports = new List<V1ServicePort> { new() { Port = req.ContainerPort, TargetPort = req.ContainerPort } },
            },
        }), req.Name, req.Namespace, fieldManager: FieldManager, force: true, cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task ApplyCanaryServiceAsync(IKubernetes client, RolloutDeployRequest req, string canarySlot, CancellationToken ct) =>
        await client.CoreV1.PatchNamespacedServiceAsync(Apply(new V1Service
        {
            ApiVersion = "v1", Kind = "Service",
            Metadata = new V1ObjectMeta
            {
                Name = CanaryName(req.Name), NamespaceProperty = req.Namespace,
                Labels = new Dictionary<string, string> { ["app"] = req.Name },
            },
            Spec = new V1ServiceSpec
            {
                Selector = new Dictionary<string, string> { ["app"] = req.Name, ["slot"] = canarySlot },
                Ports = new List<V1ServicePort> { new() { Port = req.ContainerPort, TargetPort = req.ContainerPort } },
            },
        }), CanaryName(req.Name), req.Namespace, fieldManager: FieldManager, force: true, cancellationToken: ct).ConfigureAwait(false);

    private Task ApplyPrimaryIngressAsync(IKubernetes client, RolloutDeployRequest req, CancellationToken ct) =>
        client.NetworkingV1.PatchNamespacedIngressAsync(
            Apply(BuildIngress(req, req.Name, service: req.Name, canaryWeight: null)),
            req.Name, req.Namespace, fieldManager: FieldManager, force: true, cancellationToken: ct);

    private Task ApplyCanaryIngressAsync(IKubernetes client, RolloutDeployRequest req, int weight, CancellationToken ct) =>
        client.NetworkingV1.PatchNamespacedIngressAsync(
            Apply(BuildIngress(req, CanaryName(req.Name), service: CanaryName(req.Name), canaryWeight: weight)),
            CanaryName(req.Name), req.Namespace, fieldManager: FieldManager, force: true, cancellationToken: ct);

    private V1Ingress BuildIngress(RolloutDeployRequest req, string ingressName, string service, int? canaryWeight)
    {
        var host = $"{req.Name}.{_options.CurrentValue.AppIngressDomain}";
        var annotations = canaryWeight is { } w
            ? new Dictionary<string, string>
            {
                ["nginx.ingress.kubernetes.io/canary"] = "true",
                ["nginx.ingress.kubernetes.io/canary-weight"] = w.ToString(),
            }
            : null;
        return new V1Ingress
        {
            ApiVersion = "networking.k8s.io/v1", Kind = "Ingress",
            Metadata = new V1ObjectMeta
            {
                Name = ingressName, NamespaceProperty = req.Namespace,
                Labels = new Dictionary<string, string> { ["app"] = req.Name },
                Annotations = annotations,
            },
            Spec = new V1IngressSpec
            {
                IngressClassName = _options.CurrentValue.IngressClassName,
                Rules = new List<V1IngressRule>
                {
                    new()
                    {
                        Host = host,
                        Http = new V1HTTPIngressRuleValue
                        {
                            Paths = new List<V1HTTPIngressPath>
                            {
                                new()
                                {
                                    Path = "/", PathType = "Prefix",
                                    Backend = new V1IngressBackend
                                    {
                                        Service = new V1IngressServiceBackend
                                        {
                                            Name = service,
                                            Port = new V1ServiceBackendPort { Number = req.ContainerPort },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private async Task RetireSlotAsync(IKubernetes client, string ns, string name, string oldSlot, string newSlot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(oldSlot) || oldSlot == newSlot) return;
        var old = SlotName(name, oldSlot);
        if (_options.CurrentValue.RolloutDeleteRetiredSlot)
            await IgnoreNotFoundAsync(() => client.AppsV1.DeleteNamespacedDeploymentAsync(old, ns, cancellationToken: ct)).ConfigureAwait(false);
        else
            await ScaleAsync(client, ns, old, 0, ct).ConfigureAwait(false);
    }

    private static async Task ScaleAsync(IKubernetes client, string ns, string deployment, int replicas, CancellationToken ct) =>
        await IgnoreNotFoundAsync(() => client.AppsV1.PatchNamespacedDeploymentScaleAsync(
            MergePatch($"{{\"spec\":{{\"replicas\":{replicas}}}}}"), deployment, ns, cancellationToken: ct)).ConfigureAwait(false);

    /// <summary>The slot pinned in the Service selector (blue-green), or null if there's no Service / no slot.</summary>
    private static async Task<string?> ResolveSelectorSlotAsync(IKubernetes client, string ns, string name, CancellationToken ct)
    {
        try
        {
            var svc = await client.CoreV1.ReadNamespacedServiceAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
            return svc.Spec?.Selector is { } sel && sel.TryGetValue("slot", out var slot) && !string.IsNullOrWhiteSpace(slot) ? slot : null;
        }
        catch (HttpOperationException e) when ((int)e.Response.StatusCode == 404) { return null; }
    }

    /// <summary>The active slot recorded in the Service annotation (canary), or null if there's no Service.</summary>
    private static async Task<string?> ResolveAnnotationSlotAsync(IKubernetes client, string ns, string name, CancellationToken ct)
    {
        try
        {
            var svc = await client.CoreV1.ReadNamespacedServiceAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
            return svc.Metadata?.Annotations is { } ann && ann.TryGetValue(ActiveSlotAnnotation, out var slot) && !string.IsNullOrWhiteSpace(slot) ? slot : null;
        }
        catch (HttpOperationException e) when ((int)e.Response.StatusCode == 404) { return null; }
    }

    private async Task<bool> HealthGateAsync(IKubernetes client, RolloutDeployRequest req, string slot, CancellationToken ct, int? desiredOverride = null)
    {
        var o = _options.CurrentValue;
        var timeout = o.RolloutHealthTimeoutSeconds > 0 ? o.RolloutHealthTimeoutSeconds : Math.Max(1, o.ReadinessTimeoutSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeout);
        var desired = Math.Max(1, desiredOverride ?? req.Replicas);
        var deployName = SlotName(req.Name, slot);
        var threshold = Math.Max(1, o.RolloutRestartThreshold);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = (await client.AppsV1.ReadNamespacedDeploymentStatusAsync(deployName, req.Namespace, cancellationToken: ct).ConfigureAwait(false)).Status;
            if ((status?.AvailableReplicas ?? 0) >= desired) return true;

            var pods = await client.CoreV1.ListNamespacedPodAsync(req.Namespace, labelSelector: $"app={req.Name},slot={slot}", cancellationToken: ct).ConfigureAwait(false);
            if (pods.Items.Any(p => (p.Status?.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0) >= threshold))
            {
                _logger.LogWarning("[k8s] health gate: {Deploy} has a crash-looping pod (>= {Threshold} restarts).", deployName, threshold);
                return false;
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, o.ReadinessPollSeconds)), ct).ConfigureAwait(false);
        }
        return false;
    }

    private static V1Deployment BuildDeployment(RolloutDeployRequest req, string slot, int replicas)
    {
        var labels = new Dictionary<string, string> { ["app"] = req.Name, ["slot"] = slot, ["app.kubernetes.io/managed-by"] = FieldManager };
        return new V1Deployment
        {
            ApiVersion = "apps/v1", Kind = "Deployment",
            Metadata = new V1ObjectMeta { Name = SlotName(req.Name, slot), NamespaceProperty = req.Namespace, Labels = labels },
            Spec = new V1DeploymentSpec
            {
                Replicas = Math.Max(1, replicas),
                Selector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["app"] = req.Name, ["slot"] = slot } },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container>
                        {
                            new()
                            {
                                Name = req.Name,
                                Image = req.Image,
                                Ports = new List<V1ContainerPort> { new() { ContainerPort = req.ContainerPort } },
                                Env = req.EnvVars.Select(kv => new V1EnvVar { Name = kv.Key, Value = kv.Value }).ToList(),
                            },
                        },
                        ImagePullSecrets = string.IsNullOrWhiteSpace(req.ImagePullSecret)
                            ? null
                            : new List<V1LocalObjectReference> { new() { Name = req.ImagePullSecret } },
                    },
                },
            },
        };
    }

    private static string SlotName(string name, string slot) => $"{name}-{slot}";
    private static string CanaryName(string name) => $"{name}-canary";
    private static string Other(string? slot) => slot == Blue ? Green : Blue;
    private static V1Patch Apply(object body) => new(body, V1Patch.PatchType.ApplyPatch);
    private static V1Patch MergePatch(string json) => new(json, V1Patch.PatchType.MergePatch);

    private static async Task IgnoreConflictAsync(Func<Task> op)
    { try { await op().ConfigureAwait(false); } catch (HttpOperationException e) when ((int)e.Response.StatusCode is 409) { } }

    private static async Task IgnoreNotFoundAsync(Func<Task> op)
    { try { await op().ConfigureAwait(false); } catch (HttpOperationException e) when ((int)e.Response.StatusCode is 404) { } }

    private static Exception Wrap(Exception ex, string what) => ex switch
    {
        DeploymentStepException => ex,
        HttpOperationException http => new DeploymentStepException(
            (int)http.Response.StatusCode switch { 401 or 403 => StepFailureKind.CloudRunAuth, 404 => StepFailureKind.Config, _ => StepFailureKind.Unknown },
            $"{what} failed ({(int)http.Response.StatusCode}): {FirstLine(http.Response.Content)}", http),
        _ => new DeploymentStepException(StepFailureKind.Unknown, $"{what} failed: {ex.Message}", ex),
    };

    private static string FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "no detail";
        var nl = s.IndexOfAny(['\r', '\n']);
        return (nl >= 0 ? s[..nl] : s).Trim();
    }
}
