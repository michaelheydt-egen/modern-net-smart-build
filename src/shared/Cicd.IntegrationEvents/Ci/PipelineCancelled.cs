namespace Cicd.IntegrationEvents.Ci;

/// <summary>
/// An orchestration pipeline run was cancelled in flight. Emitted by the CI service; carries the
/// steps that had completed before cancellation (reuses <see cref="PipelineCompletedStep"/>).
/// </summary>
public sealed record PipelineCancelled(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid RunId,
    Guid PipelineId,
    string PipelineName,
    Guid? RepositoryId,
    string TriggeredBy,
    IReadOnlyList<PipelineCompletedStep> CompletedSteps) : IIntegrationEvent;
