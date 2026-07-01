using Jenkins.Domain.Common;

namespace Jenkins.Domain.Builds.Events;

public sealed record BuildStarted(
    Guid BuildId,
    Guid RepositoryId,
    string CiJobName,
    int CiBuildNumber,
    string CommitSha,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record BuildVersionsRecorded(
    Guid BuildId,
    string PackageVersion,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record BuildQualityAttached(
    Guid BuildId,
    string SbomUri,
    string VulnerabilityReportUri,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record BuildSucceeded(
    Guid BuildId,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record BuildFailed(
    Guid BuildId,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record BuildAborted(
    Guid BuildId,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ArtifactRecorded(
    Guid BuildId,
    Guid BuildArtifactId,
    ArtifactKind Kind,
    string Name,
    string Version,
    string Digest,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>
/// Raised when a container artifact's successful Nexus push is recorded. The
/// application layer's auto-publish handler listens for this to fire a handoff
/// when the matching <c>DeployableComponent.AutoPublish</c> is set (CI decision #3).
/// </summary>
public sealed record ContainerPublished(
    Guid BuildId,
    Guid BuildArtifactId,
    Guid PublicationId,
    string ContainerName,
    string Reference,
    Guid RepositoryId,
    string Version,
    string CommitSha,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>
/// Raised when an Aspire build's Kustomize-manifest archive is published to Nexus (its URL
/// captured from <c>build-info.json</c>). Translated to the cross-service
/// <c>Cicd.IntegrationEvents.Ci.AspireAppPublished</c> so the deployment service can update
/// (and optionally auto-deploy) the matching <c>AspireApplication</c>.
/// </summary>
public sealed record AspireManifestPublished(
    Guid BuildId,
    Guid RepositoryId,
    string AppName,
    string ManifestUrl,
    string Version,
    string CommitSha,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
