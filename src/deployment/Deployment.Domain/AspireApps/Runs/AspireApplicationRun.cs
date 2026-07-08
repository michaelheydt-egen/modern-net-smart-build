using Deployment.Domain.Common;
using Deployment.Domain.Runs;
using Deployment.Domain.AspireApps.Runs.Events;

namespace Deployment.Domain.AspireApps.Runs;

/// <summary>
/// One deployment of an <see cref="AspireApplication"/> via Aspir8. Created Pending (raises
/// <see cref="AspireApplicationRunRequested"/> to drive the executor), then Running →
/// Succeeded/Failed. Snapshots the target coordinates so the executor needs no catalog re-read, and
/// captures the aspirate CLI output as <see cref="Log"/>. Reuses <see cref="DeploymentRunStatus"/>.
/// </summary>
public sealed class AspireApplicationRun : AggregateRoot<Guid>
{
    public Guid ApplicationId { get; private set; }
    public string ApplicationName { get; private set; }

    // Target snapshot.
    public Guid EnvironmentId { get; private set; }
    public string EnvironmentName { get; private set; }
    public string KubeContext { get; private set; }
    public string Namespace { get; private set; }
    public string ManifestSource { get; private set; }
    public string? Version { get; private set; }

    public DeploymentRunStatus Status { get; private set; }
    public string TriggeredBy { get; private set; }
    public string? Log { get; private set; }
    public string? FailureReason { get; private set; }
    /// <summary>Who approved or rejected the run (when it targeted a protected environment).</summary>
    public string? DecisionBy { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Images this run put on the cluster (workload → image ref), snapshotted on success. Empty until then.</summary>
    public IReadOnlyList<DeployedImage> DeployedImages { get; private set; }

    private AspireApplicationRun()
    {
        ApplicationName = string.Empty;
        EnvironmentName = string.Empty;
        KubeContext = string.Empty;
        Namespace = string.Empty;
        ManifestSource = string.Empty;
        TriggeredBy = string.Empty;
        DeployedImages = Array.Empty<DeployedImage>();
    }

    public AspireApplicationRun(
        Guid id, Guid applicationId, string applicationName,
        Guid environmentId, string environmentName, string kubeContext, string @namespace,
        string manifestSource, string? version, string triggeredBy, DateTimeOffset requestedAtUtc,
        bool requiresApproval = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        Id = id;
        ApplicationId = applicationId;
        ApplicationName = applicationName?.Trim() ?? string.Empty;
        EnvironmentId = environmentId;
        EnvironmentName = environmentName?.Trim() ?? string.Empty;
        KubeContext = kubeContext?.Trim() ?? string.Empty;
        Namespace = @namespace?.Trim() ?? string.Empty;
        ManifestSource = manifestSource?.Trim() ?? string.Empty;
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "manual" : triggeredBy.Trim();
        RequestedAtUtc = requestedAtUtc;
        DeployedImages = Array.Empty<DeployedImage>();

        // A run targeting a protected environment parks for approval and does NOT raise the requested
        // event, so the executor won't apply it until Approve() transitions it to Pending.
        if (requiresApproval)
        {
            Status = DeploymentRunStatus.AwaitingApproval;
        }
        else
        {
            Status = DeploymentRunStatus.Pending;
            RaiseEvent(new AspireApplicationRunRequested(Id, ApplicationId, requestedAtUtc));
        }
    }

    /// <summary>Approve a parked run → Pending, and raise the request that drives the executor.</summary>
    public void Approve(string approvedBy, DateTimeOffset occurredAtUtc)
    {
        if (Status != DeploymentRunStatus.AwaitingApproval) return;
        Status = DeploymentRunStatus.Pending;
        DecisionBy = string.IsNullOrWhiteSpace(approvedBy) ? "unknown" : approvedBy.Trim();
        RaiseEvent(new AspireApplicationRunRequested(Id, ApplicationId, occurredAtUtc));
    }

    /// <summary>Reject a parked run — terminal, nothing is applied. Not a deploy failure, so no failure event.</summary>
    public void Reject(string rejectedBy, string? reason, DateTimeOffset occurredAtUtc)
    {
        if (Status != DeploymentRunStatus.AwaitingApproval) return;
        Status = DeploymentRunStatus.Rejected;
        DecisionBy = string.IsNullOrWhiteSpace(rejectedBy) ? "unknown" : rejectedBy.Trim();
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Rejected." : reason.Trim();
        CompletedAtUtc = occurredAtUtc;
    }

    public void Start() { if (Status == DeploymentRunStatus.Pending) Status = DeploymentRunStatus.Running; }

    public void Succeed(string? log, IReadOnlyList<DeployedImage>? deployedImages, DateTimeOffset completedAtUtc)
    {
        if (Status is DeploymentRunStatus.Succeeded or DeploymentRunStatus.Failed) return;
        Status = DeploymentRunStatus.Succeeded;
        Log = Trim(log);
        if (deployedImages is { Count: > 0 })
            DeployedImages = deployedImages.Where(i => !string.IsNullOrWhiteSpace(i.Image)).ToArray();
        CompletedAtUtc = completedAtUtc;
        RaiseEvent(new AspireApplicationRunSucceeded(Id, ApplicationId, ApplicationName, Namespace, completedAtUtc));
    }

    public void Fail(string reason, string? log, DateTimeOffset completedAtUtc)
    {
        if (Status is DeploymentRunStatus.Succeeded or DeploymentRunStatus.Failed) return;
        Status = DeploymentRunStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown error." : reason.Trim();
        Log = Trim(log);
        CompletedAtUtc = completedAtUtc;
        RaiseEvent(new AspireApplicationRunFailed(Id, ApplicationId, ApplicationName, FailureReason, completedAtUtc));
    }

    private static string? Trim(string? log)
        => string.IsNullOrEmpty(log) ? log : (log.Length > 16000 ? log[^16000..] : log);
}
