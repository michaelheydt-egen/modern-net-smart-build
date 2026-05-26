namespace Jenkins.Orchestrator;

public interface IPipelineOrchestrator
{
    /// <summary>
    /// Runs the steps sequentially. Each step's build number is propagated to its declared
    /// downstream via <c>SOURCE_BUILD_NUMBER</c>. Stops on the first non-success result or
    /// exception. Cancellation attempts to stop the in-flight Jenkins build.
    /// </summary>
    Task<PipelineRun> RunAsync(
        IEnumerable<PipelineStep> steps,
        IProgress<PipelineEvent>? progress = null,
        CancellationToken cancellationToken = default);
}
