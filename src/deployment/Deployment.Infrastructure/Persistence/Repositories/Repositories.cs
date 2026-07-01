using Microsoft.EntityFrameworkCore;
using Deployment.Domain.Abstractions;
using Deployment.Domain.AspireApps;
using Deployment.Domain.AspireApps.Runs;
using Deployment.Domain.Common;
using Deployment.Domain.Containers;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Services;

namespace Deployment.Infrastructure.Persistence.Repositories;

internal abstract class EfRepository<TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    protected readonly DeploymentDbContext Db;
    protected EfRepository(DeploymentDbContext db) => Db = db;
    protected DbSet<TAggregate> Set => Db.Set<TAggregate>();

    public virtual Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(a => a.Id.Equals(id), ct);
    public async Task AddAsync(TAggregate aggregate, CancellationToken ct = default)
        => await Set.AddAsync(aggregate, ct).ConfigureAwait(false);
    public void Remove(TAggregate aggregate) => Set.Remove(aggregate);
}

internal sealed class ServiceRepository : EfRepository<Service, Guid>, IServiceRepository
{
    public ServiceRepository(DeploymentDbContext db) : base(db) { }
    public Task<Service?> FindByNameAsync(string name, CancellationToken ct = default)
    { var n = name.Trim(); return Set.FirstOrDefaultAsync(s => s.Name == n, ct); }
    public async Task<IReadOnlyList<Service>> ListActiveByContainerNameAsync(string containerName, CancellationToken ct = default)
    { var n = containerName.Trim(); return await Set.Where(s => s.IsActive && s.ContainerName == n).ToListAsync(ct).ConfigureAwait(false); }
}

internal sealed class EnvironmentRepository : EfRepository<DeploymentEnvironment, Guid>, IEnvironmentRepository
{
    public EnvironmentRepository(DeploymentDbContext db) : base(db) { }
    public Task<DeploymentEnvironment?> FindByNameAsync(string name, CancellationToken ct = default)
    { var n = name.Trim(); return Set.FirstOrDefaultAsync(e => e.Name == n, ct); }
}

internal sealed class DeploymentMappingRepository : EfRepository<DeploymentMapping, Guid>, IDeploymentMappingRepository
{
    public DeploymentMappingRepository(DeploymentDbContext db) : base(db) { }
    public Task<DeploymentMapping?> FindAsync(Guid serviceId, Guid environmentId, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(m => m.ServiceId == serviceId && m.EnvironmentId == environmentId, ct);
    public async Task<IReadOnlyList<DeploymentMapping>> ListByServiceAsync(Guid serviceId, CancellationToken ct = default)
        => await Set.Where(m => m.ServiceId == serviceId).ToListAsync(ct).ConfigureAwait(false);
    public async Task<IReadOnlyList<DeploymentMapping>> ListAutoByServiceAsync(Guid serviceId, CancellationToken ct = default)
        => await Set.Where(m => m.ServiceId == serviceId && m.AutoDeploy).ToListAsync(ct).ConfigureAwait(false);
    public async Task<IReadOnlyList<DeploymentMapping>> ListByEnvironmentAsync(Guid environmentId, CancellationToken ct = default)
        => await Set.Where(m => m.EnvironmentId == environmentId).ToListAsync(ct).ConfigureAwait(false);
}

internal sealed class KnownContainerRepository : EfRepository<KnownContainer, Guid>, IKnownContainerRepository
{
    public KnownContainerRepository(DeploymentDbContext db) : base(db) { }
    public Task<KnownContainer?> FindByNameAsync(string containerName, CancellationToken ct = default)
    { var n = containerName.Trim(); return Set.FirstOrDefaultAsync(c => c.ContainerName == n, ct); }
}

internal sealed class DeploymentRunRepository : EfRepository<DeploymentRun, Guid>, IDeploymentRunRepository
{
    public DeploymentRunRepository(DeploymentDbContext db) : base(db) { }
}

internal sealed class AspireApplicationRepository : EfRepository<AspireApplication, Guid>, IAspireApplicationRepository
{
    public AspireApplicationRepository(DeploymentDbContext db) : base(db) { }
    public Task<AspireApplication?> FindByNameAsync(string name, CancellationToken ct = default)
    { var n = name.Trim(); return Set.FirstOrDefaultAsync(a => a.Name == n, ct); }

    // CI handoff resolution: prefer an explicit SourceKey match; fall back to name-matching for
    // apps that haven't set one (backward compatible).
    public async Task<AspireApplication?> FindBySourceKeyAsync(string appName, CancellationToken ct = default)
    {
        var key = appName.Trim();
        return await Set.FirstOrDefaultAsync(a => a.SourceKey == key, ct).ConfigureAwait(false)
            ?? await Set.FirstOrDefaultAsync(a => a.SourceKey == null && a.Name == key, ct).ConfigureAwait(false);
    }
}

internal sealed class AspireApplicationRunRepository : EfRepository<AspireApplicationRun, Guid>, IAspireApplicationRunRepository
{
    public AspireApplicationRunRepository(DeploymentDbContext db) : base(db) { }
}
