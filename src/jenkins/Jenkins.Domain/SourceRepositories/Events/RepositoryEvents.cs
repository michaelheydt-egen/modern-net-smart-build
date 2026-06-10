using Jenkins.Domain.Common;

namespace Jenkins.Domain.SourceRepositories.Events;

public sealed record RepositoryRegistered(
    Guid RepositoryId,
    string Name,
    string GitUrl,
    RepositoryProvider Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record RepositoryDetailsUpdated(
    Guid RepositoryId,
    string Name,
    string GitUrl,
    RepositoryProvider Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record RepositoryDeactivated(
    Guid RepositoryId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record RepositoryReactivated(
    Guid RepositoryId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record DeployableComponentAdded(
    Guid RepositoryId,
    Guid DeployableComponentId,
    string ContainerName,
    Guid DeployableUnitId,
    bool AutoPublish,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record DeployableComponentRemapped(
    Guid RepositoryId,
    Guid DeployableComponentId,
    Guid DeployableUnitId,
    bool AutoPublish,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record DeployableComponentDeactivated(
    Guid RepositoryId,
    Guid DeployableComponentId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
