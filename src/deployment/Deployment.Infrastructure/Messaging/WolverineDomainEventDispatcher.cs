using Deployment.Domain.Common;
using Deployment.Infrastructure.Persistence;
using Wolverine;

namespace Deployment.Infrastructure.Messaging;

public sealed class WolverineDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMessageBus _bus;
    public WolverineDomainEventDispatcher(IMessageBus bus) => _bus = bus;
    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _bus.PublishAsync(domainEvent).AsTask();
}
