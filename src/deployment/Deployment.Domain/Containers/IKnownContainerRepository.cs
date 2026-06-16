using Deployment.Domain.Abstractions;

namespace Deployment.Domain.Containers;

public interface IKnownContainerRepository : IRepository<KnownContainer, Guid>
{
    Task<KnownContainer?> FindByNameAsync(string containerName, CancellationToken cancellationToken = default);
}
