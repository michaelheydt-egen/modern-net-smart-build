using Jenkins.Client;

namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// Where the Builds + BuildDetails pages get their data. Two implementations:
///   <see cref="JenkinsLiveBuildStore"/> — every call hits Jenkins (default).
///   (future) SqliteBuildStore — reads from a locally-synced SQLite mirror.
/// The seam lets the persistence layer be turned on later by swapping a single
/// DI registration in <c>Program.cs</c> without touching the pages.
/// </summary>
public interface IBuildStore
{
    /// <summary>
    /// Non-null when a sync layer is in front of Jenkins (i.e. the SQLite store).
    /// Live mode returns <c>null</c> so the UI can surface a "live mode" badge
    /// distinguishably from "mirror, last synced N minutes ago".
    /// </summary>
    BuildSyncStatus? Status { get; }

    /// <summary>
    /// Returns the most recent <paramref name="count"/> builds of
    /// <paramref name="jobName"/>, newest first.
    /// </summary>
    Task<IReadOnlyList<Build>> ListBuildsAsync(string jobName, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full record for one build (basics + artifacts + causes).
    /// Throws if the build does not exist on the configured Jenkins instance.
    /// </summary>
    Task<JenkinsBuildDetails> GetBuildAsync(string jobName, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the parsed <c>build-info.json</c> artifact attached to a build, or
    /// <c>null</c> if the build doesn't have one (older builds predate the artifact).
    /// </summary>
    Task<BuildInfo?> GetBuildInfoAsync(string jobName, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches raw artifact bytes from Jenkins. Always live — artifact content is
    /// not mirrored to SQLite (immutable, potentially large, Jenkins is already
    /// the system of record). Callers are responsible for size-capping rendering.
    /// </summary>
    Task<byte[]> GetArtifactBytesAsync(string jobName, int number, string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot of the sync layer's freshness. Returned by <see cref="IBuildStore.Status"/>
/// when a mirror is active; the layout uses it to render a "synced N minutes ago" pill.
/// </summary>
public sealed record BuildSyncStatus(
    DateTimeOffset LastSyncedAt,
    int BuildsTracked,
    string? LastError);
