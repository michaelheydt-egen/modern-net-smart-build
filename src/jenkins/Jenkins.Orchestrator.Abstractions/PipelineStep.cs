namespace Jenkins.Orchestrator;

/// <summary>
/// One step in a pipeline. <see cref="UpstreamJob"/> identifies the prior step
/// whose build number is forwarded into this step as <c>SOURCE_BUILD_NUMBER</c>.
/// </summary>
public sealed record PipelineStep(
    string JobName,
    string? UpstreamJob = null,
    IReadOnlyDictionary<string, string>? AdditionalParameters = null);
