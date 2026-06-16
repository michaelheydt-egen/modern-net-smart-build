using Microsoft.EntityFrameworkCore;
using Deployment.Application.Features.Containers;
using Deployment.Application.Features.Environments;
using Deployment.Application.Features.Mappings;
using Deployment.Application.Features.Runs;
using Deployment.Application.Features.Services;
using Deployment.Contracts.Catalog;
using Deployment.Contracts.Mappings;
using Deployment.Contracts.Runs;

namespace Deployment.Infrastructure.Persistence.Readers;

internal sealed class EfServiceReader : IServiceReader
{
    private readonly DeploymentDbContext _db;
    public EfServiceReader(DeploymentDbContext db) => _db = db;
    public async Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct = default)
        => await _db.Services.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.ContainerName, s.IsActive, s.CreatedAtUtc, s.UpdatedAtUtc))
            .ToListAsync(ct).ConfigureAwait(false);
    public async Task<ServiceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Services.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new ServiceDto(s.Id, s.Name, s.ContainerName, s.IsActive, s.CreatedAtUtc, s.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
}

internal sealed class EfEnvironmentReader : IEnvironmentReader
{
    private readonly DeploymentDbContext _db;
    public EfEnvironmentReader(DeploymentDbContext db) => _db = db;
    public async Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct = default)
        => await _db.Environments.AsNoTracking().OrderBy(e => e.Name)
            .Select(e => new EnvironmentDto(e.Id, e.Name, e.GcpProject, e.Region, e.GarRepository, e.IsActive, e.CreatedAtUtc, e.UpdatedAtUtc))
            .ToListAsync(ct).ConfigureAwait(false);
    public async Task<EnvironmentDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Environments.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new EnvironmentDto(e.Id, e.Name, e.GcpProject, e.Region, e.GarRepository, e.IsActive, e.CreatedAtUtc, e.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
}

internal sealed class EfKnownContainerReader : IKnownContainerReader
{
    private readonly DeploymentDbContext _db;
    public EfKnownContainerReader(DeploymentDbContext db) => _db = db;
    public async Task<IReadOnlyList<KnownContainerDto>> ListAsync(CancellationToken ct = default)
        => await _db.KnownContainers.AsNoTracking().OrderBy(c => c.ContainerName)
            .Select(c => new KnownContainerDto(c.Id, c.ContainerName, c.Version, c.ImageDigest, c.NexusRef, c.FirstSeenAtUtc, c.LastSeenAtUtc))
            .ToListAsync(ct).ConfigureAwait(false);
}

internal sealed class EfMappingReader : IMappingReader
{
    private readonly DeploymentDbContext _db;
    public EfMappingReader(DeploymentDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeploymentMappingDto>> ListAsync(Guid? serviceId, CancellationToken ct = default)
    {
        var mappings = await _db.Mappings.AsNoTracking()
            .Where(m => !serviceId.HasValue || m.ServiceId == serviceId.Value)
            .ToListAsync(ct).ConfigureAwait(false);
        return await MapAsync(mappings, ct).ConfigureAwait(false);
    }

    public async Task<DeploymentMappingDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var m = await _db.Mappings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (m is null) return null;
        return (await MapAsync(new[] { m }, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<List<DeploymentMappingDto>> MapAsync(IReadOnlyList<Domain.Mappings.DeploymentMapping> mappings, CancellationToken ct)
    {
        if (mappings.Count == 0) return new List<DeploymentMappingDto>();
        var svcIds = mappings.Select(m => m.ServiceId).Distinct().ToList();
        var envIds = mappings.Select(m => m.EnvironmentId).Distinct().ToList();
        var svcNames = await _db.Services.AsNoTracking().Where(s => svcIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct).ConfigureAwait(false);
        var envNames = await _db.Environments.AsNoTracking().Where(e => envIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, ct).ConfigureAwait(false);

        return mappings
            .OrderBy(m => svcNames.GetValueOrDefault(m.ServiceId, ""))
            .Select(m => new DeploymentMappingDto(
                m.Id, m.ServiceId, svcNames.GetValueOrDefault(m.ServiceId, ""),
                m.EnvironmentId, envNames.GetValueOrDefault(m.EnvironmentId, ""),
                m.CloudRunServiceName, m.AutoDeploy,
                m.Steps.Select(s => new DeploymentStepDto(s.Order, (DeploymentStepKindDto)(int)s.Kind)).ToList(),
                m.CreatedAtUtc, m.UpdatedAtUtc))
            .ToList();
    }
}

internal sealed class EfRunReader : IRunReader
{
    private readonly DeploymentDbContext _db;
    public EfRunReader(DeploymentDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeploymentRunDto>> ListAsync(Guid? serviceId, Guid? mappingId, CancellationToken ct = default)
    {
        var runs = await _db.Runs.AsNoTracking()
            .Where(r => (!serviceId.HasValue || r.ServiceId == serviceId.Value)
                        && (!mappingId.HasValue || r.MappingId == mappingId.Value))
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        return runs.Select(ToDto).ToList();
    }

    public async Task<DeploymentRunDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return r is null ? null : ToDto(r);
    }

    private static DeploymentRunDto ToDto(Domain.Runs.DeploymentRun r) => new(
        r.Id, r.MappingId, r.ServiceId, r.ServiceName, r.EnvironmentId, r.ContainerName, r.Version, r.SourceRef,
        r.GcpProject, r.Region, r.CloudRunServiceName,
        (DeploymentTriggerDto)(int)r.Trigger, r.TriggeredBy, (DeploymentRunStatusDto)(int)r.Status,
        r.RemoteImageRef, r.CloudRunRevision, r.FailureReason,
        r.Steps.Select(s => new RunStepResultDto(s.Order, s.Kind.ToString(), s.Status, s.Detail)).ToList(),
        r.RequestedAtUtc, r.CompletedAtUtc);
}
