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
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<DeploymentRunExecutor> logger,
        CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(evt.RunId, ct).ConfigureAwait(false);
        if (run is null || run.Status != DeploymentRunStatus.Pending) return;

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
        };

        var failed = false;
        string? failReason = null;

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
                run.RecordStep(step.Order, step.Kind, outcome.Success ? "Succeeded" : "Failed", outcome.Detail);
                if (!outcome.Success)
                {
                    failed = true;
                    failReason = $"step {step.Kind} failed: {outcome.Detail}";
                    break;
                }
            }
            catch (Exception ex)
            {
                run.RecordStep(step.Order, step.Kind, "Failed", ex.Message);
                failed = true;
                failReason = $"step {step.Kind} threw: {ex.Message}";
                logger.LogError(ex, "[deploy] Run {Run} step {Kind} threw.", run.Id, step.Kind);
                break;
            }
        }

        if (ctx.RemoteImageRef is { Length: > 0 }) run.SetRemoteImageRef(ctx.RemoteImageRef);
        if (ctx.CloudRunRevision is { Length: > 0 }) run.SetCloudRunRevision(ctx.CloudRunRevision);

        var now = clock.GetUtcNow();
        if (failed) run.Fail(failReason ?? "Deployment failed.", now);
        else run.Succeed(now);

        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("[deploy] Run {Run} -> {Status}.", run.Id, run.Status);
    }
}
