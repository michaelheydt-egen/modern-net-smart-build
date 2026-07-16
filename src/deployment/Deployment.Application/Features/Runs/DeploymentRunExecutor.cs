using Microsoft.Extensions.Logging;
using Deployment.Application.Abstractions;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Runs.Events;
using Wolverine.Attributes;

namespace Deployment.Application.Features.Runs;

/// <summary>
/// Executes a requested deployment: runs the mapping's ordered steps (GarPush → CloudRunDeploy by
/// default), recording each step's outcome, then settles the run. Discovered by Wolverine as a
/// handler for <see cref="DeploymentRunRequested"/>, so it runs asynchronously off the request that
/// created the run, with the bus's retry + SQL outbox.
///
/// [WolverineHandler] is REQUIRED: Wolverine's convention only auto-discovers types whose names end
/// in "Handler"/"Consumer", so a "*Executor" is invisible without it (the run stays Pending because
/// nothing consumes <see cref="DeploymentRunRequested"/>).
/// </summary>
[WolverineHandler]
public sealed class DeploymentRunExecutor
{
    public async Task Handle(
        DeploymentRunRequested evt,
        IDeploymentRunRepository runs,
        IDeploymentMappingRepository mappings,
        IStepExecutorRegistry stepExecutors,
        Observability.DeploymentTelemetry telemetry,
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<DeploymentRunExecutor> logger,
        CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(evt.RunId, ct).ConfigureAwait(false);
        if (run is null || run.Status != DeploymentRunStatus.Pending) return;

        using var activity = Observability.DeploymentTelemetry.Activity.StartActivity("deploy.run");
        activity?.SetTag("deploy.service", run.ServiceName);
        activity?.SetTag("deploy.version", run.Version);

        run.Start();

        var mapping = await mappings.GetByIdAsync(run.MappingId, ct).ConfigureAwait(false);
        var steps = mapping?.Steps ?? DeploymentMapping.DefaultSteps();

        var ctx = new DeploymentContext
        {
            ContainerName = run.ContainerName,
            Version = run.Version,
            SourceRef = run.SourceRef,
            GcpProject = run.GcpProject,
            Region = run.Region,
            GarRepository = run.GarRepository,
            CloudRunServiceName = run.CloudRunServiceName,
            KubernetesContext = run.KubernetesContext,
            KubernetesNamespace = run.KubernetesNamespace,
            Kubernetes = run.KubernetesSpec,
        };

        var failed = false;
        string? failReason = null;
        StepFailureKind? failureKind = null;
        Deployment.Domain.Mappings.DeploymentStepKind? failedStep = null;
        var paused = false;
        string? pausedGreenSlot = null, pausedActiveSlot = null;
        int? pausedCanaryWeight = null;

        foreach (var step in steps.OrderBy(s => s.Order))
        {
            if (!stepExecutors.TryGet(step.Kind, out var executor))
            {
                run.RecordStep(step.Order, step.Kind, "Skipped", "no executor registered for this step kind");
                continue;
            }

            try
            {
                var outcome = await executor.ExecuteAsync(ctx, ct).ConfigureAwait(false);

                // Blue-green manual promotion: green is healthy but traffic hasn't cut over. Park the run.
                if (outcome.Paused)
                {
                    run.RecordStep(step.Order, step.Kind, "AwaitingPromotion", outcome.Detail);
                    paused = true;
                    pausedGreenSlot = outcome.GreenSlot;
                    pausedActiveSlot = outcome.ActiveSlot;
                    pausedCanaryWeight = outcome.CanaryWeight;
                    break;
                }

                run.RecordStep(step.Order, step.Kind, outcome.Success ? "Succeeded" : "Failed", outcome.Detail, outcome.FailureKind);
                if (!outcome.Success)
                {
                    failed = true;
                    failedStep = step.Kind;
                    failureKind = outcome.FailureKind ?? StepFailureKind.Unknown;
                    failReason = outcome.Detail ?? "step failed.";
                    break;
                }
            }
            catch (DeploymentStepException ex)
            {
                run.RecordStep(step.Order, step.Kind, "Failed", ex.Message, ex.Kind);
                failed = true;
                failedStep = step.Kind;
                failureKind = ex.Kind;
                failReason = ex.Message;
                logger.LogError(ex, "[deploy] Run {Run} step {Step} failed: {Category}.", run.Id, step.Kind, ex.Kind);
                break;
            }
            catch (Exception ex)
            {
                run.RecordStep(step.Order, step.Kind, "Failed", ex.Message, StepFailureKind.Unknown);
                failed = true;
                failedStep = step.Kind;
                failureKind = StepFailureKind.Unknown;
                failReason = ex.Message;
                logger.LogError(ex, "[deploy] Run {Run} step {Step} threw.", run.Id, step.Kind);
                break;
            }
        }

        if (ctx.RemoteImageRef is { Length: > 0 }) run.SetRemoteImageRef(ctx.RemoteImageRef);
        if (ctx.CloudRunRevision is { Length: > 0 }) run.SetCloudRunRevision(ctx.CloudRunRevision);
        if (ctx.KubernetesResource is { Length: > 0 }) run.SetKubernetesResource(ctx.KubernetesResource);

        var now = clock.GetUtcNow();
        if (paused) run.AwaitPromotion(pausedGreenSlot ?? "green", pausedActiveSlot ?? "blue", now, pausedCanaryWeight);
        else if (failed) run.Fail(failReason ?? "Deployment failed.", now, failedStep?.ToString(), failureKind?.ToString());
        else run.Succeed(now);

        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        activity?.SetTag("deploy.outcome", run.Status.ToString());

        // Metric on a terminal settle only (a parked manual promotion records when it resolves).
        if (!paused)
        {
            var target = run.KubernetesSpec is not null ? "kubernetes" : "cloudrun";
            telemetry.RecordRun(target, run.Status.ToString(), run.KubernetesSpec?.Strategy.ToString(), (now - run.RequestedAtUtc).TotalSeconds);
        }
        logger.LogInformation("[deploy] Run {Run} -> {Status}.", run.Id, run.Status);
    }
}
