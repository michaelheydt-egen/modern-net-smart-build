using Deployment.Domain.Common;

namespace Deployment.Domain.Services.Events;

public sealed record ServiceRegistered(Guid ServiceId, string Name, string ContainerName, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record ServiceUpdated(Guid ServiceId, string Name, string ContainerName, DateTimeOffset OccurredAtUtc) : IDomainEvent;
