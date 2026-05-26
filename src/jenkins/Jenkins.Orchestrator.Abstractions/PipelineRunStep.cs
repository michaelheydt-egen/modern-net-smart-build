using Jenkins.Client;

namespace Jenkins.Orchestrator;

public sealed record PipelineRunStep(
    string JobName,
    int? BuildNumber,
    BuildResult? Result,
    TimeSpan Duration,
    string? Error);
