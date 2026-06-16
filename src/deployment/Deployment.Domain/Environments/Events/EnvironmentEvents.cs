using Deployment.Domain.Common;

namespace Deployment.Domain.Environments.Events;

public sealed record EnvironmentRegistered(Guid EnvironmentId, string Name, string GcpProject, string Region, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record EnvironmentUpdated(Guid EnvironmentId, string Name, string GcpProject, string Region, DateTimeOffset OccurredAtUtc) : IDomainEvent;
