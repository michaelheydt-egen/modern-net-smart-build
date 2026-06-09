using System.Text;
using System.Text.Json;
using Jenkins.Client;
using Microsoft.EntityFrameworkCore;

namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// Background worker that mirrors Jenkins build history into the local SQLite
/// store. On each tick: for every configured job, fetch a page of recent builds
/// from Jenkins, plus refresh any rows we have marked Building, and upsert.
/// </summary>
public sealed class BuildSyncService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly BuildSyncOptions _options;
    private readonly IJenkinsClient _jenkins;
    private readonly IDbContextFactory<BuildSyncDbContext> _dbFactory;
    private readonly SqliteBuildStore _store;
    private readonly ILogger<BuildSyncService> _logger;

    private readonly HashSet<string> _backfilledJobs = new(StringComparer.OrdinalIgnoreCase);

    public BuildSyncService(
        BuildSyncOptions options,
        IJenkinsClient jenkins,
        IDbContextFactory<BuildSyncDbContext> dbFactory,
        SqliteBuildStore store,
        ILogger<BuildSyncService> logger)
    {
        _options   = options;
        _jenkins   = jenkins;
        _dbFactory = dbFactory;
        _store     = store;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        _logger.LogInformation(
            "BuildSync starting — jobs=[{Jobs}], interval={IntervalSec}s, backfillCount={Backfill}",
            string.Join(",", _options.Jobs), _options.PollIntervalSeconds, _options.BackfillCount);

        // Tick immediately on startup so the UI is populated without waiting a full interval.
        var nextTick = TimeSpan.Zero;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(nextTick, stoppingToken);
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BuildSync tick failed; retrying on next interval");
                _store.UpdateStatus(new BuildSyncStatus(DateTimeOffset.UtcNow, BuildsTracked: -1, LastError: ex.Message));
            }
            nextTick = interval;
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var totalTracked = 0;
        foreach (var job in _options.Jobs)
        {
            // First tick for a job uses BackfillCount so the UI doesn't start empty.
            var firstTime = _backfilledJobs.Add(job);
            var fetchCount = firstTime ? _options.BackfillCount : _options.PerJobFetchCount;

            totalTracked += await SyncJobAsync(job, fetchCount, ct);
        }
        _store.UpdateStatus(new BuildSyncStatus(DateTimeOffset.UtcNow, totalTracked, LastError: null));
    }

    private async Task<int> SyncJobAsync(string jobName, int fetchCount, CancellationToken ct)
    {
        IReadOnlyList<Build> recent;
        try
        {
            recent = await _jenkins.ListBuildsAsync(jobName, fetchCount, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BuildSync ListBuilds for {Job} failed", jobName);
            return await CountTrackedAsync(jobName, ct);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // What we already have for this job at the numbers Jenkins is showing us.
        var fetchedNumbers = recent.Select(b => b.Number).ToHashSet();
        var existingByNumber = await db.BuildRuns
            .Where(r => r.JobName == jobName && fetchedNumbers.Contains(r.Number))
            .ToDictionaryAsync(r => r.Number, ct);

        // Plus any in-flight rows that might be outside the recent-N window
        // — they need a refresh until they reach a terminal state.
        var inFlightOutsideWindow = await db.BuildRuns
            .Where(r => r.JobName == jobName && r.Building && !fetchedNumbers.Contains(r.Number))
            .ToListAsync(ct);

        var upsertedCount = 0;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var build in recent)
        {
            existingByNumber.TryGetValue(build.Number, out var row);

            // Already cached, already terminal, already enriched with build-info.json
            // → nothing more to do.
            if (row is { Building: false } && !string.IsNullOrEmpty(row.BuildInfoJson))
            {
                continue;
            }

            // Need details (artifacts + causes) — and we'll try the build-info.json
            // artifact if it's archived. Failure of either is non-fatal.
            JenkinsBuildDetails? details = null;
            string? buildInfoJson = null;
            try
            {
                details = await _jenkins.GetBuildDetailsAsync(jobName, build.Number, ct);
                if (details.Artifacts.Any(a => a.RelativePath.EndsWith("build-info.json", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var bytes = await _jenkins.GetArtifactAsync(jobName, build.Number, "build-info.json", ct);
                        buildInfoJson = Encoding.UTF8.GetString(bytes);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "build-info.json fetch failed for {Job}#{N}", jobName, build.Number);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetBuildDetails for {Job}#{N} failed; storing list-level fields only", jobName, build.Number);
            }

            row ??= new BuildRunRecord { JobName = jobName, Number = build.Number };
            ApplyFromList(row, build);
            if (details is not null) ApplyFromDetails(row, details);
            if (buildInfoJson is not null) row.BuildInfoJson = buildInfoJson;
            row.SyncedAt = now;

            if (db.Entry(row).State == EntityState.Detached)
            {
                db.BuildRuns.Add(row);
            }
            upsertedCount++;
        }

        // Refresh in-flight builds outside the recent window — same flow, single fetch each.
        foreach (var row in inFlightOutsideWindow)
        {
            try
            {
                var details = await _jenkins.GetBuildDetailsAsync(jobName, row.Number, ct);
                ApplyFromDetails(row, details);
                row.SyncedAt = now;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Refresh of in-flight {Job}#{N} failed", jobName, row.Number);
            }
        }

        // Reconcile deletions inside the window we just inspected. We only touch
        // rows whose Number falls between the min and max of what Jenkins returned —
        // anything older than the window is outside our perception this tick and
        // shouldn't be touched (avoids accidentally pruning history older than
        // PerJobFetchCount that Jenkins itself has already aged out).
        var reconciledDeletes = 0;
        if (_options.ReconcileDeleted && fetchedNumbers.Count > 0)
        {
            var minFetched = fetchedNumbers.Min();
            var maxFetched = fetchedNumbers.Max();
            var stale = await db.BuildRuns
                .Where(r => r.JobName == jobName
                         && r.Number >= minFetched
                         && r.Number <= maxFetched
                         && !fetchedNumbers.Contains(r.Number))
                .ToListAsync(ct);
            if (stale.Count > 0)
            {
                db.BuildRuns.RemoveRange(stale);
                reconciledDeletes = stale.Count;
                _logger.LogInformation(
                    "BuildSync reconciled {Count} deleted build(s) for {Job} in window [{Min},{Max}]",
                    reconciledDeletes, jobName, minFetched, maxFetched);
            }
        }

        if (upsertedCount > 0 || inFlightOutsideWindow.Count > 0 || reconciledDeletes > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return await CountTrackedAsync(jobName, ct);
    }

    private async Task<int> CountTrackedAsync(string jobName, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.BuildRuns.CountAsync(r => r.JobName == jobName, ct);
    }

    // --- Field projection helpers ---

    private static void ApplyFromList(BuildRunRecord row, Build build)
    {
        row.Building    = build.Building;
        row.Result      = build.Result?.ToString();
        row.Timestamp   = build.Timestamp;
        row.Duration    = build.Duration;
        row.Description = build.Description;
    }

    private static void ApplyFromDetails(BuildRunRecord row, JenkinsBuildDetails details)
    {
        row.Building    = details.Building;
        row.Result      = details.Result?.ToString();
        row.Timestamp   = details.Timestamp;
        row.Duration    = details.Duration;
        row.Description = details.Description;
        row.CausesJson    = JsonSerializer.Serialize(details.Causes,    JsonOpts);
        row.ArtifactsJson = JsonSerializer.Serialize(details.Artifacts, JsonOpts);
    }
}
