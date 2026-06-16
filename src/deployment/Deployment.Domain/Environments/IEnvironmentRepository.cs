using Deployment.Domain.Abstractions;

namespace Deployment.Domain.Environments;

public interface IEnvironmentRepository : IRepository<DeploymentEnvironment, Guid>
{
    Task<DeploymentEnvironment?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
