using Deployment.Domain.Common;
using Deployment.Domain.Runs.Events;

namespace Deployment.Domain.Runs;

/// <summary>
/// One execution of a mapping's recipe: promote the container to GAR, deploy to Cloud Run. Created
/// Pending (raises <see cref="DeploymentRunRequested"/> to drive the executor), then Running →
/// Succeeded/Failed. Carries enough snapshot to run without re-loading the catalog and to emit the
/// success integration event.
/// </summary>
public sealed class DeploymentRun : AggregateRoot<Guid>
{
    public Guid MappingId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid EnvironmentId { get; private set; }

    public string ServiceName { get; private set; }
    public string ContainerName { get; private set; }
    public string Version { get; private set; }
    public string SourceRef { get; private set; }

    // Target snapshot (so the executor + event need no catalog re-read).
    public string GcpProject { get; private set; }
    public string Region { get; private set; }
    public string GarRepository { get; private set; }
    public string? CloudRunServiceName { get; private set; }

    // Kubernetes target snapshot (set for KubernetesApply mappings).
    public string? KubernetesContext { get; private set; }
    public string? KubernetesNamespace { get; private set; }
    public Mappings.KubernetesSpec? KubernetesSpec { get; private set; }

    public DeploymentTrigger Trigger { get; private set; }
    public string TriggeredBy { get; private set; }
    public DeploymentRunStatus Status { get; private set; }

    /// <summary>The GAR image reference produced by the GarPush step.</summary>
    public string? RemoteImageRef { get; private set; }

    /// <summary>The Cloud Run revision that became ready.</summary>
    public string? CloudRunRevision { get; private set; }

    /// <summary>The Kubernetes resource (e.g. <c>deployment/foo</c>) applied by the KubernetesApply step.</summary>
    public string? KubernetesResource { get; private set; }

    // Blue-green rollout context (set when a manual-promotion run parks in AwaitingPromotion).
    /// <summary>The freshly-deployed slot awaiting promotion (e.g. <c>green</c>).</summary>
    public string? RolloutGreenSlot { get; private set; }
    /// <summary>The currently-live slot the Service still points at (e.g. <c>blue</c>).</summary>
    public string? RolloutActiveSlot { get; private set; }
    /// <summary>Canary only: the traffic weight (%) the canary Ingress is currently routing to the new slot.</summary>
    public int? RolloutCanaryWeight { get; private set; }
    /// <summary>Who promoted or rolled back the parked rollout.</summary>
    public string? DecisionBy { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Per-step results, ordered. Stored as a JSON column.</summary>
    public IReadOnlyList<RunStepResult> Steps { get; private set; } = Array.Empty<RunStepResult>();

    private DeploymentRun()
    {
        ServiceName = string.Empty;
        ContainerName = string.Empty;
        Version = string.Empty;
        SourceRef = string.Empty;
        GcpProject = string.Empty;
        Region = string.Empty;
        GarRepository = string.Empty;
        TriggeredBy = string.Empty;
    }

    public DeploymentRun(
        Guid id, Guid mappingId, Guid serviceId, Guid environmentId,
        string serviceName, string containerName, string version, string sourceRef,
        string gcpProject, string region, string garRepository, string? cloudRunServiceName,
        string? kubernetesContext, string? kubernetesNamespace, Mappings.KubernetesSpec? kubernetesSpec,
        DeploymentTrigger trigger, string triggeredBy, DateTimeOffset requestedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));

        Id = id;
        MappingId = mappingId;
        ServiceId = serviceId;
        EnvironmentId = environmentId;
        ServiceName = serviceName?.Trim() ?? string.Empty;
        ContainerName = containerName?.Trim() ?? string.Empty;
        Version = version?.Trim() ?? string.Empty;
        SourceRef = sourceRef?.Trim() ?? string.Empty;
        GcpProject = gcpProject?.Trim() ?? string.Empty;
        Region = region?.Trim() ?? string.Empty;
        GarRepository = garRepository?.Trim() ?? string.Empty;
        CloudRunServiceName = string.IsNullOrWhiteSpace(cloudRunServiceName) ? null : cloudRunServiceName.Trim();
        KubernetesContext = string.IsNullOrWhiteSpace(kubernetesContext) ? null : kubernetesContext.Trim();
        KubernetesNamespace = string.IsNullOrWhiteSpace(kubernetesNamespace) ? null : kubernetesNamespace.Trim();
        KubernetesSpec = kubernetesSpec;
        Trigger = trigger;
        TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "system" : triggeredBy.Trim();
        Status = DeploymentRunStatus.Pending;
        RequestedAtUtc = requestedAtUtc;

