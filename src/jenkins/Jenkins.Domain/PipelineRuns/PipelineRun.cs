using Jenkins.Domain.Common;
using Jenkins.Domain.PipelineRuns.Events;

namespace Jenkins.Domain.PipelineRuns;

/// <summary>
/// A server-side execution of an orchestration pipeline. Created Running, then the executor
/// records each successful step and settles the run Succeeded/Failed/Cancelled. Raises
/// per-step and whole-run domain events that translate to integration events on the bus.
/// </summary>
public sealed class PipelineRun : AggregateRoot<Guid>
{
    public Guid PipelineId { get; private set; }
    public string PipelineName { get; private set; }
    public Guid? RepositoryId { get; private set; }
    public string TriggeredBy { get; private set; }
    public PipelineRunStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private readonly List<PipelineRunStepRecord> _steps = new();
    public IReadOnlyList<PipelineRunStepRecord> Steps => _steps.OrderBy(s => s.Order).ToList();

    private PipelineRun()
    {
        PipelineName = string.Empty;
        TriggeredBy = string.Empty;
    }

    public PipelineRun(
        Guid id,
        Guid pipelineId,
        string pipelineName,
        Guid? repositoryId,
        string triggeredBy,
        DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (pipelineId == Guid.Empty) throw new ArgumentException("PipelineId cannot be empty.", nameof(pipelineId));
        if (string.IsNullOrWhiteSpace(pipelineName))
            throw new ArgumentException("PipelineName cannot be empty.", nameof(pipelineName));

        Id = id;
        PipelineId = pipelineId;
        PipelineName = pipelineName.Trim();
        RepositoryId = repositoryId;
        TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "unknown" : triggeredBy.Trim();
        Status = PipelineRunStatus.Running;
        StartedAtUtc = startedAtUtc;

        RaiseEvent(new PipelineRunStarted(Id, PipelineId, PipelineName, RepositoryId, TriggeredBy, startedAtUtc));
    }

    /// <summary>Record a step that finished successfully (raises a per-step domain event).</summary>
    public void RecordStepSucceeded(int order, string jobName, int buildNumber, DateTimeOffset occurredAtUtc)
    {
        if (Status != PipelineRunStatus.Running) return;
        _steps.Add(new PipelineRunStepRecord(order, jobName?.Trim() ?? string.Empty, buildNumber, "Success"));
        RaiseEvent(new PipelineRunStepSucceeded(Id, PipelineId, PipelineName, RepositoryId, jobName!.Trim(), buildNumber, occurredAtUtc));
    }

    public void Succeed(DateTimeOffset occurredAtUtc)
    {
        if (Status != PipelineRunStatus.Running) return;
        Status = PipelineRunStatus.Succeeded;
        CompletedAtUtc = occurredAtUtc;
        RaiseEvent(new PipelineRunSucceeded(Id, PipelineId, PipelineName, RepositoryId, TriggeredBy, Steps, occurredAtUtc));
    }

    public void Fail(string reason, DateTimeOffset occurredAtUtc)
    {
        if (Status != PipelineRunStatus.Running) return;
        Status = PipelineRunStatus.Failed;
        CompletedAtUtc = occurredAtUtc;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Pipeline failed." : reason.Trim();
        RaiseEvent(new PipelineRunFailed(Id, FailureReason, occurredAtUtc));
    }

    public void Cancel(DateTimeOffset occurredAtUtc)
    {
        if (Status != PipelineRunStatus.Running) return;
        Status = PipelineRunStatus.Cancelled;
        CompletedAtUtc = occurredAtUtc;
        RaiseEvent(new PipelineRunCancelled(Id, PipelineId, PipelineName, RepositoryId, TriggeredBy, Steps, occurredAtUtc));
    }
}
