namespace Jenkins.Client;

public interface IJenkinsClient
{
    // --- Connectivity ---

    /// <summary>
    /// Hits a lightweight Jenkins endpoint to confirm reachability + credentials.
    /// Throws <see cref="HttpRequestException"/> with a populated <see cref="HttpRequestException.StatusCode"/>
    /// on HTTP errors; throws other exceptions on network / parse / timeout failures.
    /// </summary>
    Task PingAsync(CancellationToken cancellationToken = default);

    // --- Triggering ---

    /// <summary>
    /// Triggers a new build of <paramref name="jobName"/>. Returns the queue item id;
    /// use <see cref="WaitForBuildToStartAsync"/> to resolve it to a build number.
    /// </summary>
    Task<long> StartBuildAsync(
        string jobName,
        IDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);

    // --- Queue tracking ---

    Task<QueueItem> GetQueueItemAsync(long queueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the queue until the item has been assigned an executor (or cancelled).
    /// Returns the resulting build number.
    /// </summary>
    Task<int> WaitForBuildToStartAsync(
        long queueId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    // --- Build tracking ---

    Task<Build> GetBuildAsync(string jobName, int buildNumber, CancellationToken cancellationToken = default);

    Task<Build> GetLastBuildAsync(string jobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the build until <c>Building == false</c>. Returns the final build with its result.
    /// </summary>
    Task<Build> WaitForBuildToFinishAsync(
        string jobName,
        int buildNumber,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task StopBuildAsync(string jobName, int buildNumber, CancellationToken cancellationToken = default);

    // --- Outputs ---

    Task<string> GetConsoleLogAsync(string jobName, int buildNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single archived artifact (relative path under the build's artifacts/) as raw bytes.
    /// Useful for pulling build-info.json or similar manifests in an orchestrator.
    /// </summary>
    Task<byte[]> GetArtifactAsync(
        string jobName,
        int buildNumber,
        string artifactPath,
        CancellationToken cancellationToken = default);

    // --- Convenience: trigger + wait + return final result ---

    /// <summary>
    /// Triggers a build and waits for it to both start and finish. Returns the completed build.
    /// </summary>
    Task<Build> RunBuildAsync(
        string jobName,
        IDictionary<string, string>? parameters = null,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
