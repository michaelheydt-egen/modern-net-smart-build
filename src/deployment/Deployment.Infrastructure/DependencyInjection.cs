using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Deployment.Application.Abstractions;
using Deployment.Application.Features.Containers;
using Deployment.Application.Features.Environments;
using Deployment.Application.Features.Mappings;
using Deployment.Application.Features.Runs;
using Deployment.Application.Features.Services;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Containers;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Services;
using Deployment.Infrastructure.Gcp;
using Deployment.Infrastructure.Messaging;
using Deployment.Infrastructure.Persistence;
using Deployment.Infrastructure.Persistence.Readers;
using Deployment.Infrastructure.Persistence.Repositories;
using Deployment.Infrastructure.Steps;

namespace Deployment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDeploymentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Deployment")
            ?? throw new InvalidOperationException("ConnectionStrings:Deployment is required by Deployment.Infrastructure.");

        services.AddDbContext<DeploymentDbContext>(opts => opts.UseSqlServer(connection));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, WolverineDomainEventDispatcher>();
        services.AddSingleton(TimeProvider.System);

        // Repositories
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<IDeploymentMappingRepository, DeploymentMappingRepository>();
        services.AddScoped<IKnownContainerRepository, KnownContainerRepository>();
        services.AddScoped<IDeploymentRunRepository, DeploymentRunRepository>();

        // Readers
        services.AddScoped<IServiceReader, EfServiceReader>();
        services.AddScoped<IEnvironmentReader, EfEnvironmentReader>();
        services.AddScoped<IMappingReader, EfMappingReader>();
        services.AddScoped<IRunReader, EfRunReader>();
        services.AddScoped<IKnownContainerReader, EfKnownContainerReader>();

        // GCP adapters (ADC). Crane for Nexus→GAR, Cloud Run admin client for deploy.
        // ValidateOnStart fails the host immediately on a missing crane / bad timeouts, instead of
        // surfacing the misconfiguration as a late GarPush failure on the first deploy.
        services.AddSingleton<IValidateOptions<GoogleCloudRunOptions>, GoogleCloudRunOptionsValidator>();
        services.AddOptions<GoogleCloudRunOptions>()
            .Bind(configuration.GetSection(GoogleCloudRunOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IArtifactPromoter, CraneArtifactPromoter>();
        services.AddSingleton<ICloudRunDeployer, GoogleCloudRunDeployer>();

        // Pluggable step executors — one per DeploymentStepKind — fronted by a registry the run
        // handler resolves by kind (see IStepExecutorRegistry for why the handler can't inject the
        // executor collection directly).
        services.AddScoped<IDeploymentStepExecutor, GarPushStepExecutor>();
        services.AddScoped<IDeploymentStepExecutor, CloudRunDeployStepExecutor>();
        services.AddScoped<IStepExecutorRegistry, StepExecutorRegistry>();

        return services;
    }
}
