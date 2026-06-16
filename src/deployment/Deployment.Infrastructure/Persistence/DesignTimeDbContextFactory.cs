using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Deployment.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DeploymentDbContext>
{
    public DeploymentDbContext CreateDbContext(string[] args)
    {
        var connection = System.Environment.GetEnvironmentVariable("DEPLOYMENT_CONNECTIONSTRING")
            ?? "Server=localhost;Database=Deployment;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";
        var opts = new DbContextOptionsBuilder<DeploymentDbContext>()
            .UseSqlServer(connection, b => b.MigrationsAssembly(typeof(DeploymentDbContext).Assembly.GetName().Name))
            .Options;
        return new DeploymentDbContext(opts);
    }
}
