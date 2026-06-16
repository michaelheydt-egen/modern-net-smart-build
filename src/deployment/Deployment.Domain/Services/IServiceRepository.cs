using Deployment.Domain.Abstractions;

namespace Deployment.Domain.Services;

public interface IServiceRepository : IRepository<Service, Guid>
{
    Task<Service?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Active services whose container name matches — the auto-deploy trigger lookup.</summary>
    Task<IReadOnlyList<Service>> ListActiveByContainerNameAsync(string containerName, CancellationToken cancellationToken = default);
}
