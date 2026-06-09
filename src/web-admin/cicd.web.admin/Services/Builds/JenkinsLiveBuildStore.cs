using System.Net;
using System.Text.Json;
using Jenkins.Client;

namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// Default <see cref="IBuildStore"/> — every call passes through to <see cref="IJenkinsClient"/>.
/// Stateless: no caching, no persistence. Use this when build sync is disabled
/// (or as the fallback while the SQLite mirror is being set up).
/// </summary>
public sealed class JenkinsLiveBuildStore : IBuildStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IJenkinsClient _jenkins;

    /// <summary>Live mode has no sync state; the layout reads this as "live mode".</summary>
    public BuildSyncStatus? Status => null;

    public JenkinsLiveBuildStore(IJenkinsClient jenkins)
    {
        _jenkins = jenkins ?? throw new ArgumentNullException(nameof(jenkins));
    }

    public Task<IReadOnlyList<Build>> ListBuildsAsync(string jobName, int count, CancellationToken cancellationToken = default)
        => _jenkins.ListBuildsAsync(jobName, count, cancellationToken);

    public Task<JenkinsBuildDetails> GetBuildAsync(string jobName, int number, CancellationToken cancellationToken = default)
        => _jenkins.GetBuildDetailsAsync(jobName, number, cancellationToken);

    public Task<byte[]> GetArtifactBytesAsync(string jobName, int number, string relativePath, CancellationToken cancellationToken = default)
        => _jenkins.GetArtifactAsync(jobName, number, relativePath, cancellationToken);

    public async Task<BuildInfo?> GetBuildInfoAsync(string jobName, int number, CancellationToken cancellationToken = default)
    {
        // Best-effort fetch. Older builds (pre-Jenkinsfile change) don't archive
        // build-info.json — Jenkins returns 404 for the missing artifact, which we
        // translate to null so the caller can render "—" gracefully.
        try
        {
            var bytes = await _jenkins.GetArtifactAsync(jobName, number, "build-info.json", cancellationToken);
            return JsonSerializer.Deserialize<BuildInfo>(bytes, JsonOpts);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
