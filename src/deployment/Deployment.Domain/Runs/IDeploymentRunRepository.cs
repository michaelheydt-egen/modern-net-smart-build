using Deployment.Domain.Abstractions;

namespace Deployment.Domain.Runs;

public interface IDeploymentRunRepository : IRepository<DeploymentRun, Guid>
{
}
