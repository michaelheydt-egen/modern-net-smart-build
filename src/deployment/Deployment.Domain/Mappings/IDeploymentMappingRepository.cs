using Deployment.Domain.Abstractions;

namespace Deployment.Domain.Mappings;

public interface IDeploymentMappingRepository : IRepository<DeploymentMapping, Guid>
{
    Task<DeploymentMapping?> FindAsync(Guid serviceId, Guid environmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeploymentMapping>> ListByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>Auto-deploy-enabled mappings for a service — the event-driven trigger set.</summary>
    Task<IReadOnlyList<DeploymentMapping>> ListAutoByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>Mappings targeting an environment (used to block environment deletion).</summary>
    Task<IReadOnlyList<DeploymentMapping>> ListByEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken = default);
}