        RaiseEvent(new DeploymentRunRequested(Id, MappingId, ServiceId, EnvironmentId, requestedAtUtc));
    }

    public void Start() { if (Status == DeploymentRunStatus.Pending) Status = DeploymentRunStatus.Running; }

    public void RecordStep(int order, Mappings.DeploymentStepKind kind, string status, string? detail, StepFailureKind? failureKind = null)
    {
        Steps = Steps.Where(s => s.Order != order)
            .Append(new RunStepResult(order, kind, status, detail, failureKind))
            .OrderBy(s => s.Order)
            .ToArray();
    }

    public void SetRemoteImageRef(string remoteRef) => RemoteImageRef = remoteRef?.Trim();
    public void SetCloudRunRevision(string revision) => CloudRunRevision = revision?.Trim();
    public void SetKubernetesResource(string resource) => KubernetesResource = resource?.Trim();

    public void Succeed(DateTimeOffset completedAtUtc)
    {
        if (Status is DeploymentRunStatus.Succeeded or DeploymentRunStatus.Failed) return;
        Status = DeploymentRunStatus.Succeeded;
        CompletedAtUtc = completedAtUtc;
        RaiseEvent(new DeploymentRunSucceeded(
            Id, ServiceId, EnvironmentId, ServiceName, ContainerName, Version,
            GcpProject, Region, CloudRunServiceName ?? string.Empty, RemoteImageRef ?? string.Empty,
            CloudRunRevision ?? KubernetesResource ?? string.Empty, completedAtUtc));
    }

    public void Fail(string reason, DateTimeOffset completedAtUtc, string? failedStep = null, string? category = null)
    {
        if (Status is DeploymentRunStatus.Succeeded or DeploymentRunStatus.Failed) return;
        Status = DeploymentRunStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown error." : reason.Trim();
        CompletedAtUtc = completedAtUtc;
        RaiseEvent(new DeploymentRunFailed(Id, ServiceId, EnvironmentId, FailureReason, failedStep, category, completedAtUtc));
    }

    /// <summary>Blue-green (manual promotion): green is deployed + healthy but the Service still points at
    /// the active slot. Parks the run until a human promotes or rolls back. Terminal states are guarded.</summary>
    public void AwaitPromotion(string greenSlot, string activeSlot, DateTimeOffset occurredAtUtc, int? canaryWeight = null)
    {
        if (Status is DeploymentRunStatus.Succeeded or DeploymentRunStatus.Failed or DeploymentRunStatus.RolledBack) return;
        Status = DeploymentRunStatus.AwaitingPromotion;
        RolloutGreenSlot = string.IsNullOrWhiteSpace(greenSlot) ? null : greenSlot.Trim();
        RolloutActiveSlot = string.IsNullOrWhiteSpace(activeSlot) ? null : activeSlot.Trim();
        RolloutCanaryWeight = canaryWeight;
    }

    /// <summary>Canary progressive ramp: record a new traffic weight while staying parked in AwaitingPromotion.</summary>
    public void AdvanceCanary(int weight)
    {
        if (Status != DeploymentRunStatus.AwaitingPromotion) return;
        RolloutCanaryWeight = weight;
    }

    /// <summary>Promote a parked rollout — the caller has already swapped the Service selector to green.</summary>
    public void PromoteRollout(string promotedBy, DateTimeOffset completedAtUtc)
    {
        if (Status != DeploymentRunStatus.AwaitingPromotion) return;
        DecisionBy = string.IsNullOrWhiteSpace(promotedBy) ? "unknown" : promotedBy.Trim();
        Succeed(completedAtUtc);
    }

    /// <summary>Roll a parked rollout back — the caller has already deleted the green slot; blue stayed live.</summary>
    public void RollbackRollout(string rolledBackBy, string? reason, DateTimeOffset completedAtUtc)
    {
        if (Status != DeploymentRunStatus.AwaitingPromotion) return;
        Status = DeploymentRunStatus.RolledBack;
        DecisionBy = string.IsNullOrWhiteSpace(rolledBackBy) ? "unknown" : rolledBackBy.Trim();
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Rolled back." : reason.Trim();
        CompletedAtUtc = completedAtUtc;
        RaiseEvent(new DeploymentRunFailed(Id, ServiceId, EnvironmentId, FailureReason, "promote", "RolledBack", completedAtUtc));
    }
}
