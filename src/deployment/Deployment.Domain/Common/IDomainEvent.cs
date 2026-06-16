namespace Deployment.Domain.Common;

/// <summary>Marker for things-that-happened in the domain; dispatched after SaveChanges.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}
