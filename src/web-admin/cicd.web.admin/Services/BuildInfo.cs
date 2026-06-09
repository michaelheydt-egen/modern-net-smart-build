namespace Cicd.Web.Admin.Services;

/// <summary>
/// Schema of the <c>build-info.json</c> artifact written by every <c>cicd-build</c> run.
/// Mirrors the JSON object produced by <c>writeJSON</c> in the Jenkinsfile —
/// keep the field set in sync with <c>jenkins/build/Jenkinsfile</c>'s Archive stage.
/// All fields nullable so older builds with a partial schema deserialize cleanly.
/// </summary>
public sealed record BuildInfo(
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
