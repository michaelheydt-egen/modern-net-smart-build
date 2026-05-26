namespace Jenkins.Orchestrator;

public sealed record PipelineRun(
    IReadOnlyList<PipelineRunStep> Steps,
    bool Success,
    string? FailureReason);
