namespace Cicd.IntegrationEvents.Ci;

/// <summary>
/// An Aspire application's Kustomize-manifest archive produced by a CI build was published to
/// Nexus. Emitted by the Jenkins CI service for repositories typed <c>BuildKind.Aspire</c>; the
/// deployment service reacts by updating (and optionally auto-deploying) the matching
/// <c>AspireApplication</c> (matched by <see cref="AppName"/>).
/// </summary>
public sealed record AspireAppPublished(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid BuildId,
    Guid RepositoryId,
    string AppName,
    string ManifestUrl,
    string Version,
    string CommitSha) : IIntegrationEvent;
