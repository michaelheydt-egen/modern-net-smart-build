namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// Controls the optional Jenkins → SQLite build-history sync. When
/// <see cref="Enabled"/> is <c>false</c> the WebUI runs in live-only mode
/// (default) and none of this config is consulted.
/// </summary>
public sealed record BuildSyncOptions
{
    /// <summary>Master switch. Off by default — opt in per environment.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Jenkins job names to mirror. First cut typically just "cicd-build".</summary>
    public IReadOnlyList<string> Jobs { get; init; } = new[] { "cicd-build" };

    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// On first run (empty DB) the sync grabs the last N builds per job so the UI
    /// has history immediately rather than only what arrives going forward.
    /// </summary>
    public int BackfillCount { get; init; } = 100;

    /// <summary>How many builds to fetch from Jenkins per tick (steady state).</summary>
    public int PerJobFetchCount { get; init; } = 25;

    /// <summary>
    /// Path to the SQLite file. Relative paths are resolved against the
    /// WebUI's working directory. In container deployments this should point
    /// at a bind-mounted host directory.
    /// </summary>
    public string DbPath { get; init; } = "./data/builds.db";

    /// <summary>
    /// When enabled, the sync tick deletes local rows for builds Jenkins no
    /// longer has within the recent-N fetch window. Common case: an admin
    /// deleted a build in Jenkins and you don't want it lingering in the UI.
    /// Off by default — destructive, and most teams want the WebUI's history
    /// to outlive Jenkins's retention rather than mirror its pruning.
    /// </summary>
    public bool ReconcileDeleted { get; init; } = false;
}
