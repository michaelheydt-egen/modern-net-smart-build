using Microsoft.EntityFrameworkCore;
using Deployment.Application.Features.AspireApps;
using Deployment.Application.Features.Containers;
using Deployment.Application.Features.Environments;
using Deployment.Application.Features.Mappings;
using Deployment.Application.Features.Previews;
using Deployment.Application.Features.Runs;
using Deployment.Application.Features.Services;
using Deployment.Contracts.AspireApps;
using Deployment.Contracts.Catalog;
using Deployment.Contracts.Mappings;
using Deployment.Contracts.Previews;
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
            .Select(e => new EnvironmentDto(e.Id, e.Name, e.GcpProject, e.Region, e.GarRepository, e.KubernetesContext, e.KubernetesNamespace, e.IsActive, e.CreatedAtUtc, e.UpdatedAtUtc, e.IsProtected))
            .ToListAsync(ct).ConfigureAwait(false);
    public async Task<EnvironmentDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Environments.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new EnvironmentDto(e.Id, e.Name, e.GcpProject, e.Region, e.GarRepository, e.KubernetesContext, e.KubernetesNamespace, e.IsActive, e.CreatedAtUtc, e.UpdatedAtUtc, e.IsProtected))
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
                m.CloudRunServiceName,
                m.Kubernetes == null ? null : new KubernetesSpecDto(m.Kubernetes.DeploymentName, m.Kubernetes.ContainerPort, m.Kubernetes.Replicas, m.Kubernetes.EnvVars, m.Kubernetes.ImagePullSecret, m.Kubernetes.CreateService, (RolloutStrategyDto)(int)m.Kubernetes.Strategy, (PromotionModeDto)(int)m.Kubernetes.PromotionMode, m.Kubernetes.CanaryWeightPercent, m.Kubernetes.CanarySteps),
                m.AutoDeploy,
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
        r.RemoteImageRef, r.CloudRunRevision, r.KubernetesResource, r.FailureReason,
        r.Steps.Select(s => new RunStepResultDto(s.Order, s.Kind.ToString(), s.Status, s.Detail, s.FailureKind?.ToString())).ToList(),
        r.RequestedAtUtc, r.CompletedAtUtc, r.RolloutGreenSlot, r.RolloutActiveSlot, r.DecisionBy, r.RolloutCanaryWeight);
}

internal sealed class EfAspireApplicationReader : IAspireApplicationReader
{
    private readonly DeploymentDbContext _db;
    public EfAspireApplicationReader(DeploymentDbContext db) => _db = db;

    // Project from an already-filtered/ordered entity query. Ordering and filtering must be applied
    // to the entity (a) — never the projected DTO — or SQL Server can't translate `new Dto(...).X`.
    private IQueryable<AspireApplicationDto> Project(IQueryable<Domain.AspireApps.AspireApplication> apps) =>
        from a in apps
        join e in _db.Environments.AsNoTracking() on a.EnvironmentId equals e.Id into ej
        from e in ej.DefaultIfEmpty()
        select new AspireApplicationDto(
            a.Id, a.Name, a.Description, a.EnvironmentId, e != null ? e.Name : "", a.ManifestSource, a.Version,
            a.SourceKey, a.IsActive, a.AutoDeploy, a.CreatedAtUtc, a.UpdatedAtUtc, a.MainBranch,
            (Deployment.Contracts.Mappings.RolloutStrategyDto)(int)a.Strategy, (Deployment.Contracts.Mappings.PromotionModeDto)(int)a.PromotionMode, a.ActiveSlot);

    public async Task<IReadOnlyList<AspireApplicationDto>> ListAsync(CancellationToken ct = default)
        => await Project(_db.AspireApplications.AsNoTracking().OrderBy(a => a.Name))
            .ToListAsync(ct).ConfigureAwait(false);
    public async Task<AspireApplicationDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Project(_db.AspireApplications.AsNoTracking().Where(a => a.Id == id))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
}

internal sealed class EfAspireApplicationRunReader : IAspireApplicationRunReader
{
    private readonly DeploymentDbContext _db;
    public EfAspireApplicationRunReader(DeploymentDbContext db) => _db = db;

    private static AspireApplicationRunDto ToDto(Domain.AspireApps.Runs.AspireApplicationRun r) => new(
        r.Id, r.ApplicationId, r.ApplicationName, r.EnvironmentName, r.KubeContext, r.Namespace, r.ManifestSource, r.Version,
        (AspireRunStatusDto)(int)r.Status, r.TriggeredBy, r.Log, r.FailureReason, r.RequestedAtUtc, r.CompletedAtUtc, r.DecisionBy,
        r.DeployedImages.Select(i => new DeployedImageDto(i.Workload, i.Image)).ToList(),
        r.RolloutGreenSlot, r.RolloutActiveSlot);

    public async Task<IReadOnlyList<AspireApplicationRunDto>> ListAsync(Guid? applicationId = null, CancellationToken ct = default)
    {
        var runs = await _db.AspireApplicationRuns.AsNoTracking()
            .Where(r => !applicationId.HasValue || r.ApplicationId == applicationId.Value)
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        return runs.Select(ToDto).ToList();
    }
    public async Task<AspireApplicationRunDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.AspireApplicationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return r is null ? null : ToDto(r);
    }
}

internal sealed class EfPreviewEnvironmentReader : IPreviewEnvironmentReader
{
    private readonly DeploymentDbContext _db;
    public EfPreviewEnvironmentReader(DeploymentDbContext db) => _db = db;

    private static PreviewEnvironmentDto ToDto(Domain.Previews.PreviewEnvironment p) => new(
        p.Id, p.ApplicationId, p.ApplicationName, p.Key, p.KubeContext, p.Namespace, p.ManifestSource, p.Version,
        (PreviewStatusDto)(int)p.Status, p.TriggeredBy, p.Log, p.FailureReason,
        p.CreatedAtUtc, p.ExpiresAtUtc, p.ActivatedAtUtc, p.TornDownAtUtc, p.Url);

    public async Task<IReadOnlyList<PreviewEnvironmentDto>> ListAsync(Guid? applicationId = null, bool includeTornDown = false, CancellationToken ct = default)
    {
        var previews = await _db.PreviewEnvironments.AsNoTracking()
            .Where(p => (!applicationId.HasValue || p.ApplicationId == applicationId.Value)
                && (includeTornDown || p.Status != Domain.Previews.PreviewStatus.TornDown))
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        return previews.Select(ToDto).ToList();
    }

    public async Task<PreviewEnvironmentDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.PreviewEnvironments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return p is null ? null : ToDto(p);
    }
}
