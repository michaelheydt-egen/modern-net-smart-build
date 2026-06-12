using System.Net;
using System.Text.Json;
using Jenkins.Application.Features.Builds;
using Jenkins.Application.Features.Repositories;
using Jenkins.Client;
using Jenkins.Contracts.Builds;
using Jenkins.Contracts.Repositories;
using Jenkins.Infrastructure.Sync.Nexus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jenkins.Infrastructure.Sync;

/// <summary>
/// Background worker that mirrors Jenkins build history into the CI model. On each
/// tick, for every active tracked repository it pulls a page of recent builds of the
/// repo's <c>CiJobName</c> and drives the ingestion features:
/// <c>RecordBuild</c> (idempotent on the CI key) and, once a build is finished,
/// <c>CompleteBuild</c> (status + versions from <c>build-info.json</c> + the SBOM /
/// vulnerability artifact URLs).
///
/// Commit + version metadata comes from the <c>build-info.json</c> artifact that
/// every <c>cicd-build</c> run archives; builds without it are skipped (the model
/// requires a commit). Artifact/publication ingestion (Nexus/registry correlation)
/// is a separate concern and not done here.
/// </summary>
internal sealed class JenkinsBuildSyncService : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly IJenkinsClient _jenkins;
    private readonly JenkinsSyncOptions _options;
    private readonly ILogger<JenkinsBuildSyncService> _logger;

    private readonly HashSet<string> _backfilled = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _reconciled = new(); // builds whose Nexus artifacts are attached
    private readonly HashSet<Guid> _noArtifactsLogged = new(); // builds we've already warned about (avoid per-tick spam)
    private bool _nexusDisabledLogged;

    public JenkinsBuildSyncService(
        IServiceScopeFactory scopes,
        IJenkinsClient jenkins,
        IOptions<JenkinsSyncOptions> options,
        ILogger<JenkinsBuildSyncService> logger)
    {
        _scopes = scopes;
        _jenkins = jenkins;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        _logger.LogInformation("Jenkins build sync starting — interval={IntervalSec}s, backfill={Backfill}",
            _options.PollIntervalSeconds, _options.BackfillCount);

        var nextTick = TimeSpan.Zero; // first tick immediately
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(nextTick, stoppingToken).ConfigureAwait(false);
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Jenkins build sync tick failed; retrying next interval");
            }
            nextTick = interval;
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        IReadOnlyList<RepositoryDto> repositories;
        using (var scope = _scopes.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IRepositoryCatalogReader>();
            repositories = await reader.ListAsync(onlyActive: true, ct).ConfigureAwait(false);
        }

        foreach (var repo in repositories)
        {
            try
            {
                await SyncRepositoryAsync(repo, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Build sync for repository '{Repo}' (job '{Job}') failed", repo.Name, repo.CiJobName);
            }
        }
    }

    private async Task SyncRepositoryAsync(RepositoryDto repo, CancellationToken ct)
    {
        var firstTime = _backfilled.Add(repo.CiJobName);
        var fetchCount = firstTime ? _options.BackfillCount : _options.PerJobFetchCount;

        var recent = await _jenkins.ListBuildsAsync(repo.CiJobName, fetchCount, ct).ConfigureAwait(false);

        using var scope = _scopes.CreateScope();
        var recordBuild = scope.ServiceProvider.GetRequiredService<RecordBuildHandler>();
        var completeBuild = scope.ServiceProvider.GetRequiredService<CompleteBuildHandler>();
        var reconcile = scope.ServiceProvider.GetRequiredService<ReconcileBuildArtifactsHandler>();
        var nexus = scope.ServiceProvider.GetService<INexusArtifactReader>(); // null when Nexus unconfigured
        if (nexus is null && !_nexusDisabledLogged)
        {
            _nexusDisabledLogged = true;
            _logger.LogWarning(
                "[reconcile] Nexus artifact reconciliation is DISABLED — Nexus:Url and/or Nexus:Password are not configured " +
                "for the CI service. Pushed container images will NOT auto-populate the publisher inventory.");
        }

        foreach (var build in recent)
        {
            try
            {
                await IngestBuildAsync(repo, build, recordBuild, completeBuild, reconcile, nexus, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ingest of {Job}#{Number} failed", repo.CiJobName, build.Number);
            }
        }
    }

    private async Task IngestBuildAsync(
        RepositoryDto repo,
        Build build,
        RecordBuildHandler recordBuild,
        CompleteBuildHandler completeBuild,
        ReconcileBuildArtifactsHandler reconcile,
        INexusArtifactReader? nexus,
        CancellationToken ct)
    {
        var info = await TryGetBuildInfoAsync(repo.CiJobName, build.Number, ct).ConfigureAwait(false);
        if (info is null || string.IsNullOrWhiteSpace(info.GitCommitHash))
        {
            _logger.LogDebug("Skipping {Job}#{Number}: no build-info.json commit", repo.CiJobName, build.Number);
            return;
        }

        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(build.Timestamp);
        var commitShort = string.IsNullOrWhiteSpace(info.GitCommitShort)
            ? Short(info.GitCommitHash!)
            : info.GitCommitShort!;

        var summary = await recordBuild.HandleAsync(new RecordBuildCommand(
            Id: Guid.NewGuid(),
            RepositoryId: repo.Id,
            CiJobName: repo.CiJobName,
            CiBuildNumber: build.Number,
            CiRunUrl: build.Url,
            CiRunId: $"{repo.CiJobName}/#{build.Number}",
            CommitSha: info.GitCommitHash!,
            CommitShort: commitShort,
            Branch: repo.DefaultBranch,
            Author: null,
            Message: null,
            CommittedAtUtc: null,
            TriggeredBy: null,
            StartedAtUtc: startedAt), ct).ConfigureAwait(false);

        var succeeded = summary.Status == BuildStatusDto.Succeeded;

        // Finished in Jenkins but still Running in our store → settle it.
        if (!build.Building && summary.Status == BuildStatusDto.Running)
        {
            var status = MapResult(build.Result);
            if (status is { } terminal)
            {
                var versions = BuildVersionsFrom(info);
                var quality = await BuildQualityFromAsync(repo.CiJobName, build, ct).ConfigureAwait(false);
                var completedAt = DateTimeOffset.FromUnixTimeMilliseconds(build.Timestamp + Math.Max(0, build.Duration));

                await completeBuild.HandleAsync(new CompleteBuildCommand(
                    BuildId: summary.Id,
                    Status: terminal,
                    CompletedAtUtc: completedAt,
                    DurationMs: build.Duration > 0 ? build.Duration : null,
                    Versions: versions,
                    Quality: quality), ct).ConfigureAwait(false);

                succeeded = terminal == BuildStatusDto.Succeeded;
            }
        }

        // Option b: attach the build's published artifacts from Nexus. Retries each
        // tick (artifacts land after the downstream publish jobs) until present.
        if (succeeded && nexus is not null
            && !_reconciled.Contains(summary.Id)
            && !string.IsNullOrWhiteSpace(info.PackageVersion))
        {
            try
            {
                var specs = await nexus.FindArtifactsAsync(info.PackageVersion!, commitShort, build.Number, ct).ConfigureAwait(false);
                if (specs.Count > 0)
                {
                    var result = await reconcile.HandleAsync(
                        new ReconcileBuildArtifactsCommand(summary.Id, specs), ct).ConfigureAwait(false);
                    if (result.TotalArtifacts > 0)
                    {
                        _reconciled.Add(summary.Id);
                        _noArtifactsLogged.Remove(summary.Id);
                        _logger.LogInformation("[reconcile] {Job}#{Number}: +{Added} artifact(s) ({Total} total)",
                            repo.CiJobName, build.Number, result.Added, result.TotalArtifacts);
                    }
                }
                else if (_noArtifactsLogged.Add(summary.Id))
                {
                    // Visible (once per build) so a tag/repo mismatch isn't silent. The reader searches
                    // the docker repo for a component tagged with the commit-short (fallback ci-<build#>).
                    _logger.LogInformation(
                        "[reconcile] {Job}#{Number}: no Nexus artifacts found yet (version '{Version}', commit '{Commit}'). " +
                        "Will retry each tick. Verify the image was pushed to Nexus tagged '{Commit}' or 'ci-{Number}'.",
                        repo.CiJobName, build.Number, info.PackageVersion, commitShort, commitShort, build.Number);
                }
            }
            catch (Exception ex) when (_noArtifactsLogged.Add(summary.Id))
            {
                _logger.LogWarning(ex,
                    "[reconcile] {Job}#{Number}: Nexus query failed — is Nexus:Url reachable from this service and the password valid? Will retry.",
                    repo.CiJobName, build.Number);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Artifact reconciliation for {Job}#{Number} failed; will retry", repo.CiJobName, build.Number);
            }
        }
    }

    private async Task<JenkinsBuildInfo?> TryGetBuildInfoAsync(string job, int number, CancellationToken ct)
    {
        try
        {
            var bytes = await _jenkins.GetArtifactAsync(job, number, "build-info.json", ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<JenkinsBuildInfo>(bytes, Json);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // older builds don't archive it
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "build-info.json fetch failed for {Job}#{Number}", job, number);
            return null;
        }
    }

    private async Task<BuildQualityInput?> BuildQualityFromAsync(string job, Build build, CancellationToken ct)
    {
        try
        {
            var details = await _jenkins.GetBuildDetailsAsync(job, build.Number, ct).ConfigureAwait(false);
            var sbom = details.Artifacts.FirstOrDefault(a => Ends(a.RelativePath, "bom-vex.json"))
                       ?? details.Artifacts.FirstOrDefault(a => Ends(a.RelativePath, "bom.json"));
            var vuln = details.Artifacts.FirstOrDefault(a => Ends(a.RelativePath, "vulnerabilities.json"));
            if (sbom is null || vuln is null)
                return null;

            var baseUrl = build.Url.TrimEnd('/');
            return new BuildQualityInput(
                SbomUri: $"{baseUrl}/artifact/{sbom.RelativePath}",
                VulnerabilityReportUri: $"{baseUrl}/artifact/{vuln.RelativePath}");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Quality artifact lookup failed for {Job}#{Number}", job, build.Number);
            return null;
        }
    }

    private static BuildVersionsInput? BuildVersionsFrom(JenkinsBuildInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.PackageVersion)
            || string.IsNullOrWhiteSpace(info.FileVersion)
            || string.IsNullOrWhiteSpace(info.AssemblyVersion)
            || string.IsNullOrWhiteSpace(info.InformationalVersion)
            || string.IsNullOrWhiteSpace(info.PackVer))
            return null;

        return new BuildVersionsInput(
            PackageVersion: info.PackageVersion!,
            FileVersion: info.FileVersion!,
            AssemblyVersion: info.AssemblyVersion!,
            InformationalVersion: info.InformationalVersion!,
            BaseVersion: info.PackVer!);
    }

    private static BuildStatusDto? MapResult(BuildResult? result) => result switch
    {
        BuildResult.Success => BuildStatusDto.Succeeded,
        BuildResult.Failure => BuildStatusDto.Failed,
        BuildResult.Unstable => BuildStatusDto.Failed,
        BuildResult.Aborted => BuildStatusDto.Aborted,
        _ => null, // NotBuilt / null — not yet a terminal we record
    };

    private static bool Ends(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static string Short(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    /// <summary>
    /// Local mirror of the <c>build-info.json</c> schema (kept in sync with
    /// <c>jenkins/build/Jenkinsfile</c>). All nullable so partial/older builds
    /// deserialize cleanly.
    /// </summary>
    private sealed record JenkinsBuildInfo(
        string? PackageVersion,
        string? FileVersion,
        string? AssemblyVersion,
        string? InformationalVersion,
        string? GitCommitHash,
        string? GitCommitShort,
        string? BuildNumber,
        string? PackVer,
        string? BuildFile,
        string? BuildTimestamp);
}
