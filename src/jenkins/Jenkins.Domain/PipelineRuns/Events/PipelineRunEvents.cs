using Jenkins.Domain.Common;

namespace Jenkins.Domain.PipelineRuns.Events;

public sealed record PipelineRunStarted(
    Guid RunId,
    Guid PipelineId,
    string PipelineName,
    Guid? RepositoryId,
    string TriggeredBy,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>One step (Jenkins job) of the run completed successfully — translated to the
/// <c>Ci.PipelineStepCompleted</c> integration event.</summary>
public sealed record PipelineRunStepSucceeded(
    Guid RunId,
    Guid PipelineId,
    string PipelineName,
    Guid? RepositoryId,
    string JobName,
    int BuildNumber,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>The whole run completed successfully — translated to the
/// <c>Ci.PipelineCompleted</c> integration event.</summary>
public sealed record PipelineRunSucceeded(
    Guid RunId,
    Guid PipelineId,
    string PipelineName,
    Guid? RepositoryId,
    string TriggeredBy,
    IReadOnlyList<PipelineRunStepRecord> Steps,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record PipelineRunFailed(
    Guid RunId,
    string FailureReason,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>The run was cancelled in flight — translated to the <c>Ci.PipelineCancelled</c>
/// integration event. Carries the steps that completed before cancellation.</summary>
public sealed record PipelineRunCancelled(
    Guid RunId,
    Guid PipelineId,
    string PipelineName,
    Guid? RepositoryId,
    string TriggeredBy,
    IReadOnlyList<PipelineRunStepRecord> Steps,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
