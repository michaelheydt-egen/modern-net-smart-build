using Microsoft.EntityFrameworkCore;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Common;

namespace Deployment.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly DeploymentDbContext _db;
    private readonly IDomainEventDispatcher _events;

    public UnitOfWork(DeploymentDbContext db, IDomainEventDispatcher events)
    {
        _db = db;
        _events = events;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var pending = _db.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<AggregateRoot<Guid>>()
            .SelectMany(a => { var ev = a.DomainEvents.ToArray(); a.ClearDomainEvents(); return ev; })
            .ToArray();

        var written = await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var evt in pending)
            await _events.DispatchAsync(evt, cancellationToken).ConfigureAwait(false);
        return written;
    }
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
