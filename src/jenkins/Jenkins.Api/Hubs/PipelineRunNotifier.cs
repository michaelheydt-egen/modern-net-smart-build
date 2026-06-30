using Jenkins.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Jenkins.Api.Hubs;

/// <summary>
/// <see cref="IPipelineRunNotifier"/> over the SignalR <see cref="PipelineRunHub"/> — the bridge
/// from the Infrastructure executor to subscribed clients. Pushes to the run's group.
/// </summary>
internal sealed class PipelineRunNotifier : IPipelineRunNotifier
{
    private readonly IHubContext<PipelineRunHub> _hub;

    public PipelineRunNotifier(IHubContext<PipelineRunHub> hub) => _hub = hub;

    public Task StepChangedAsync(Guid runId, PipelineRunStepUpdate step, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(PipelineRunHub.GroupName(runId)).SendAsync(
            "StepChanged",
            new { runId, step.JobName, step.Status, step.BuildNumber, step.Reason },
            cancellationToken);

    public Task ConsoleAppendedAsync(Guid runId, string jobName, int buildNumber, string text, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(PipelineRunHub.GroupName(runId)).SendAsync(
            "ConsoleAppended",
            new { runId, jobName, buildNumber, text },
            cancellationToken);

    public Task RunSettledAsync(Guid runId, string status, string? failureReason, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(PipelineRunHub.GroupName(runId)).SendAsync(
            "RunSettled",
            new { runId, status, failureReason },
            cancellationToken);

    public Task RunCompletedAsync(Guid runId, string pipelineName, string status, string? failureReason, CancellationToken cancellationToken = default)
        => _hub.Clients.All.SendAsync(
            "RunCompleted",
            new { runId, pipelineName, status, failureReason },
            cancellationToken);
}
