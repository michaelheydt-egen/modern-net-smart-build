using Deployment.Domain.Common;

namespace Deployment.Domain.AspireApps.Events;

public sealed record AspireApplicationRegistered(Guid ApplicationId, string Name, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record AspireApplicationUpdated(Guid ApplicationId, string Name, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record AspireApplicationAutoDeployChanged(Guid ApplicationId, bool AutoDeploy, DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>A CI <c>AspireAppPublished</c> refreshed this app's manifest source/version (name-matched).</summary>
public sealed record AspireApplicationManifestPublished(Guid ApplicationId, string Name, string ManifestSource, string? Version, DateTimeOffset OccurredAtUtc) : IDomainEvent;
