namespace Jenkins.Application.Abstractions;

/// <summary>
/// Hands an enqueued pipeline run to the background executor. Implemented as a singleton
/// in-memory channel in Infrastructure.
/// </summary>
public interface IPipelineRunQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Pushes live run updates to subscribed clients (SignalR). Implemented in the API host where
/// the hub lives; the Infrastructure executor depends only on this port.
/// </summary>
public interface IPipelineRunNotifier
{
    Task StepChangedAsync(Guid runId, PipelineRunStepUpdate step, CancellationToken cancellationToken = default);
    Task ConsoleAppendedAsync(Guid runId, string jobName, int buildNumber, string text, CancellationToken cancellationToken = default);
    Task RunSettledAsync(Guid runId, string status, string? failureReason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcast a terminal run to <em>all</em> connected clients (not the per-run group) so the UI
    /// can raise an app-wide completion toast regardless of which page is open. <paramref name="failureReason"/>
    /// is non-null on a failed run so the toast can show why.
    /// </summary>
    Task RunCompletedAsync(Guid runId, string pipelineName, string status, string? failureReason, CancellationToken cancellationToken = default);
}

public sealed record PipelineRunStepUpdate(string JobName, string Status, int? BuildNumber, string? Reason);

/// <summary>
/// Bounded in-memory console buffer per active run so a (re)connecting client can replay the
/// current output. Written by the executor, read by the hub on subscribe. Singleton.
/// </summary>
public interface IPipelineRunConsoleBuffer
{
    void Append(Guid runId, string jobName, string chunk);
    IReadOnlyList<PipelineRunConsoleSegment> Snapshot(Guid runId);
    void Clear(Guid runId);
}

public sealed record PipelineRunConsoleSegment(string JobName, string Text);

/// <summary>
/// Tracks in-flight runs so a cancel request can stop the specific run (cancelling its token,
/// which the orchestrator uses to stop the in-flight Jenkins build). The executor registers a
/// run on start and forgets it on completion; a cancel command calls <see cref="Cancel"/>.
/// Singleton.
/// </summary>
public interface IPipelineRunCancellation
{
    void Track(Guid runId, CancellationTokenSource cts);
    void Forget(Guid runId);

    /// <summary>Request cancellation of an in-flight run. Returns false if it isn't executing.</summary>
    bool Cancel(Guid runId);
}
