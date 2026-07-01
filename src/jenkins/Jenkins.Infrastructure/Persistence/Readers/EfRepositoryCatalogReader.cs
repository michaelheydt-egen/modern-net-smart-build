using Jenkins.Application.Features.Repositories;
using Jenkins.Contracts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Jenkins.Infrastructure.Persistence.Readers;

/// <summary>
/// Read-model reader for the repository catalog: flat projections to
/// <see cref="RepositoryDto"/> (with nested components) without tracking.
/// </summary>
public sealed class EfRepositoryCatalogReader : IRepositoryCatalogReader
{
    private readonly JenkinsCiDbContext _db;

    public EfRepositoryCatalogReader(JenkinsCiDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RepositoryDto>> ListAsync(
        bool? onlyActive,
        CancellationToken cancellationToken = default)
    {
        var query =
            from r in _db.Repositories.AsNoTracking()
            where !onlyActive.HasValue || r.IsActive == onlyActive.Value
            orderby r.Name
            select Project(r);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RepositoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Repositories.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => Project(r))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Shared projection — EF translates the nested component select.
    private static RepositoryDto Project(Domain.SourceRepositories.SourceRepository r) => new(
        r.Id,
        r.Name,
        r.GitUrl,
        (RepositoryProviderDto)(int)r.Provider,
        r.DefaultBranch,
        r.CiJobName,
        r.BaseVersion,
        r.IsActive,
        r.AllowContainerPublish,
        (BuildKindDto)(int)r.BuildKind,
        r.AppHostPath,
        r.CreatedAtUtc,
        r.Components
            .OrderBy(c => c.ContainerName)
            .Select(c => new DeployableComponentDto(
                c.Id, c.ContainerName, c.DeployableUnitId, c.DeployableUnitName, c.AutoPublish, c.IsActive))
            .ToList());
}
