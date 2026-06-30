using Deployment.Application.Abstractions;
using Deployment.Domain.Runs;
using Deployment.Domain.Runs.Events;
using Wolverine.Attributes;

namespace Deployment.Application.Features.Integration;

/// <summary>
/// When a deployment run settles, push a SignalR broadcast (via <see cref="IDeploymentRunNotifier"/>)
/// so the UI raises an app-wide completion toast. Covers both manual and auto deploys (both raise
/// these domain events). Distinct from the integration-event translator, which puts facts on the bus.
///
/// [WolverineHandler] is REQUIRED: Wolverine only auto-discovers types whose names end in
/// "Handler"/"Consumer", so this "*Notifier" is invisible without it.
/// </summary>
[WolverineHandler]
public sealed class DeploymentRunCompletedNotifier
{
    public Task Handle(DeploymentRunSucceeded e, IDeploymentRunNotifier notifier, CancellationToken ct)
        => notifier.RunCompletedAsync(
            e.RunId,
            "Succeeded",
            $"Deployed {e.ServiceName} {e.Version}",
            $"{e.CloudRunServiceName} · {e.Region}",
            ct);

    public Task Handle(DeploymentRunFailed e, IDeploymentRunNotifier notifier, CancellationToken ct)
        => notifier.RunCompletedAsync(
            e.RunId,
            "Failed",
            BuildFailureTitle(e.FailedStep, e.Category),
            e.Reason,
            ct);

    /// <summary>"Deploy failed at GarPush — registry auth" when we know the step + category; degrades gracefully.</summary>
    private static string BuildFailureTitle(string? failedStep, string? category)
    {
        var at = string.IsNullOrWhiteSpace(failedStep) ? "" : $" at {failedStep}";
        var why = Humanize(category);
        return why is null ? $"Deploy failed{at}" : $"Deploy failed{at} — {why}";
    }

    private static string? Humanize(string? category) =>
        Enum.TryParse<StepFailureKind>(category, out var kind)
            ? kind switch
            {
                StepFailureKind.ToolMissing => "tooling missing",
                StepFailureKind.RegistryAuth => "registry auth",
                StepFailureKind.RegistryError => "registry error",
                StepFailureKind.CloudRunAuth => "Cloud Run auth",
                StepFailureKind.CloudRunNotFound => "service not found",
                StepFailureKind.Timeout => "timed out",
                StepFailureKind.Config => "configuration",
                _ => null,
            }
            : null;
}
