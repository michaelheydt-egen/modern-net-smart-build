using Deployment.Domain.Common;

namespace Deployment.Domain.Mappings.Events;

public sealed record DeploymentMappingCreated(Guid MappingId, Guid ServiceId, Guid EnvironmentId, bool AutoDeploy, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record DeploymentMappingUpdated(Guid MappingId, Guid ServiceId, Guid EnvironmentId, bool AutoDeploy, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record DeploymentMappingAutoDeployChanged(Guid MappingId, bool AutoDeploy, DateTimeOffset OccurredAtUtc) : IDomainEvent;
