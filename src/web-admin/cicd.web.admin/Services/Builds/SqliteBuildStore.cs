using System.Text.Json;
using Jenkins.Client;
using Microsoft.EntityFrameworkCore;

namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// <see cref="IBuildStore"/> backed by the local SQLite mirror.
/// Falls back to the supplied live store on cache miss so a freshly-built row
/// that the sync hasn't picked up yet is still viewable.
/// </summary>
public sealed class SqliteBuildStore : IBuildStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDbContextFactory<BuildSyncDbContext> _dbFactory;
    private readonly JenkinsLiveBuildStore _fallback;
    private BuildSyncStatus? _status;

    public BuildSyncStatus? Status => _status;

    public SqliteBuildStore(
        IDbContextFactory<BuildSyncDbContext> dbFactory,
        JenkinsLiveBuildStore fallback)
    {
        _dbFactory = dbFactory;
        _fallback  = fallback;
    }

    /// <summary>Called by <see cref="BuildSyncService"/> after each tick.</summary>
    internal void UpdateStatus(BuildSyncStatus status) => _status = status;

    public async Task<IReadOnlyList<Build>> ListBuildsAsync(string jobName, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.BuildRuns
            .AsNoTracking()
            .Where(r => r.JobName == jobName)
            .OrderByDescending(r => r.Number)
            .Take(count)
            .ToListAsync(cancellationToken);

        // If the sync hasn't populated yet, fall through to live so the page isn't empty.
        if (rows.Count == 0)
        {
            return await _fallback.ListBuildsAsync(jobName, count, cancellationToken);
        }

        return rows.Select(ToBuild).ToArray();
    }

    public async Task<JenkinsBuildDetails> GetBuildAsync(string jobName, int number, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var row = await db.BuildRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.JobName == jobName && r.Number == number, cancellationToken);

        if (row is null)
        {
            // Brand-new build the sync hasn't seen yet — defer to live so the UI still works.
            return await _fallback.GetBuildAsync(jobName, number, cancellationToken);
        }

        return ToDetails(row);
    }

    public Task<byte[]> GetArtifactBytesAsync(string jobName, int number, string relativePath, CancellationToken cancellationToken = default)
        // Artifact bytes are immutable and potentially large — we don't mirror them
        // to SQLite. Always go straight to Jenkins via the fallback live store.
        => _fallback.GetArtifactBytesAsync(jobName, number, relativePath, cancellationToken);

    public async Task<BuildInfo?> GetBuildInfoAsync(string jobName, int number, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var row = await db.BuildRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.JobName == jobName && r.Number == number, cancellationToken);

        if (row is null)
        {
            return await _fallback.GetBuildInfoAsync(jobName, number, cancellationToken);
        }

        if (string.IsNullOrEmpty(row.BuildInfoJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<BuildInfo>(row.BuildInfoJson, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- Row → DTO projection (shared with the sync service) ---

    internal static Build ToBuild(BuildRunRecord r) => new(
        Number:      r.Number,
        Url:         string.Empty,   // we don't store URL; the page rewrites onto JenkinsOpts.BaseUrl
        Building:    r.Building,
        Result:      ParseResult(r.Result),
        Duration:    r.Duration,
        Timestamp:   r.Timestamp,
        Description: r.Description);

    internal static JenkinsBuildDetails ToDetails(BuildRunRecord r)
    {
        var causes = string.IsNullOrEmpty(r.CausesJson)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(r.CausesJson, JsonOpts) ?? Array.Empty<string>();

        var artifacts = string.IsNullOrEmpty(r.ArtifactsJson)
            ? Array.Empty<JenkinsBuildArtifact>()
            : JsonSerializer.Deserialize<JenkinsBuildArtifact[]>(r.ArtifactsJson, JsonOpts) ?? Array.Empty<JenkinsBuildArtifact>();

        return new JenkinsBuildDetails(
            Number:      r.Number,
            Url:         string.Empty,
            Building:    r.Building,
            Result:      ParseResult(r.Result),
            Timestamp:   r.Timestamp,
            Duration:    r.Duration,
            Description: r.Description,
            Artifacts:   artifacts,
            Causes:      causes);
    }

    internal static BuildResult? ParseResult(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return Enum.TryParse<BuildResult>(raw, ignoreCase: true, out var v) ? v : null;
    }
}
